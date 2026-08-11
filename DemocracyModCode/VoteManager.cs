using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;

namespace DemocracyMod.DemocracyModCode;

public static class VoteManager
{
    public enum Phase { Idle, Negotiation, Voting, Resolved }

    public class VoteState
    {
        public string EntryId { get; init; } = "";
        public List<ulong> EligibleVoterIds { get; init; } = new();
        public Dictionary<ulong, ulong> Votes { get; } = new();
        public Phase CurrentPhase { get; set; } = Phase.Idle;
        public ulong? WinnerId { get; set; }
        public DateTime VoteStartTime { get; set; }
        public bool AllVoted => Votes.Count >= EligibleVoterIds.Count;

        public Dictionary<ulong, ulong> Tally()
        {
            var tally = new Dictionary<ulong, ulong>();
            foreach (var target in Votes.Values)
                tally[target] = tally.GetValueOrDefault(target, 0UL) + 1UL;
            return tally;
        }
    }

    private static readonly Dictionary<string, VoteState> VoteStates = new();
    private static readonly object LockObj = new();
    private static readonly Dictionary<ulong, HashSet<string>> Interests = new();
    private static string? _activeEntryId;
    private static Phase _currentPhase = Phase.Idle;

    public static Phase CurrentPhase => _currentPhase;
    public static string? ActiveEntryId => _activeEntryId;

    public static bool StartNextEntry(List<ulong> playerIds)
    {
        var pending = RewardPool.GetPending();
        if (pending.Count == 0) { _currentPhase = Phase.Idle; _activeEntryId = null; return false; }
        var entry = pending[0];
        _activeEntryId = entry.Id;
        var state = new VoteState { EntryId = entry.Id, EligibleVoterIds = new List<ulong>(playerIds) };
        lock (LockObj) VoteStates[entry.Id] = state;
        if (DemocracyConfig.NegotiationTimeoutSeconds > 0)
            state.CurrentPhase = _currentPhase = Phase.Negotiation;
        else BeginVoting(entry.Id);
        return true;
    }

    public static void BeginVoting(string entryId)
    {
        VoteState? state;
        lock (LockObj) VoteStates.TryGetValue(entryId, out state);
        if (state == null) return;
        state.CurrentPhase = Phase.Voting;
        state.VoteStartTime = DateTime.UtcNow;
        _currentPhase = Phase.Voting;
        var entry = RewardPool.GetEntry(entryId);
        MultiplayerCoordinator.SendVoteStart(entryId, entry?.DisplayName ?? entryId, DemocracyConfig.VoteTimeoutSeconds);
    }

    public static bool CastVote(ulong voterId, string entryId, ulong targetId)
    {
        VoteState? state;
        lock (LockObj) VoteStates.TryGetValue(entryId, out state);
        if (state == null || state.CurrentPhase != Phase.Voting) return false;
        if (!state.EligibleVoterIds.Contains(voterId)) return false;
        lock (LockObj) state.Votes[voterId] = targetId;
        if (state.AllVoted) ResolveEntry(entryId);
        return true;
    }

    public static void Update()
    {
        if (_currentPhase != Phase.Voting || _activeEntryId == null) return;
        VoteState? state;
        lock (LockObj) VoteStates.TryGetValue(_activeEntryId, out state);
        if (state == null) return;
        var elapsed = (DateTime.UtcNow - state.VoteStartTime).TotalSeconds;
        if (DemocracyConfig.VoteTimeoutSeconds > 0 && elapsed > DemocracyConfig.VoteTimeoutSeconds)
        {
            AutoCastRemaining(_activeEntryId);
            ResolveEntry(_activeEntryId);
        }
    }

    public static double GetRemainingTime()
    {
        if (_currentPhase != Phase.Voting || _activeEntryId == null) return -1;
        VoteState? state;
        lock (LockObj) VoteStates.TryGetValue(_activeEntryId, out state);
        if (state == null) return -1;
        return Math.Max(0, DemocracyConfig.VoteTimeoutSeconds - (DateTime.UtcNow - state.VoteStartTime).TotalSeconds);
    }

    public static void ExpressInterest(ulong playerId, string entryId)
    {
        if (!Interests.ContainsKey(playerId)) Interests[playerId] = new HashSet<string>();
        Interests[playerId].Add(entryId);
    }

    public static VoteState? GetVoteState(string entryId)
    {
        lock (LockObj) { VoteStates.TryGetValue(entryId, out var s); return s; }
    }

    private static void AutoCastRemaining(string entryId)
    {
        VoteState? state;
        lock (LockObj) VoteStates.TryGetValue(entryId, out state);
        if (state == null) return;
        var rng = Random.Shared;
        foreach (var voter in state.EligibleVoterIds)
            if (!state.Votes.ContainsKey(voter))
                state.Votes[voter] = DemocracyConfig.SelfishDefault ? voter : state.EligibleVoterIds[rng.Next(state.EligibleVoterIds.Count)];
    }

    private static void ResolveEntry(string entryId)
    {
        VoteState? state;
        lock (LockObj) VoteStates.TryGetValue(entryId, out state);
        if (state == null) return;
        var tally = state.Tally();
        ulong maxVotes = 0;
        var leaders = new List<ulong>();
        foreach (var (pid, count) in tally)
        {
            if (count > maxVotes) { maxVotes = count; leaders.Clear(); leaders.Add(pid); }
            else if (count == maxVotes) leaders.Add(pid);
        }
        ulong winner = leaders.Count == 1 ? leaders[0] : TieBreak(leaders);
        state.WinnerId = winner;
        state.CurrentPhase = Phase.Resolved;
        _currentPhase = Phase.Resolved;
        RewardPool.MarkDistributed(entryId, winner);
        MultiplayerCoordinator.SendVoteResult(entryId, winner, tally);
        var playerIds = MultiplayerCoordinator.GetPlayers().Select(p => p.NetId).ToList();
        if (!StartNextEntry(playerIds)) MultiplayerCoordinator.SendPoolDistributed();
    }

    private static ulong TieBreak(List<ulong> candidates)
    {
        if (candidates.Count == 1) return candidates[0];
        var fairness = DemocracyConfig.TieBreakFairness;
        var players = MultiplayerCoordinator.GetPlayers();
        var pm = players.ToDictionary(p => p.NetId, p => p);
        var wc = new Dictionary<ulong, int>();
        foreach (var c in candidates) wc[c] = pm.TryGetValue(c, out var p) ? RewardPool.PlayerWinCount[p] : 0;
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
        lock (LockObj) VoteStates.Clear();
        Interests.Clear();
        _activeEntryId = null;
        _currentPhase = Phase.Idle;
    }
}
