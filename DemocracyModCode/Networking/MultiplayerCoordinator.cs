using BaseLib.Abstracts;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Combat;
using HarmonyLib;

namespace DemocracyMod.DemocracyModCode.Networking;

public static class MultiplayerCoordinator
{
    public static bool IsHost { get; private set; }
    public static ulong LocalPlayerId { get; private set; }

    public static void Send<T>(T msg) where T : ICustomMessage
    {
        var ns = RunManager.Instance?.NetService;
        if (ns != null) CustomMessageWrapper.Send(msg, ns);
    }

    public static void BroadcastPoolUpdate()
    {
        var pending = RewardPool.GetPending();
        var msg = new DemocracyPoolUpdateMessage { EntryCount = pending.Count, TotalGoldPooled = RewardPool.TotalGoldPooled };
        foreach (var e in pending) msg.Entries.Add((e.Id, e.Type.ToString(), e.SourcePlayerId, e.DisplayName, e.GoldAmount));
        Send(msg);
    }

    public static void SendVoteStart(string entryId, string displayName, int timeoutSeconds)
        => Send(new DemocracyVoteStartMessage { RewardId = entryId, RewardName = displayName, TimeoutSeconds = timeoutSeconds });

    public static void SendVoteResult(string entryId, ulong winnerId, Dictionary<ulong, ulong> tally)
    {
        var msg = new DemocracyVoteResultMessage { RewardId = entryId, WinnerId = winnerId, VoteCount = tally.Count };
        foreach (var (v, t) in tally) msg.Votes.Add((v, t));
        Send(msg);
    }

    public static void SendPoolDistributed() => Send(new DemocracyPoolDistributedMessage());
    public static void SendVote(string entryId, ulong targetPlayerId) => Send(new DemocracyVoteCastMessage { RewardId = entryId, TargetPlayerId = targetPlayerId });
    public static void SendInterest(string entryId) => Send(new DemocracyInterestMessage { RewardId = entryId });

    internal static void HandlePoolUpdate(DemocracyPoolUpdateMessage msg) { }
    internal static void HandleVoteStart(DemocracyVoteStartMessage msg) { }
    internal static void HandleVoteResult(DemocracyVoteResultMessage msg)
    {
        RewardPool.MarkDistributed(msg.RewardId, msg.WinnerId);
    }

    public static IReadOnlyList<Player> GetPlayers()
    {
        var cs = CombatManager.Instance?.State;
        if (cs != null) return cs.PlayerCreatures.Select(c => c.Player!).Where(p => p != null).ToList()!;
        return Array.Empty<Player>();
    }

    public static int GetPlayerCount() => GetPlayers().Count;

    public static void InitializeForRun()
    {
        var pt = (SteamInitializer.Initialized && !MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("fastmp")) ? PlatformType.Steam : PlatformType.None;
        LocalPlayerId = PlatformUtil.GetLocalPlayerId(pt);
        var players = GetPlayers();
        IsHost = players.Count > 0 && players[0].NetId == LocalPlayerId;
    }
}
