using BaseLib.Abstracts;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;

namespace DemocracyMod.DemocracyModCode.Networking;

public static class MultiplayerCoordinator
{
    public static ulong LocalPlayerId { get; private set; }

    public static void Send<T>(T msg) where T : ICustomMessage
    {
        var ns = RunManager.Instance?.NetService;
        if (ns != null) CustomMessageWrapper.Send(msg, ns);
    }

    /// <summary>A player's vote for one stage (gold mode or reward ids).</summary>
    public static void SendStage(int stage, int goldMode, List<string> rewardIds)
        => Send(new DemocracyStageMessage { Stage = stage, GoldMode = goldMode, RewardIds = rewardIds });

    /// <summary>Host: every player has voted — advance everyone to the next stage.</summary>
    public static void SendAdvance(int nextStage)
        => Send(new DemocracyAdvanceMessage { NextStage = nextStage });

    public static void SendResolved(VoteManager.Resolution r)
        => Send(new DemocracyResolvedMessage
        {
            EntryIds = r.EntryIds,
            WinnerIds = r.WinnerIds,
            ReclaimPlayerIds = r.ReclaimPlayerIds,
            ReclaimAmounts = r.ReclaimAmounts,
            GoldPlayerIds = r.GoldPlayerIds,
            GoldAmounts = r.GoldAmounts,
            GoldMode = r.GoldMode,
        });

    /// <summary>Host: broadcast the authoritative gameplay config so all clients follow
    /// the host's settings (non-deterministic local config diverges the reward flow).</summary>
    public static void SendConfig()
        => Send(new DemocracyConfigMessage
        {
            ShowGoldScreen = DemocracyConfig.ShowGoldScreen,
            ShowPotionsScreen = DemocracyConfig.ShowPotionsScreen,
            ShowRelicsScreen = DemocracyConfig.ShowRelicsScreen,
            ShowCardsScreen = DemocracyConfig.ShowCardsScreen,
            ShowResultsPanel = DemocracyConfig.ShowResultsPanel,
            EnableAncients = DemocracyConfig.EnableAncients,
            TieBreakFairness = DemocracyConfig.TieBreakFairness,
        });

    /// <summary>Broadcast this player's current live selection for the current stage
    /// (cosmetic icon display only — the authoritative vote is SendStage).</summary>
    public static void SendSelection(int stage, List<string> selectedIds)
        => Send(new DemocracySelectionMessage { Stage = stage, SelectedIds = selectedIds });

    internal static void HandleStage(ulong senderId, DemocracyStageMessage msg)
        => VoteManager.SubmitStage(senderId, msg.Stage, msg.GoldMode, msg.RewardIds);

    internal static void HandleAdvance(ulong senderId, DemocracyAdvanceMessage msg)
        => VoteManager.AdvanceTo(msg.NextStage);

    internal static void HandleResolved(ulong senderId, DemocracyResolvedMessage msg)
        => VoteManager.ApplyResolved(msg);

    internal static void HandleSelection(ulong senderId, DemocracySelectionMessage msg)
        => DemocracyFlow.ApplyRemoteSelection(senderId, msg.Stage, msg.SelectedIds);

    internal static void HandleConfig(ulong senderId, DemocracyConfigMessage msg)
        => HostConfig.ApplyRemote(msg.ShowGoldScreen, msg.ShowPotionsScreen, msg.ShowRelicsScreen,
            msg.ShowCardsScreen, msg.ShowResultsPanel, msg.EnableAncients, msg.TieBreakFairness);

    public static void InitializeForRun()
    {
        var pt = (SteamInitializer.Initialized && !MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("fastmp"))
            ? PlatformType.Steam : PlatformType.None;
        LocalPlayerId = PlatformUtil.GetLocalPlayerId(pt);
    }

    /// <summary>True if this machine controls the first player in the run (the host).</summary>
    public static bool IsHost
    {
        get
        {
            if (LocalPlayerId == 0) InitializeForRun();
            try
            {
                var players = RunManager.Instance?.State?.Players;
                if (players == null || players.Count == 0) return false;
                return players[0].NetId == LocalPlayerId;
            }
            catch { return false; }
        }
    }
}
