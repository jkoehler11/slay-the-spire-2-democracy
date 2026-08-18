using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;
using DemocracyMod.DemocracyModCode.Patches;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Stage-synchronized claim coordination. The reward flow is now four sequential
/// STAGES — gold (0), potions (1), relics (2), cards (3) — and every player must
/// submit their vote for the current stage before ANYONE advances to the next.
///
/// Host-authoritative: only the host decides when a stage is complete. On each
/// SubmitStage it checks whether every player has voted for the current stage, then
/// broadcasts a DemocracyAdvanceMessage (or, after the final cards stage, resolves
/// the whole distribution and broadcasts a DemocracyResolvedMessage). Clients never
/// resolve on their own — they apply the host's exact decision and show results.
/// </summary>
public static class VoteManager
{
    public class Claim
    {
        public int GoldMode { get; set; } = -1;
        public List<string> RewardIds { get; } = new();
    }

    /// <summary>The host's authoritative distribution decision (also the wire format).</summary>
    public class Resolution
    {
        public List<string> EntryIds = new();        // non-gold reward entry ids, in order
        public List<ulong> WinnerIds = new();        // parallel; 0 == discarded
        public List<ulong> ReclaimPlayerIds = new(); // gold reclaimed from each source
        public List<int> ReclaimAmounts = new();
        public List<ulong> GoldPlayerIds = new();    // gold granted to each player
        public List<int> GoldAmounts = new();
        public int GoldMode = (int)GoldVoteMode.OriginalAmount; // winning gold mode
    }

    private static readonly object LockObj = new();

    // Per-stage, per-player votes: _stageVotes[stage][playerId] = vote.
    private static readonly Dictionary<int, Dictionary<ulong, Claim>> _stageVotes = new();
    // Accumulated final answers across stages (for the end-of-flow resolution).
    private static readonly Dictionary<ulong, int> _goldModes = new();
    private static readonly Dictionary<ulong, List<string>> _rewardIds = new();

    private static int _currentStage = 0;
    private static bool _advanced;
    private static volatile bool _resolutionDone;

    public static bool ResolutionDone => _resolutionDone;

    public static void BeginFlow()
    {
        lock (LockObj)
        {
            _stageVotes.Clear();
            _goldModes.Clear();
            _rewardIds.Clear();
            _currentStage = 0;
            _advanced = false;
            _resolutionDone = false;
        }
    }

    /// <summary>Sets the current stage directly (used by DemocracyFlow.Start to begin at
    /// the first non-empty stage).</summary>
    public static void SetCurrentStage(int stage)
    {
        lock (LockObj)
        {
            _currentStage = stage;
            _advanced = false;
        }
    }

    public static void SubmitStage(ulong playerId, int stage, int goldMode, List<string> rewardIds)
    {
        lock (LockObj)
        {
            if (!_stageVotes.TryGetValue(stage, out var d)) { d = new(); _stageVotes[stage] = d; }
            if (!d.TryGetValue(playerId, out var c)) { c = new Claim(); d[playerId] = c; }
            c.GoldMode = goldMode;
            c.RewardIds.Clear();
            c.RewardIds.AddRange(rewardIds);

            if (goldMode >= 0) _goldModes[playerId] = goldMode;
            if (rewardIds.Count > 0)
            {
                if (!_rewardIds.TryGetValue(playerId, out var l)) { l = new(); _rewardIds[playerId] = l; }
                foreach (var id in rewardIds)
                    if (!l.Contains(id)) l.Add(id);
            }
        }

        MainFile.LogVote(string.Format("Democracy: stage {0} vote from P{1}: goldMode={2} + [{3}]",
            stage, playerId, goldMode, string.Join(", ", rewardIds)));

        CheckAdvance(stage);
    }

