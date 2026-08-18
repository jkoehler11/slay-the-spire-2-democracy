using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using DemocracyMod.DemocracyModCode;

namespace DemocracyMod.DemocracyModCode.Patches;

/// <summary>
/// Hooks that run the mod's 4-step claim flow on the game's NATIVE event screen
/// (NEventRoom + NEventLayout) AFTER each player has selected their own loot on the
/// vanilla rewards screen.
///
/// 1. NEventRoom.get_Instance is patched to return the mod's live claim NEventRoom while
///    the claim flow is active. The native event-option buttons route clicks through
///    NEventRoom.Instance.OptionButtonClicked; every claim option is built with
///    IsProceed=true, so that handler runs option.Chosen() -> the mod's toggle/submit
///    callback (no EventSynchronizer involved).
///
/// 2. NRewardsScreen.TryEnableProceedButton is patched to keep the vanilla combat
///    Proceed button disabled while there is pooled loot. The player picks their own
///    loot first, then the claim flow runs on top; advancing is driven by
///    PostCombatPatch.AdvanceFromRewards once the vote completes, so the vanilla
///    Proceed must not let a player skip ahead. When there is no pooled loot the
///    vanilla Proceed stays intact so the run can advance normally.
/// </summary>
public static class NativeUiPatch
{
    [HarmonyPatch(typeof(NEventRoom), "get_Instance")]
    public static class ClaimRoomInstance
    {
        [HarmonyPrefix]
        static bool Prefix(ref NEventRoom __result)
        {
            if (DemocracyFlow.IsClaimActive && DemocracyFlow.Room != null)
            {
                __result = DemocracyFlow.Room;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen.TryEnableProceedButton))]
    public static class SuppressCombatProceed
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            // While there is pooled combat loot the mod will run the claim flow and
            // advance the group itself (PostCombatPatch.AdvanceFromRewards). Keep the
            // vanilla Proceed button disabled so a player can't click through to the
            // map (or signal ready) before the vote completes. With no pooled loot
            // (HasPending false) the vanilla Proceed stays intact.
            if (CombatRewardPatch.IsDemocracyActive && RewardPool.HasPending)
                return false;
            return true;
        }
    }


    /// <summary>
    /// The claim event is a synthetic EventModel with no portrait asset, so the game's
    /// NEventLayout.InitializeVisuals → CreateInitialPortrait tries to load
    /// res://images/events/democracy_claim_event.png and fails. Return null (blank
    /// portrait) for the claim event so no resource load is attempted.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.EventModel), "CreateInitialPortrait")]
    public static class SkipClaimPortrait
    {
        [HarmonyPrefix]
        static bool Prefix(MegaCrit.Sts2.Core.Models.EventModel __instance, ref Texture2D __result)
        {
            if (__instance is DemocracyClaimEvent)
            {
                __result = null!;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.EventModel), "CreateInitialPhobiaModePortrait")]
    public static class SkipClaimPhobiaPortrait
    {
        [HarmonyPrefix]
        static bool Prefix(MegaCrit.Sts2.Core.Models.EventModel __instance, ref Texture2D __result)
        {
            if (__instance is DemocracyClaimEvent)
            {
                __result = null!;
                return false;
            }
            return true;
        }
    }

    /// <summary>Crash-diagnosis probe: confirms SetupLayout's async continuation completes.</summary>
    [HarmonyPatch(typeof(NEventLayout), "OnSetupComplete")]
    public static class SetupCompleteProbe
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            MainFile.Logger.Info("[CRASHDBG] NEventLayout.OnSetupComplete fired");
        }
    }

    [HarmonyPatch(typeof(NEventRoom), "SetOptions")]
    public static class SetOptionsProbe
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            MainFile.Logger.Info("[CRASHDBG] NEventRoom.SetOptions done");
        }
    }

    /// <summary>
    /// Player-icon vote display. The native event-option button shows each player's
    /// character icon for players who "voted" for it, driven by the private
    /// ShouldDisplayPlayerVote(Player) delegate (bound to the button's
    /// NMultiplayerVoteContainer in _Ready). That delegate reads the game's
    /// EventSynchronizer vote state, which is empty for our synthetic claim options.
    /// While the claim flow is active we answer instead from DemocracyFlow's live
    /// per-player selection map, then RefreshVotes() renders the icons.
    /// </summary>
    [HarmonyPatch(typeof(NEventOptionButton), "ShouldDisplayPlayerVote")]
    public static class DemocracyPlayerVoteIcons
    {
        [HarmonyPrefix]
        static bool Prefix(NEventOptionButton __instance, Player player, ref bool __result)
        {
            if (!DemocracyFlow.IsClaimActive) return true;   // vanilla (shared events)
            var optId = __instance.Option?.TextKey;
            __result = optId != null && DemocracyFlow.HasSelected(player.NetId, optId);
            return false;
        }
    }

}
