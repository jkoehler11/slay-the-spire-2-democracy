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
///
/// NOTE: we intentionally do NOT call HostConfig.Reset() here. The config message sent
/// by the host at its own launch postfix can arrive at a client while the NetMessageBus
/// is buffering messages during the run-start transition; it is delivered during the
/// client's RunManager.Launch method body, and a Reset() in this postfix would then run
/// AFTER that delivery and wipe the freshly-applied snapshot, falling back to the client's
/// divergent LOCAL settings. Keeping the prior snapshot (host re-broadcasts every run and
/// the message reliably arrives before the first event) is always safer than local.
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