    private static void CheckAdvance(int stage)
    {
        if (!MultiplayerCoordinator.IsHost) return;

        lock (LockObj)
        {
            if (stage != _currentStage) return;   // stale vote for an earlier stage
            if (_advanced) return;                // already advanced this stage
            int seen = CombatRewardPatch.GetSeenPlayerCount();
            int voted = _stageVotes.TryGetValue(stage, out var d) ? d.Count : 0;
            if (seen < 1 || voted < seen) return;
            _advanced = true;
        }

        MainFile.LogVote(string.Format("Democracy: stage {0} complete ({1} players) — advancing.",
            _currentStage, CombatRewardPatch.GetSeenPlayerCount()));

        int next = DemocracyFlow.NextStageWithLoot(_currentStage);
        if (next < 0)
        {
            _ = ResolveAndBroadcastAsync();
        }
        else
        {
            // Host advances its own UI directly (it does not rely on receiving its own
            // broadcast); clients advance when they receive the broadcast.
            AdvanceTo(next);
            MultiplayerCoordinator.SendAdvance(next);
        }
    }

    /// <summary>Everyone advances to the next stage's screen (host and clients alike).
    /// Idempotent — a host that also receives its own advance broadcast re-enters here
    /// and is a no-op because the stage already matches.</summary>
    public static void AdvanceTo(int nextStage)
    {
        lock (LockObj)
        {
            if (nextStage == _currentStage) return;
            _currentStage = nextStage;
            _advanced = false;
        }
        DemocracyFlow.ShowStage(nextStage);
    }

    // ---- Host-authoritative resolution (after the final cards stage) ----
    private static async Task ResolveAndBroadcastAsync()
    {
        if (_resolutionDone) return;
        _resolutionDone = true;

        var resolution = ComputeResolution();
        if (resolution == null) return;

        await ApplyResolutionAsync(resolution);

        MultiplayerCoordinator.SendResolved(resolution);
    }

    /// <summary>Client path: apply the host's broadcast decision verbatim.</summary>
    public static void ApplyResolved(DemocracyResolvedMessage msg)
    {
        if (_resolutionDone) return;   // host already applied its own decision
        _resolutionDone = true;

        var resolution = new Resolution
        {
            EntryIds = msg.EntryIds,
            WinnerIds = msg.WinnerIds,
            ReclaimPlayerIds = msg.ReclaimPlayerIds,
            ReclaimAmounts = msg.ReclaimAmounts,
            GoldPlayerIds = msg.GoldPlayerIds,
            GoldAmounts = msg.GoldAmounts,
            GoldMode = msg.GoldMode,
        };
        _ = ApplyResolutionAsync(resolution);
    }

    private static Resolution? ComputeResolution()
    {
        var playerIds = CombatRewardPatch.GetSeenPlayerIds();
        if (playerIds.Count == 0) return null;

        // Reset the per-player win tally at the start of every resolve so the tie-break
        // is deterministic regardless of run history.
        RewardPool.ResetWinCounts();

        MainFile.LogVote(string.Format("Democracy: RESOLVING CLAIMS — {0} players (host)", playerIds.Count));

        var resolution = new Resolution();

        // --- Gold: plurality vote among OriginalAmount / Randomized / DistributeEvenly ---
        int totalGold = RewardPool.TotalGoldPooled;
        if (totalGold > 0)
        {
            var tally = new Dictionary<int, int>();
            lock (LockObj)
                foreach (var kv in _goldModes)
                    tally[kv.Value] = tally.GetValueOrDefault(kv.Value, 0) + 1;

            int winningMode = DecideGoldMode(tally);
            resolution.GoldMode = winningMode;
            MainFile.LogVote(string.Format("Democracy: gold vote -> {0} ({1}g)",
                (GoldVoteMode)winningMode, totalGold));

            if (winningMode != (int)GoldVoteMode.OriginalAmount)
            {
                var autoGold = new Dictionary<ulong, int>();
                var goldEntries = RewardPool.GetPending()
                    .Where(e => e.Type == RewardPool.PoolEntry.RewardType.GoldPile).ToList();
                foreach (var g in goldEntries)
                    autoGold[g.SourcePlayerId] = autoGold.GetValueOrDefault(g.SourcePlayerId, 0) + g.GoldAmount;

                foreach (var kv in autoGold.OrderBy(kv => kv.Key))
                {
                    resolution.ReclaimPlayerIds.Add(kv.Key);
                    resolution.ReclaimAmounts.Add(kv.Value);
                }

                var grants = winningMode == (int)GoldVoteMode.DistributeEvenly
                    ? SplitEvenly(playerIds, totalGold)
                    : SplitRandomized(playerIds, totalGold);

                foreach (var kv in grants.OrderBy(kv => kv.Key))
                {
                    if (kv.Value <= 0) continue;
                    resolution.GoldPlayerIds.Add(kv.Key);
                    resolution.GoldAmounts.Add(kv.Value);
                }
            }
        }

        // --- Non-gold rewards: winner (or discard) per entry ---
        var nonGold = RewardPool.GetNonGoldPending();
        foreach (var entry in nonGold)
        {
            var claimants = new List<ulong>();
            lock (LockObj)
                foreach (var kv in _rewardIds)
                    if (kv.Value.Contains(entry.Id))
                        claimants.Add(kv.Key);

            ulong winner;
            if (claimants.Count == 1)
                winner = claimants[0];
            else if (claimants.Count > 1)
                winner = TieBreak(entry.Id, claimants);
            else
                winner = entry.SourcePlayerId;   // unclaimed — kept by source

            resolution.EntryIds.Add(entry.Id);
            resolution.WinnerIds.Add(winner);
        }

        return resolution;
    }

