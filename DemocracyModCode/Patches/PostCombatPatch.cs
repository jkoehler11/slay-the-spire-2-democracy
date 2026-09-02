using HarmonyLib;
using Godot;
using System.Collections.Generic;
using System.Linq;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace DemocracyMod.DemocracyModCode.Patches;

/// <summary>
/// Post-combat claim orchestration.
///
/// Problem this solves: CompleteRewardsSetIfNecessary fires at combat START
/// when reward sets are still EMPTY (an empty set is trivially "complete"),
/// which previously opened the claim UI before any rewards were pooled.
///
/// Correct signal: all players are done picking when the synchronizer's
/// per-player `_rewardStates` each have an empty `rewardsStack`, AND the pool
/// is non-empty (rewards were actually taken). We capture the synchronizer
/// from CompleteRewardsSetIfNecessary, then re-check after every
/// AfterRewardTaken (which is where rewards are actually pooled).
/// </summary>
public static class PostCombatPatch
{
    private static RewardsSetSynchronizer? _sync;
    private static bool _claimShown;
    private static bool _confirmArmed;
    private static WaitPanel? _waitPanel;

    [HarmonyPatch(typeof(RewardsSetSynchronizer), "CompleteRewardsSetIfNecessary")]
    public static class OnCompleteRewardsSet
    {
        [HarmonyPostfix]
        static void Postfix(RewardsSetSynchronizer __instance)
        {
            _sync = __instance;
            CheckCompletion();
        }
    }


    /// <summary>
    /// CompleteRewardsSet is the single completion choke point: it is called both by
    /// CompleteRewardsSetIfNecessary (all rewards claimed) and by SkipRewardsSet (a
    /// player declined their remaining rewards). The latter path bypasses
    /// CompleteRewardsSetIfNecessary, so without this patch a player who claims some
    /// rewards and then skips the rest would complete their set without ever re-triggering
    /// the all-players-done check — stranding the group on the loot screen. Fires on every
    /// machine (skip is broadcast and applied via HandleRewardSetSkippedMessage on peers),
    /// keeping the completion signal deterministic.
    /// </summary>
    [HarmonyPatch(typeof(RewardsSetSynchronizer), "CompleteRewardsSet")]
    public static class OnRewardsSetCompleted
    {
        [HarmonyPostfix]
        static void Postfix(RewardsSetSynchronizer __instance)
        {
            _sync = __instance;
            CheckCompletion();
        }
    }

