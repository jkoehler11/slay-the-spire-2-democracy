using HarmonyLib;
using Godot;
using System.Linq;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace DemocracyMod.DemocracyModCode.Patches;

/// <summary>
/// Auto-pick: when a reward set begins for the LOCAL player, automatically select
/// every reward so the flow goes straight to the claim UI instead of making the
/// player click through the vanilla reward screen.
///
/// Hooks RewardsSetSynchronizer.BeginRewardsSet (the multiplayer reward-set entry
/// point) rather than the reward UI screen, so it fires for every reward source
/// (combat, events, ancients) and is on the same synchronizer that already drives
/// the pooling/completion path.
///
/// The vanilla NRewardsScreen is still shown by RewardsSet.Offer() after the set
/// begins, but auto-pick bypasses its buttons (SelectLocalReward directly), which
/// leaves it a stale, input-blocking zombie. TrackRewardsScreen captures it and
/// AutoPick dismisses it once every reward has been selected, so only the claim UI
/// is visible.
/// </summary>
public static class AutoPickPatch
{
    private static NRewardsScreen? _rewardsScreen;

    [HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen.ShowScreen))]
    public static class TrackRewardsScreen
    {
        [HarmonyPostfix]
        static void Postfix(NRewardsScreen __result)
        {
            _rewardsScreen = __result;
        }
    }

    [HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.BeginRewardsSet))]
    public static class AutoPickOnBeginRewardsSet
    {
        [HarmonyPostfix]
        static void Postfix(RewardsSetSynchronizer __instance, RewardsSet set)
        {
            if (!CombatRewardPatch.IsDemocracyActive) return;
            // Grant-capture gate: set on EVERY reward set so both machines capture the
            // granted models deterministically. (set.Room is host-local and unreliable on
            // the remote machine — reward POOLING is gated separately in AfterRewardTaken
            // via runState.CurrentRoom, which IS synced.)
            RewardPool.IsRewardPhaseActive = true;
            if (!DemocracyConfig.AutoPickRewards) return;
            if (__instance == null || set?.Rewards == null) return;

            var local = __instance.LocalPlayer;
            if (local == null || set.Player == null) return;
            if (set.Player.NetId != local.NetId) return;   // only the local player's set

            // Only auto-pick the LOCAL player's COMBAT set (local set.Room is reliable).
            // Ancients/events/shops/treasure stay vanilla — the user picks manually.
            if (set.Room is not CombatRoom) return;

            var sync = __instance;
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
            {
                AutoPick(sync, set);
                return;
            }

            // Defer slightly so the synchronizer has fully registered the set.
            var timer = tree.CreateTimer(0.25);
            timer.Timeout += () => AutoPick(sync, set);
        }
    }

    private static async void AutoPick(RewardsSetSynchronizer sync, RewardsSet set)
    {
        try
        {
            foreach (var reward in set.Rewards.ToList())
            {
                if (reward == null || reward.SuccessfullySelected) continue;
                try
                {
                    MainFile.Logger.Info(string.Format("Democracy: auto-picking {0}", reward.GetType().Name));
                    var ok = await sync.SelectLocalReward(reward);
                    if (!ok)
                        MainFile.Logger.Info(string.Format("Democracy: auto-pick returned false for {0}", reward.GetType().Name));
                }
                catch (Exception e)
                {
                    MainFile.Logger.Info(string.Format("Democracy: auto-pick failed for {0}: {1}",
                        reward.GetType().Name, e.Message));
                }
            }

            // All rewards selected — the set is complete. Dismiss the vanilla reward
            // screen (if it was shown) so it doesn't block the claim UI.
            DismissRewardsScreen();
        }
        catch (Exception e)
        {
            MainFile.Logger.Info(string.Format("Democracy: auto-pick error: {0}", e.Message));
        }
    }

    private static void DismissRewardsScreen()
    {
        var screen = _rewardsScreen;
        _rewardsScreen = null;
        if (screen == null || !GodotObject.IsInstanceValid(screen)) return;

        // Auto-pick bypassed the screen's reward buttons, so _rewardButtons is
        // still full and UpdateScreenState() would keep the Proceed button hidden
        // (and leave the screen as an input-blocking zombie). Clear the buttons
        // and let UpdateScreenState() drive the normal completion path: it enables
        // the Proceed button and emits Completed. Do NOT Hide() or QueueFree() the
        // screen — the game's async reward flow still references it (QueueFree was
        // the previous ObjectDisposedException crash), and hiding it was what
        // killed the Proceed button in the first place.
        try
        {
            if (screen._rewardButtons != null) screen._rewardButtons.Clear();
            screen.UpdateScreenState();
            MainFile.Logger.Info("Democracy: completed vanilla reward screen (proceed button enabled).");
        }
        catch (Exception e)
        {
            MainFile.Logger.Info(string.Format("Democracy: dismiss rewards screen error: {0}", e.Message));
        }
    }

    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
    public static class AutoSelectFirstCard
    {
        [HarmonyPostfix]
        static void Postfix(NCardRewardSelectionScreen __instance)
        {
            if (!CombatRewardPatch.IsDemocracyActive) return;
            if (!DemocracyConfig.AutoPickRewards) return;
            if (__instance == null) return;
            // Only auto-select cards during COMBAT rewards. Non-combat card
            // rewards (events, shops, ancients) must let the player choose.
            if (RunManager.Instance?.State?.CurrentRoom is not CombatRoom) return;

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            var screen = __instance;
            var timer = tree.CreateTimer(0.15);
            timer.Timeout += () => SelectFirstCard(screen);
        }
    }

    private static void SelectFirstCard(NCardRewardSelectionScreen screen)
    {
        try
        {
            if (screen == null || !GodotObject.IsInstanceValid(screen)) return;
            var options = screen._options;
            if (options == null || options.Count == 0) return;

            var card = options[0].Card;
            if (card == null) return;

            var holder = screen.GetCardHolder(card);
            if (holder == null) return;

            MainFile.Logger.Info(string.Format("Democracy: auto-selecting first card: {0}", card.Title));
            screen.SelectCard(holder);
        }
        catch (Exception e)
        {
            MainFile.Logger.Info(string.Format("Democracy: auto-pick card failed: {0}", e.Message));
        }
    }
}
