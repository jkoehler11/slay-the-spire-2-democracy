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
        lock (LockObj)
        {
            _claims[playerId] = new Claim { GoldAmount = goldAmount };
            _claims[playerId].RewardIds.AddRange(rewardIds);
        }
        MainFile.Logger.Info(string.Format("Democracy: claim from P{0}: {1}g + {2} rewards",
            playerId, goldAmount, rewardIds.Count));

        CheckAndResolve();
    }

    private static void CheckAndResolve()
    {
        int seen = CombatRewardPatch.GetSeenPlayerCount();
        int submitted;
        lock (LockObj) submitted = _claims.Count;

        if (seen < 1) return;
        if (submitted < seen) return;

        ResolveClaims();
    }

    private static void ResolveClaims()
    {
        if (_resolutionDone) return;
        _resolutionDone = true;

        var playerIds = CombatRewardPatch.GetSeenPlayerIds();
        if (playerIds.Count == 0) return;

        MainFile.Logger.Info(string.Format("Democracy: RESOLVING CLAIMS — {0} players", playerIds.Count));

        // --- Gold distribution ---
        // Gold was already granted to each source player during auto-pick. Reclaim
        // it first so the whole pool is redistributed from scratch per the vote.
        var goldEntries = RewardPool.GetPending().Where(e => e.Type == RewardPool.PoolEntry.RewardType.GoldPile).ToList();
        var autoGold = new Dictionary<ulong, int>();
        foreach (var g in goldEntries)
            autoGold[g.SourcePlayerId] = autoGold.GetValueOrDefault(g.SourcePlayerId, 0) + g.GoldAmount;
        foreach (var kv in autoGold)
            RewardPool.RemoveGold(kv.Key, kv.Value);

        int totalGold = RewardPool.TotalGoldPooled;
        int totalClaimedGold = 0;
        lock (LockObj)
            foreach (var c in _claims.Values) totalClaimedGold += c.GoldAmount;

        MainFile.Logger.Info(string.Format("Democracy: gold — pool {0}g, claimed {1}g", totalGold, totalClaimedGold));

        if (totalGold > 0)
        {
            for (var i = 0; i < playerIds.Count; i++)
            {
                var pid = playerIds[i];
                int claimed;
                lock (LockObj) claimed = _claims.TryGetValue(pid, out var c) ? c.GoldAmount : 0;

                int grant;
                if (totalClaimedGold <= totalGold)
                {
                    // Give everyone what they asked for; split any leftover (or the
                    // whole pool, if nobody claimed) evenly, remainder to first N.
                    int leftover = totalGold - totalClaimedGold;
                    grant = claimed + leftover / playerIds.Count + (i < leftover % playerIds.Count ? 1 : 0);
                }
                else
                {
                    // Over-claimed: scale proportionally.
                    grant = totalClaimedGold > 0 ? (int)((long)claimed * totalGold / totalClaimedGold) : 0;
                }

                if (grant > 0)
                    RewardPool.GrantGold(pid, grant);
            }
        }

        // --- Non-gold rewards: grant to the winner (or discard) ---
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
                    // Already granted to the source during auto-pick — keep it.
                    RewardPool.MarkDistributed(entry.Id, winner);
                    MainFile.Logger.Info(string.Format("Democracy: {0} -> P{1} (uncontested, already owned)", entry.DisplayName, winner));
                }
                else
                {
                    RewardPool.TransferReward(entry, winner);
                    RewardPool.MarkDistributed(entry.Id, winner);
                    MainFile.Logger.Info(string.Format("Democracy: {0} -> P{1} (uncontested, transferred)", entry.DisplayName, winner));
                }
            }
            else if (claimants.Count > 1)
            {
                var winner = TieBreak(claimants);
                if (winner == entry.SourcePlayerId)
                {
                    RewardPool.MarkDistributed(entry.Id, winner);
                }
                else
                {
                    RewardPool.TransferReward(entry, winner);
                    RewardPool.MarkDistributed(entry.Id, winner);
                }
                MainFile.Logger.Info(string.Format("Democracy: {0} -> P{1} ({2} claimants, tie-broken)", entry.DisplayName, winner, claimants.Count));
            }
            else
            {
                // Unclaimed: discard.
                RewardPool.DiscardReward(entry);
                RewardPool.MarkDiscarded(entry.Id);
                MainFile.Logger.Info(string.Format("Democracy: {0} unclaimed — discarded.", entry.DisplayName));
            }
        }

        // Gold pool entries: mark resolved (gold was already granted above).
        foreach (var entry in RewardPool.GetPending())
            if (entry.Type == RewardPool.PoolEntry.RewardType.GoldPile)
                RewardPool.MarkDiscarded(entry.Id);

        MainFile.Logger.Info("Democracy: distribution complete.");
        MultiplayerCoordinator.SendPoolDistributed();
    }

    private static ulong TieBreak(List<ulong> candidates)
    {
        if (candidates.Count == 1) return candidates[0];
        // Fairness: players with fewer wins get a bonus weight
        var fairness = DemocracyConfig.TieBreakFairness;
        var wc = new Dictionary<ulong, int>();
        foreach (var c in candidates)
            wc[c] = RewardPool.PlayerWinCount.GetValueOrDefault(c, 0);
        var maxW = wc.Values.Max();
        var weights = candidates.Select(c => 1.0 + fairness * (maxW - wc[c])).ToList();
        var total = weights.Sum();
        var roll = Random.Shared.NextDouble() * total;
        var cum = 0.0;
        for (var i = 0; i < candidates.Count; i++) { cum += weights[i]; if (roll <= cum) return candidates[i]; }
        return candidates[^1];
    }

    public static void Reset()
    {
        lock (LockObj) _claims.Clear();
        _resolutionDone = false;
    }
}