    [HarmonyPatch(typeof(CombatRoom), "OnCombatEnded")]
    public static class OnCombatEndTrigger
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            if (!CombatRewardPatch.IsDemocracyActive) return;
            ResetState();
        }
    }

    /// <summary>Called by CombatRewardPatch.AfterRewardTaken after each pooled reward.</summary>
    public static void NotifyRewardPooled()
    {
        ScheduleCompletionCheck();
    }

    private static void ScheduleCompletionCheck()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) { CheckCompletion(); return; }
        // Defer slightly so the synchronizer can pop the reward stack after the pick.
        var timer = tree.CreateTimer(0.3);
        timer.Timeout += CheckCompletion;
    }

    private static void CheckCompletion()
    {
        if (!CombatRewardPatch.IsDemocracyActive || _claimShown) return;
        if (_sync == null) return;
        if (!RewardPool.HasPending) return;           // nothing pooled yet (early/empty firing)

        bool stacksEmpty = AllRewardStacksEmpty(_sync);
        bool localDone = LocalPlayerDone(_sync);
        MainFile.LogDebug(string.Format(
            "Democracy: completion check — stacksEmpty={0} localDone={1} {2}",
            stacksEmpty, localDone, StackStateSummary(_sync)));

        if (!stacksEmpty)
        {
            // Someone is still picking. If the local player is already done,
            // show the "waiting for other players" overlay.
            _confirmArmed = false;
            if (localDone)
            {
                MainFile.LogDebug("Democracy: still picking — showing wait overlay.");
                ShowWaitPanel();
            }
            return;
        }

        // All reward stacks are empty — but this can be transient: the synchronizer pops a
        // player's set before their next set is pushed, which previously opened the claim UI
        // (and the "waiting" overlay) while someone was still picking (#2/#10). Require the
        // empty state to hold across a short delay before claiming.
        if (!_confirmArmed)
        {
            _confirmArmed = true;
            MainFile.LogVote("Democracy: all stacks empty — arming confirm (re-check in 0.7s).");
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) { CompleteClaim(); return; }
            var timer = tree.CreateTimer(0.7);
            timer.Timeout += CheckCompletion;
            return;
        }

        MainFile.LogVote("Democracy: all stacks still empty after debounce — claiming.");
        CompleteClaim();
    }

    /// <summary>Compact per-player reward-stack depth for debugging the completion signal.
    /// Each element is one player's rewardsStack.Count (null if the stack list is null).</summary>
    private static string StackStateSummary(RewardsSetSynchronizer sync)
    {
        try
        {
            var states = sync._rewardStates;
            if (states == null) return "[no states]";
            var depths = new List<string>();
            foreach (var st in states)
                depths.Add(st.rewardsStack == null ? "null" : st.rewardsStack.Count.ToString());
            return "[" + string.Join(", ", depths) + "]";
        }
        catch { return "[error]"; }
    }

    private static void CompleteClaim()
    {
        if (_claimShown) return;
        _claimShown = true;
        CloseWaitPanel();

        // Close every open screen (loot screen, deck view, ...) so the claim flow doesn't
        // render over a live menu (#8).
        NativeUiPatch.CloseOpenScreens();

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
        {
            MainFile.LogDebug("Democracy: no SceneTree - cannot show claim screens.");
            return;
        }

        var timer = tree.CreateTimer(0.15);
        timer.Timeout += ShowClaimPanel;
    }

    private static bool AllRewardStacksEmpty(RewardsSetSynchronizer sync)
    {
        try
        {
            var states = sync._rewardStates;
            if (states == null || states.Count < 2) return false;
            foreach (var s in states)
            {
                if (s.rewardsStack != null && s.rewardsStack.Count > 0)
                    return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static bool LocalPlayerDone(RewardsSetSynchronizer sync)
    {
        try
        {
            var local = sync.LocalPlayer;
            if (local == null) return false;
            var state = sync.GetRewardStateForPlayer(local);
            return state == null || state.rewardsStack == null || state.rewardsStack.Count == 0;
        }
        catch { return false; }
    }

    public static void ResetState()
    {
        _sync = null;
        _claimShown = false;
        _confirmArmed = false;
        CloseWaitPanel();
        DemocracyFlow.Reset();
        VoteManager.Reset();
        CombatRewardPatch.ResetTracking();
        RewardPool.Clear();
    }

    private static void ShowWaitPanel()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;
        if (_waitPanel != null && GodotObject.IsInstanceValid(_waitPanel)) return;

        _waitPanel = new WaitPanel();
        tree.Root.AddChild(_waitPanel);
    }

    private static void ShowClaimPanel()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;

        int seen = CombatRewardPatch.GetSeenPlayerCount();
        MainFile.LogVote(string.Format(
            "Democracy: CLAIM TIME! {0} players, pool: {1}g/{2}c/{3}p/{4}r",
            seen, RewardPool.TotalGoldPooled, RewardPool.TotalCardsPooled,
            RewardPool.TotalPotionsPooled, RewardPool.TotalRelicsPooled));

        DemocracyFlow.Start();
    }

    private static void CloseWaitPanel()
    {
        if (_waitPanel != null && GodotObject.IsInstanceValid(_waitPanel))
            _waitPanel.QueueFree();
        _waitPanel = null;
    }

    /// <summary>
    /// Called by VoteManager once the distribution has been applied (host or client).
    /// Closes any open democracy screens and shows the results summary after a beat.
    /// </summary>
    public static void OnDistributionComplete(List<string> results)
    {
        RewardPool.IsDemocracyFlowActive = false;
        DemocracyFlow.CloseAll();

        // Combat proceeds past its suppressed vanilla reward screen; ancient/event
        // rewards leave the player on the event's own "done" page to proceed manually.
        var currentRoom = RunManager.Instance?.State?.CurrentRoom;
        Action? onContinue = currentRoom is CombatRoom
            ? AdvanceFromRewards
            : currentRoom is MerchantRoom ? ShopPatch.LeaveShop
            : null;

        if (!HostConfig.ShowResultsPanel)
        {
            onContinue?.Invoke();
            return;
        }

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;

        var timer = tree.CreateTimer(0.4);
        timer.Timeout += () =>
        {
            try
            {
                if (tree.Root == null) return;
                var panel = new ResultsPanel();
                panel.SetLines(results, onContinue);
                tree.Root.AddChild(panel);
            }
            catch (Exception e)
            {
                MainFile.LogDebug("Democracy: results panel error: " + e.Message);
            }
        };
    }

    /// <summary>
    /// Replicates what the vanilla reward screen's proceed button does now that we
    /// suppress it, matching NRewardsScreen.OnProceedButtonPressed:
    ///   - Boss combat: the vanilla disables the proceed button and marks the local
    ///     player ready via ActChangeSynchronizer.SetLocalPlayerReady, so the group
    ///     transitions to the next act together once everyone is ready.
    ///   - Every other combat: RunManager.ProceedFromTerminalRewardsScreen() (enable
    ///     travel + open the map).
    /// The previous version consulted ActChangeSynchronizer.IsWaitingForOtherPlayers(),
    /// but that is a BOSS-ONLY act-change path — its _readyPlayers list is all-false
    /// during normal play, so the first player to press Continue marked themselves
    /// act-ready and waited forever while the second (who saw the first as ready)
    /// advanced to the map. That was the "host stuck with nothing to click" bug.
    /// </summary>
    private static void AdvanceFromRewards()
    {
        try
        {
            var rm = RunManager.Instance;
            if (rm == null) return;

            var state = rm.State;
            var room = state?.CurrentRoom;

            // Boss/victory rooms use the act-change ready gate (matching the vanilla
            // OnProceedButtonPressed: RoomType == Boss || IsVictoryRoom), EXCEPT the
            // run's final boss (act 4 / heart), which proceeds directly to the win flow.
            bool atActBoundary = room != null &&
                (room.RoomType == RoomType.Boss || room.IsVictoryRoom);

            if (atActBoundary && state != null && room != null && IsFinalBossRoom(state, room))
                atActBoundary = false;

            if (atActBoundary)
            {
                rm.ActChangeSynchronizer?.SetLocalPlayerReady();
            }
            else
            {
                _ = rm.ProceedFromTerminalRewardsScreen();
            }
        }
        catch (Exception e)
        {
            MainFile.LogDebug("Democracy: advance from rewards error: " + e.Message);
        }
    }

    /// <summary>True when the current room is the run's final boss — there is a second
    /// boss map point AND the player is standing on the boss coordinate. Mirrors the
    /// vanilla OnProceedButtonPressed check (SecondBossMapPoint != null &&
    /// CurrentMapCoord == BossMapPoint.coord).</summary>
    private static bool IsFinalBossRoom(RunState state, AbstractRoom room)
    {
        try
        {
            var map = state.Map;
            if (map?.SecondBossMapPoint == null) return false;
            var bossCoord = map.BossMapPoint?.coord;
            if (bossCoord == null) return false;
            var cur = state.CurrentMapCoord;
            if (!cur.HasValue) return false;
            return cur.Value.col == bossCoord.Value.col && cur.Value.row == bossCoord.Value.row;
        }
        catch { return false; }
    }
}