    /// <summary>Plurality vote for the gold mode, with a deterministic tie-break.</summary>
    private static int DecideGoldMode(Dictionary<int, int> tally)
    {
        if (tally.Count == 0) return (int)GoldVoteMode.OriginalAmount;
        int max = tally.Values.Max();
        var tied = tally.Where(kv => kv.Value == max).Select(kv => kv.Key).OrderBy(k => k).ToList();
        if (tied.Count == 1) return tied[0];

        // Deterministic FNV-1a over the tied modes + sorted player ids + total gold.
        uint h = 2166136261u;
        foreach (var m in tied) h = (h ^ (uint)m) * 16777619u;
        foreach (var id in CombatRewardPatch.GetSeenPlayerIds())
            for (var k = 0; k < 8; k++) h = (h ^ (byte)(id >> (8 * k))) * 16777619u;
        h = (h ^ (uint)RewardPool.TotalGoldPooled) * 16777619u;
        return tied[(int)(h % (uint)tied.Count)];
    }

    /// <summary>Even split with a deterministic remainder (lowest-indexed players get the extra 1g).</summary>
    private static Dictionary<ulong, int> SplitEvenly(List<ulong> playerIds, int totalGold)
    {
        var result = new Dictionary<ulong, int>();
        foreach (var id in playerIds) result[id] = 0;
        int per = totalGold / playerIds.Count;
        int rem = totalGold % playerIds.Count;
        for (var i = 0; i < playerIds.Count; i++)
            result[playerIds[i]] = per + (i < rem ? 1 : 0);
        return result;
    }

