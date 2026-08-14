using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;
using DemocracyMod.DemocracyModCode.Patches;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Claim-based distribution. Each player claims the rewards they want
/// (checkbox for indivisible rewards, gold amount for the gold pool).
/// Host/any client resolves deterministically once all players submit.
/// </summary>
public static class VoteManager
{
    public class Claim
    {
        public int GoldAmount { get; set; }
        public List<string> RewardIds { get; } = new();
    }

    private static readonly Dictionary<ulong, Claim> _claims = new();
    private static readonly object LockObj = new();
    private static volatile bool _resolutionDone;

    public static bool ResolutionDone => _resolutionDone;
    public static bool HasSubmitted(ulong playerId) { lock (LockObj) return _claims.ContainsKey(playerId); }

    public static void SubmitClaim(ulong playerId, int goldAmount, List<string> rewardIds)
    {
        // Dead players may not vote when DeadCanVote is disabled.
        if (!DemocracyConfig.DeadCanVote && IsDeadPlayer(playerId))
        {
            MainFile.LogVote(string.Format("Democracy: claim from P{0} ignored (dead).", playerId));
            return;
        }

        lock (LockObj)
        {
            _claims[playerId] = new Claim { GoldAmount = goldAmount };
            _claims[playerId].RewardIds.AddRange(rewardIds);
        }

        if (DemocracyConfig.OpenVoting)
            MainFile.LogVote(string.Format("Democracy: claim from P{0}: {1}g + [{2}]",
                playerId, goldAmount, string.Join(", ", rewardIds)));
        else
            MainFile.LogVote(string.Format("Democracy: claim from P{0}: {1}g + {2} reward(s)",
                playerId, goldAmount, rewardIds.Count));

        CheckAndResolve();
    }

    private static bool IsDeadPlayer(ulong playerId)
    {
        try
        {
            var p = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.State?.GetPlayer(playerId);
            return p?.Creature?.IsDead == true;
        }
        catch { return false; }
    }

    private static void CheckAndResolve()
    {
        int seen = CombatRewardPatch.GetSeenPlayerCount();
        int submitted;
        lock (LockObj) submitted = _claims.Count;

        if (seen < 1) return;
        if (submitted < seen) return;

        _ = ResolveClaimsAsync();
    }


