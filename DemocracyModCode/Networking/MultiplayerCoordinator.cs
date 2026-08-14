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
    public static ulong LocalPlayerId { get; private set; }

    public static void Send<T>(T msg) where T : ICustomMessage
    {
        var ns = RunManager.Instance?.NetService;
        if (ns != null) CustomMessageWrapper.Send(msg, ns);
    }

    public static void SendClaim(int goldAmount, List<string> rewardIds)
        => Send(new DemocracyClaimMessage { GoldAmount = goldAmount, RewardIds = rewardIds });

    public static void SendPoolDistributed() => Send(new DemocracyPoolDistributedMessage());

    internal static void HandleClaim(ulong senderId, DemocracyClaimMessage msg)
    {
        VoteManager.SubmitClaim(senderId, msg.GoldAmount, msg.RewardIds);
    }

    public static void InitializeForRun()
    {
        var pt = (SteamInitializer.Initialized && !MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("fastmp")) ? PlatformType.Steam : PlatformType.None;
        LocalPlayerId = PlatformUtil.GetLocalPlayerId(pt);
    }
}
