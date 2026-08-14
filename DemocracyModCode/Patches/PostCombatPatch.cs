using HarmonyLib;
using Godot;
using System.Collections.Generic;
using System.Linq;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Multiplayer.Game;

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
    private static VotePanel? _votePanel;
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

        if (!AllRewardStacksEmpty(_sync))
        {
            // Someone is still picking. If the local player is already done,
            // show the "waiting for other players" overlay.
            if (LocalPlayerDone(_sync))
                ShowWaitPanel();
            return;
        }

        _claimShown = true;
        CloseWaitPanel();

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) { RewardPool.DistributeEvenly(); return; }

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

    private static void ResetState()
    {
        _sync = null;
        _claimShown = false;
        CloseVotePanel();
        CloseWaitPanel();
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
        if (_votePanel != null && GodotObject.IsInstanceValid(_votePanel)) return;

        int seen = CombatRewardPatch.GetSeenPlayerCount();
        MainFile.LogVote(string.Format(
            "Democracy: CLAIM TIME! {0} players, pool: {1}g/{2}c/{3}p/{4}r",
            seen, RewardPool.TotalGoldPooled, RewardPool.TotalCardsPooled,
            RewardPool.TotalPotionsPooled, RewardPool.TotalRelicsPooled));

        _votePanel = new VotePanel();
        tree.Root.AddChild(_votePanel);
    }

    private static void CloseWaitPanel()
    {
        if (_waitPanel != null && GodotObject.IsInstanceValid(_waitPanel))
            _waitPanel.QueueFree();
        _waitPanel = null;
    }

    private static void CloseVotePanel()
    {
        if (_votePanel != null && GodotObject.IsInstanceValid(_votePanel))
            _votePanel.QueueFree();
        _votePanel = null;
    }
}
