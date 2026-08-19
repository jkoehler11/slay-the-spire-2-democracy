using System;
using HarmonyLib;
using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;
using MegaCrit.Sts2.Core.Runs;

namespace DemocracyMod.DemocracyModCode.Patches;

/// <summary>
/// Broadcasts the host's config to all clients at run launch so every machine follows
/// the HOST's settings. Per-machine local config otherwise diverges the synchronized
/// reward flow (e.g. one machine shows a vote stage the other skips). RunManager.Launch
/// fires once per run on every machine, after NetService and the player list are ready
/// but before the first event (Neow), so clients have the host's snapshot before any
/// config-gated decision.
/// </summary>
public static class ConfigSyncPatch
{
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
    public static class OnRunLaunch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
                MultiplayerCoordinator.InitializeForRun();
                HostConfig.Reset();
                if (!MultiplayerCoordinator.IsHost) return;

                HostConfig.CaptureHostValues();
                MultiplayerCoordinator.SendConfig();
                MainFile.LogVote("Democracy: host config broadcast to clients");
            }
            catch (Exception e)
            {
                MainFile.LogDebug("Democracy: config sync error: " + e.Message);
            }
        }
    }
}
