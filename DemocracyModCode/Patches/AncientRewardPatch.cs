using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;

namespace DemocracyMod.DemocracyModCode.Patches;

/// <summary>
/// Ancient reward pooling + flow trigger. Ancients (Neow, Darv, Orobas, Pael, Tanx,
/// Tezcatara, Vakuu, Nonupeipe) are NON-SHARED events: each player individually talks
/// to the ancient and picks their own reward (usually a relic), granted via the synced
/// RelicCmd.Obtain and finished via EventModel.SetEventFinished. They do NOT use a
/// RewardsSet, so the combat pipeline (Hook.AfterRewardTaken + RewardsSetSynchronizer)
/// never fires for them.
///
/// This patch mirrors the combat flow with two hooks:
///   1. EventSynchronizer.BeginEvent  — arm reward capture for non-shared ancients and
///      reset stale combat/ancient state before any reward is granted.
///   2. EventModel.SetEventFinished   — fires deterministically on every machine for every
///      player's ancient; pool that player's captured grants, mark them done, show a
///      "waiting for players" overlay while others finish, and start the shared vote flow
///      once everyone is done.
/// </summary>
public static class AncientRewardPatch
{
    private static readonly HashSet<ulong> _ancientDonePlayers = new();
    private static WaitPanel? _waitPanel;

    [HarmonyPatch(typeof(EventSynchronizer), "BeginEvent")]
    public static class OnAncientBeginEvent
    {
        [HarmonyPostfix]
        static void Postfix(object[] __args)
        {
            try
            {
                var ev = __args is { Length: > 0 } ? __args[0] as EventModel : null;
                var isShared = __args is { Length: > 1 } && __args[1] is bool b && b;

                if (ev is AncientEventModel && !isShared && HostConfig.EnableAncients)
                {
                    MainFile.LogVote(string.Format(
                        "Democracy: ancient event entered — arming reward capture (host={0} received={1} enableAncients={2}).",
                        MultiplayerCoordinator.IsHost, HostConfig.Received, HostConfig.EnableAncients));
                    PostCombatPatch.ResetState();
                    MultiplayerCoordinator.InitializeForRun();
                    CloseWaitPanel();
                    _ancientDonePlayers.Clear();
                    RewardPool.IsAncientRewardPhaseActive = true;
                }
                else
                {
                    RewardPool.IsAncientRewardPhaseActive = false;
                }
            }
            catch (Exception e)
            {
                MainFile.LogDebug("Democracy: ancient BeginEvent error: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(EventModel), "SetEventFinished")]
    public static class OnAncientEventFinished
    {
        [HarmonyPostfix]
        static void Postfix(EventModel __instance)
        {
            try
            {
                if (!CombatRewardPatch.IsDemocracyActive) return;
                if (__instance is not AncientEventModel) return;
                if (!RewardPool.IsAncientRewardPhaseActive) return;

                var player = __instance.Owner;
                if (player == null) return;
                ulong pid = player.NetId;

                PoolPendingGrants(pid);

                // Idempotent: a single ancient can call SetEventFinished more than once.
                if (!_ancientDonePlayers.Add(pid)) return;

                int total = CombatRewardPatch.GetSeenPlayerCount();
                int done = _ancientDonePlayers.Count;
                MainFile.LogVote(string.Format("Democracy: ancient P{0} finished ({1}/{2})", pid, done, total));

                // Single-player / no run state (e.g. Neow's run-start blessing) — the
                // democracy vote is a multiplayer mechanic, so leave those rewards alone.
                if (total < 2)
                {
                    RewardPool.IsAncientRewardPhaseActive = false;
                    return;
                }

                if (done >= total)
                {
                    RewardPool.IsAncientRewardPhaseActive = false;
                    CloseWaitPanel();
                    if (RewardPool.HasPending)
                    {
                        MainFile.LogVote(string.Format(
                            "Democracy: ANCIENT CLAIM TIME! pool: {0}g/{1}c/{2}p/{3}r",
                            RewardPool.TotalGoldPooled, RewardPool.TotalCardsPooled,
                            RewardPool.TotalPotionsPooled, RewardPool.TotalRelicsPooled));
                        DemocracyFlow.Start();
                    }
                    else
                    {
                        MainFile.LogVote("Democracy: ancient had no pooled rewards — skipping vote.");
                    }
                }
                else if (pid == MultiplayerCoordinator.LocalPlayerId)
                {
                    ShowWaitPanel();
                }
            }
            catch (Exception e)
            {
                MainFile.LogDebug("Democracy: ancient SetEventFinished error: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Drain the grant-capture queue for the finishing player and turn every captured
    /// relic/card/potion into a pool entry. Runs on every machine identically (the grants
    /// are synced commands, so the queue contents match), producing the same deterministic
    /// pool entry ids as the combat path.
    /// </summary>
    private static void PoolPendingGrants(ulong pid)
    {
        var relics = RewardPool.TakeAllPendingGrants(pid, RewardPool.PoolEntry.RewardType.Relic)
            .OfType<RelicModel>().ToList();
        foreach (var r in relics)
        {
            var name = CombatRewardPatch.LocName(r.Title);
            RewardPool.AddRelicReward(pid, name, false, r);
            MainFile.LogReward(string.Format("Democracy: ancient relic [{0}] from P{1} pooled", name, pid));
        }

        var cards = RewardPool.TakeAllPendingGrants(pid, RewardPool.PoolEntry.RewardType.CardReward)
            .OfType<CardModel>().ToList();
        foreach (var c in cards)
        {
            RewardPool.AddCardReward(pid, 1, c.Title, c);
            MainFile.LogReward(string.Format("Democracy: ancient card [{0}] from P{1} pooled", c.Title, pid));
        }

        var potions = RewardPool.TakeAllPendingGrants(pid, RewardPool.PoolEntry.RewardType.Potion)
            .OfType<PotionModel>().ToList();
        foreach (var p in potions)
        {
            var name = CombatRewardPatch.LocName(p.Title);
            RewardPool.AddPotionReward(pid, name, p);
            MainFile.LogReward(string.Format("Democracy: ancient potion [{0}] from P{1} pooled", name, pid));
        }
    }

    private static void ShowWaitPanel()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;
        if (_waitPanel != null && GodotObject.IsInstanceValid(_waitPanel)) return;

        _waitPanel = new WaitPanel();
        _waitPanel.Configure(
            MainFile.Loc("DemocracyMod.WaitPanel.Title", "WAITING FOR PLAYERS"),
            MainFile.Loc("DemocracyMod.WaitPanel.Subtitle", "Waiting for all players to finish the ancient"));
        tree.Root.AddChild(_waitPanel);
    }

    private static void CloseWaitPanel()
    {
        if (_waitPanel != null && GodotObject.IsInstanceValid(_waitPanel))
            _waitPanel.QueueFree();
        _waitPanel = null;
    }
}