    /// <summary>
    /// "Randomized" split. Fully deterministic (no Random / DateTime / GetHashCode): the
    /// players are shuffled with an FNV-1a stream seeded by the gold total + sorted ids,
    /// then gold is dealt in 1..10g chunks around that shuffled order.
    /// </summary>
    private static Dictionary<ulong, int> SplitRandomized(List<ulong> playerIds, int totalGold)
    {
        var result = new Dictionary<ulong, int>();
        foreach (var id in playerIds) result[id] = 0;

        var order = playerIds.OrderBy(id => id).ToList();

        uint h = 2166136261u;
        h = (h ^ (uint)totalGold) * 16777619u;
        foreach (var id in order)
            for (var k = 0; k < 8; k++) h = (h ^ (byte)(id >> (8 * k))) * 16777619u;

        for (var i = order.Count - 1; i > 0; i--)
        {
            h = (h ^ 0x9e3779b9u) * 16777619u;
            var j = (int)(h % (uint)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        int remaining = totalGold;
        int idx = 0;
        int step = 0;
        while (remaining > 0)
        {
            ulong pid = order[idx % order.Count];
            h = (h ^ (uint)(pid ^ (ulong)step)) * 16777619u;
            int chunk = 1 + (int)(h % 10u);
            if (chunk > remaining) chunk = remaining;
            result[pid] += chunk;
            remaining -= chunk;
            idx++;
            step++;
        }
        return result;
    }

    private static async Task ApplyResolutionAsync(Resolution resolution)
    {
        // Gold: reclaim first, then grant (must match the host's order/amounts exactly).
        for (var i = 0; i < resolution.ReclaimPlayerIds.Count; i++)
            RewardPool.RemoveGold(resolution.ReclaimPlayerIds[i], resolution.ReclaimAmounts[i]);
        for (var i = 0; i < resolution.GoldPlayerIds.Count; i++)
            RewardPool.GrantGold(resolution.GoldPlayerIds[i], resolution.GoldAmounts[i]);

        // Disable grant capture during transfers — the synced transfer/discard commands
        // move cards/potions/relics and must not be mistaken for fresh reward grants.
        RewardPool.IsRewardPhaseActive = false;

        var results = new List<string>();

        for (var i = 0; i < resolution.EntryIds.Count; i++)
        {
            var entry = RewardPool.GetEntry(resolution.EntryIds[i]);
            if (entry == null) continue;
            var winner = resolution.WinnerIds[i];

            if (winner == 0)
            {
                await RewardPool.DiscardReward(entry);
                RewardPool.MarkDiscarded(entry.Id);
                results.Add(string.Format("{0} → discarded", entry.DisplayName));
                MainFile.LogVote(string.Format("Democracy: {0} discarded.", entry.DisplayName));
            }
            else if (winner == entry.SourcePlayerId)
            {
                RewardPool.MarkDistributed(entry.Id, winner);
                results.Add(string.Format("{0} → {1}", entry.DisplayName, PlayerLabel(winner)));
                MainFile.LogVote(string.Format("Democracy: {0} -> P{1} (uncontested, already owned)", entry.DisplayName, winner));
            }
            else
            {
                await RewardPool.TransferReward(entry, winner);
                RewardPool.MarkDistributed(entry.Id, winner);
                results.Add(string.Format("{0} → {1}", entry.DisplayName, PlayerLabel(winner)));
                MainFile.LogVote(string.Format("Democracy: transferred {0} P{1} -> P{2}", entry.DisplayName, entry.SourcePlayerId, winner));
            }
        }

        // Gold pile entries are consumed into the shared pool — mark them discarded.
        foreach (var entry in RewardPool.GetPending())
            if (entry.Type == RewardPool.PoolEntry.RewardType.GoldPile)
                RewardPool.MarkDiscarded(entry.Id);

        // Gold grants (net) for the results UI.
        for (var i = 0; i < resolution.GoldPlayerIds.Count; i++)
            results.Add(string.Format("{0}g → {1}", resolution.GoldAmounts[i], PlayerLabel(resolution.GoldPlayerIds[i])));

        MainFile.LogVote("Democracy: distribution complete.");
        CombatRewardPatch.RefreshDeckCount();

        PostCombatPatch.OnDistributionComplete(results);
    }

    public static string PlayerLabel(ulong id)
    {
        var ids = CombatRewardPatch.GetSeenPlayerIds();
        int idx = ids.IndexOf(id);
        var label = idx >= 0 ? string.Format("P{0}", idx + 1) : id.ToString();
        if (id == MultiplayerCoordinator.LocalPlayerId) label += " (You)";
        return label;
    }

    private static ulong TieBreak(string rewardId, List<ulong> candidates)
    {
        if (candidates.Count == 1) return candidates[0];
        // DETERMINISTIC tie-break. Random.Shared is per-process and desyncs the sim.
        var sorted = candidates.OrderBy(id => id).ToList();
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
        lock (LockObj)
        {
            _stageVotes.Clear();
            _goldModes.Clear();
            _rewardIds.Clear();
        }
        _resolutionDone = false;
    }
}