    private static async Task ResolveClaimsAsync()
    {
        if (_resolutionDone) return;
        _resolutionDone = true;

        var playerIds = CombatRewardPatch.GetSeenPlayerIds();
        if (playerIds.Count == 0) return;

        MainFile.LogVote(string.Format("Democracy: RESOLVING CLAIMS — {0} players", playerIds.Count));

        // --- Gold distribution ---
        var goldEntries = RewardPool.GetPending().Where(e => e.Type == RewardPool.PoolEntry.RewardType.GoldPile).ToList();
        var autoGold = new Dictionary<ulong, int>();
        foreach (var g in goldEntries)
            autoGold[g.SourcePlayerId] = autoGold.GetValueOrDefault(g.SourcePlayerId, 0) + g.GoldAmount;

        int totalGold = RewardPool.TotalGoldPooled;
        int totalClaimedGold = 0;
        lock (LockObj)
            foreach (var c in _claims.Values) totalClaimedGold += c.GoldAmount;

        MainFile.LogVote(string.Format("Democracy: gold — pool {0}g, claimed {1}g", totalGold, totalClaimedGold));

        if (totalGold > 0)
        {
            if (DemocracyConfig.RewardSelection == RewardSelectionMode.KeepOwnRewards && totalClaimedGold == 0)
            {
                // Default: nobody claimed gold, so each player keeps the exact amount they
                // earned (already granted at auto-pick). No reclaim, no even split.
                MainFile.LogVote(string.Format("Democracy: gold — keep own ({0}g, no claims).", totalGold));
            }
            else
            {
                // Reclaim every player's gold, then re-grant per claims (with an even
                // split of any unclaimed leftover).
                foreach (var kv in autoGold)
                    RewardPool.RemoveGold(kv.Key, kv.Value);

                for (var i = 0; i < playerIds.Count; i++)
                {
                    var pid = playerIds[i];
                    int claimed;
                    lock (LockObj) claimed = _claims.TryGetValue(pid, out var c) ? c.GoldAmount : 0;

                    int grant;
                    if (totalClaimedGold <= totalGold)
                    {
                        int leftover = totalGold - totalClaimedGold;
                        grant = claimed + leftover / playerIds.Count + (i < leftover % playerIds.Count ? 1 : 0);
                    }
                    else
                    {
                        grant = totalClaimedGold > 0 ? (int)((long)claimed * totalGold / totalClaimedGold) : 0;
                    }

                    if (grant > 0)
                        RewardPool.GrantGold(pid, grant);
                }
            }
        }

        // --- Non-gold rewards: grant to the winner (or discard) ---
        // Disable grant capture during transfers — the synced transfer/discard
        // commands move cards/potions/relics and would otherwise be mistaken for
        // fresh reward grants (polluting the pending-grant queue).
        RewardPool.IsRewardPhaseActive = false;
        var nonGold = RewardPool.GetNonGoldPending();
        foreach (var entry in nonGold)
        {
            var claimants = new List<ulong>();
            lock (LockObj)
                foreach (var kv in _claims)
                    if (kv.Value.RewardIds.Contains(entry.Id))
                        claimants.Add(kv.Key);

            if (claimants.Count == 1)
            {
                var winner = claimants[0];
                if (winner == entry.SourcePlayerId)
                {
                    // Diagnostic: verify the "already owned" reward is actually in the winner's inventory.
                    if (entry.Type == RewardPool.PoolEntry.RewardType.CardReward && entry.Card != null)
                    {
                        var w = CombatRewardPatch.GetPlayer(winner);
                        bool inDeck = w?.Deck?.Cards?.Any(c => ReferenceEquals(c, entry.Card)) ?? false;
                        MainFile.LogDebug(string.Format(
                            "Democracy: OWN-CHECK {0} inWinnerDeck={1} cardOwner={2} winnerId={3}",
                            entry.DisplayName, inDeck, entry.Card.Owner?.NetId ?? 0, winner));
                    }
                    RewardPool.MarkDistributed(entry.Id, winner);
                    MainFile.LogVote(string.Format("Democracy: {0} -> P{1} (uncontested, already owned)", entry.DisplayName, winner));
                }
                else
                {
                    await RewardPool.TransferReward(entry, winner);
                    RewardPool.MarkDistributed(entry.Id, winner);
                    MainFile.LogVote(string.Format("Democracy: {0} -> P{1} (uncontested, transferred)", entry.DisplayName, winner));
                }
            }
            else if (claimants.Count > 1)
            {
                var winner = TieBreak(entry.Id, claimants);
                if (winner == entry.SourcePlayerId)
                {
                    RewardPool.MarkDistributed(entry.Id, winner);
                }
                else
                {
                    await RewardPool.TransferReward(entry, winner);
                    RewardPool.MarkDistributed(entry.Id, winner);
                }
                MainFile.LogVote(string.Format("Democracy: {0} -> P{1} ({2} claimants, tie-broken)", entry.DisplayName, winner, claimants.Count));
            }
            else
            {
                if (DemocracyConfig.RewardSelection == RewardSelectionMode.KeepOwnRewards)
                {
                    // Default: unclaimed rewards stay with whoever earned them.
                    RewardPool.MarkDistributed(entry.Id, entry.SourcePlayerId);
                    MainFile.LogVote(string.Format("Democracy: {0} unclaimed — kept by P{1}.", entry.DisplayName, entry.SourcePlayerId));
                }
                else
                {
                    await RewardPool.DiscardReward(entry);
                    RewardPool.MarkDiscarded(entry.Id);
                    MainFile.LogVote(string.Format("Democracy: {0} unclaimed — discarded.", entry.DisplayName));
                }
            }
        }

        foreach (var entry in RewardPool.GetPending())
            if (entry.Type == RewardPool.PoolEntry.RewardType.GoldPile)
                RewardPool.MarkDiscarded(entry.Id);

        MainFile.LogVote("Democracy: distribution complete.");
        CombatRewardPatch.RefreshDeckCount();
        MultiplayerCoordinator.SendPoolDistributed();
    }


    private static ulong TieBreak(string rewardId, List<ulong> candidates)
    {
        if (candidates.Count == 1) return candidates[0];
        // DETERMINISTIC tie-break. Random.Shared is per-process (each machine has its
        // own seed), so it flipped ties in opposite directions on the two machines and
        // desynced the deterministic simulation. Sort the candidates and roll from a
        // stable hash of the reward id + candidate ids instead.
        var sorted = candidates.OrderBy(id => id).ToList();
        // Fairness: players with fewer wins get a bonus weight
        var fairness = DemocracyConfig.TieBreakFairness;
        var wc = new Dictionary<ulong, int>();
        foreach (var c in sorted)
            wc[c] = RewardPool.PlayerWinCount.GetValueOrDefault(c, 0);
        var maxW = wc.Values.Max();
        var weights = sorted.Select(c => 1.0 + fairness * (maxW - wc[c])).ToList();
        var total = weights.Sum();
        var roll = DeterministicRoll(rewardId, sorted) * total;
        var cum = 0.0;
        for (var i = 0; i < sorted.Count; i++) { cum += weights[i]; if (roll <= cum) return sorted[i]; }
        return sorted[^1];
    }

    private static double DeterministicRoll(string rewardId, List<ulong> sorted)
    {
        // FNV-1a over the reward id + each candidate NetId, normalized to [0,1).
        uint h = 2166136261u;
        foreach (var ch in rewardId)
            h = (h ^ (uint)ch) * 16777619u;
        foreach (var id in sorted)
            for (var k = 0; k < 8; k++)
                h = (h ^ (byte)(id >> (8 * k))) * 16777619u;
        return (h & 0xFFFFFFu) / 16777216.0;
    }

    public static void Reset()
    {
        lock (LockObj) _claims.Clear();
        _resolutionDone = false;
    }
}
