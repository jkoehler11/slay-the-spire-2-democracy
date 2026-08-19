using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Godot;
using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace DemocracyMod.DemocracyModCode.Patches;

/// <summary>
/// Shop (merchant) purchase pooling + flow trigger.
///
/// The merchant room is SHARED — every player is in it at once, each with their own
/// MerchantInventory. Each purchase grants a card/potion/relic via the synced
/// CardPileCmd.Add / PotionCmd.TryToProcure / RelicCmd.Obtain commands (captured by
/// CombatRewardPatch's grant hooks, now gated on RewardPool.IsShopPhaseActive). Unlike
/// combat (RewardsSet) or ancients (SetEventFinished), there is NO multiplayer gate on
/// leaving the shop — NMerchantRoom.HideScreen just opens the map locally. So this patch
/// supplies its own "everyone finished shopping" gate:
///
///   1. MerchantRoom.EnterInternal — arm shop capture and reset stale state.
///   2. Hook.AfterItemPurchased — (log only) purchases already flow into the pending
///      grant queue via the capture hooks.
///   3. NMerchantRoom.HideScreen (the Proceed/leave button) — mark the local player done,
///      broadcast it, close their inventory, and block the map until everyone is done.
///      Once all players are done, drain the pending grants into the pool and start the
///      shared vote flow (DemocracyFlow.Start) — the same potions -> relics -> cards flow
///      as combat, with gold skipped automatically (TotalGoldPooled is 0).
/// </summary>
public static class ShopPatch
{
    private static readonly HashSet<ulong> _donePlayers = new();
    private static WaitPanel? _waitPanel;

    /// <summary>True while a shop vote flow is active (drives the post-resolution leave).</summary>
    public static volatile bool IsShopFlowActive;

    [HarmonyPatch(typeof(Hook), "AfterRoomEntered")]
    public static class OnMerchantEnter
    {
        [HarmonyPostfix]
        static void Postfix(IRunState runState, AbstractRoom room)
        {
            try
            {
                if (!CombatRewardPatch.IsDemocracyActive) return;
                if (room is not MerchantRoom) return;
                if (!HostConfig.EnableShops) return;
                PostCombatPatch.ResetState();
                MultiplayerCoordinator.InitializeForRun();
                _donePlayers.Clear();
                CloseWaitPanel();
                RewardPool.IsShopPhaseActive = true;
                IsShopFlowActive = true;
                MainFile.LogVote("Democracy: merchant room entered - arming shop capture.");
            }
            catch (Exception e)
            {
                MainFile.LogDebug("Democracy: shop enter error: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Hook), "AfterItemPurchased")]
    public static class TrackPurchases
    {
        [HarmonyPostfix]
        static void Postfix(Player player, MerchantEntry itemPurchased, int goldSpent)
        {
            if (!CombatRewardPatch.IsDemocracyActive || !RewardPool.IsShopPhaseActive) return;
            MainFile.LogShop(string.Format(
                "Democracy: P{0} purchased {1} for {2}g.",
                player.NetId, itemPurchased?.GetType().Name ?? "?", goldSpent));
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom), "HideScreen")]
    public static class OnShopLeave
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            try
            {
                if (!CombatRewardPatch.IsDemocracyActive || !RewardPool.IsShopPhaseActive)
                    return true;

                ulong pid = MultiplayerCoordinator.LocalPlayerId;

                // Already confirmed done — swallow further proceed clicks while waiting.
                if (_donePlayers.Contains(pid))
                    return false;

                // First proceed click: confirm before finalizing, so a stray click can't
                // end the player's shopping (and trigger the group vote) by accident.
                ConfirmSkipPanel.ShowShop(FinalizeLeave);
                return false; // don't leave until confirmed
            }
            catch (Exception e)
            {
                MainFile.LogDebug("Democracy: shop HideScreen error: " + e.Message);
                return true;
            }
        }

        /// <summary>Runs after the player confirms they are done shopping. Marks the local
        /// player done, broadcasts it, and either starts the vote (everyone done) or shows
        /// the waiting overlay (others still shopping). Mirrors the previous inline logic.</summary>
        private static void FinalizeLeave()
        {
            try
            {
                ulong pid = MultiplayerCoordinator.LocalPlayerId;
                if (_donePlayers.Add(pid))
                {
                    MultiplayerCoordinator.SendShopDone();
                    // Close the inventory so the player can't keep buying after "done".
                    try { MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom.Instance?.Inventory?.Close(); }
                    catch (Exception e) { MainFile.LogDebug("Democracy: close inventory error: " + e.Message); }
                }

                int total = CombatRewardPatch.GetSeenPlayerCount();
                int done = _donePlayers.Count;
                MainFile.LogVote(string.Format("Democracy: shop leave P{0} ({1}/{2})", pid, done, total));

                if (total < 2)
                {
                    RewardPool.IsShopPhaseActive = false;
                    IsShopFlowActive = false;
                    LeaveShop(); // single player — vanilla leave
                    return;
                }

                if (done >= total)
                {
                    RewardPool.IsShopPhaseActive = false;
                    CloseWaitPanel();
                    PoolAllPendingGrants();
                    if (RewardPool.HasPending)
                    {
                        MainFile.LogVote(string.Format(
                            "Democracy: SHOP CLAIM TIME! pool: {0}g/{1}c/{2}p/{3}r",
                            RewardPool.TotalGoldPooled, RewardPool.TotalCardsPooled,
                            RewardPool.TotalPotionsPooled, RewardPool.TotalRelicsPooled));
                        DemocracyFlow.Start();
                    }
                    else
                    {
                        MainFile.LogVote("Democracy: shop had no pooled rewards - leaving normally.");
                        IsShopFlowActive = false;
                        LeaveShop(); // nothing pooled — vanilla leave
                    }
                    return;
                }

                ShowWaitPanel(); // waiting for others
            }
            catch (Exception e)
            {
                MainFile.LogDebug("Democracy: shop finalize error: " + e.Message);
            }
        }
    }

    /// <summary>A peer finished shopping (broadcast). Track and, once everyone is done,
    /// pool the group's purchases and start the vote.</summary>
    internal static void OnPlayerShopDone(ulong senderId)
    {
        if (!CombatRewardPatch.IsDemocracyActive || !RewardPool.IsShopPhaseActive) return;
        if (!_donePlayers.Add(senderId)) return;

        int total = CombatRewardPatch.GetSeenPlayerCount();
        int done = _donePlayers.Count;
        MainFile.LogVote(string.Format("Democracy: shop P{0} done ({1}/{2})", senderId, done, total));

        if (done >= total)
        {
            RewardPool.IsShopPhaseActive = false;
            CloseWaitPanel();
            PoolAllPendingGrants();
            if (RewardPool.HasPending)
                DemocracyFlow.Start();
            else
                IsShopFlowActive = false;
        }
    }

    /// <summary>Drain every player's captured purchase grants into the pool, in sorted
    /// NetId order so pool-entry ids are identical on every machine. Mirrors
    /// AncientRewardPatch.PoolPendingGrants.</summary>
    private static void PoolAllPendingGrants()
    {
        foreach (var pid in CombatRewardPatch.GetSeenPlayerIds())
            PoolPendingGrants(pid);
    }

    private static void PoolPendingGrants(ulong pid)
    {
        var relics = RewardPool.TakeAllPendingGrants(pid, RewardPool.PoolEntry.RewardType.Relic)
            .OfType<RelicModel>().ToList();
        foreach (var r in relics)
        {
            var name = CombatRewardPatch.LocName(r.Title);
            RewardPool.AddRelicReward(pid, name, false, r);
            MainFile.LogReward(string.Format("Democracy: shop relic [{0}] from P{1} pooled", name, pid));
        }

        var cards = RewardPool.TakeAllPendingGrants(pid, RewardPool.PoolEntry.RewardType.CardReward)
            .OfType<CardModel>().ToList();
        foreach (var c in cards)
        {
            RewardPool.AddCardReward(pid, 1, c.Title, c);
            MainFile.LogReward(string.Format("Democracy: shop card [{0}] from P{1} pooled", c.Title, pid));
        }

        var potions = RewardPool.TakeAllPendingGrants(pid, RewardPool.PoolEntry.RewardType.Potion)
            .OfType<PotionModel>().ToList();
        foreach (var p in potions)
        {
            var name = CombatRewardPatch.LocName(p.Title);
            RewardPool.AddPotionReward(pid, name, p);
            MainFile.LogReward(string.Format("Democracy: shop potion [{0}] from P{1} pooled", name, pid));
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
            MainFile.Loc("DemocracyMod.WaitPanel.ShopSubtitle", "Waiting for all players to finish shopping"));
        tree.Root.AddChild(_waitPanel);
    }

    private static void CloseWaitPanel()
    {
        if (_waitPanel != null && GodotObject.IsInstanceValid(_waitPanel))
            _waitPanel.QueueFree();
        _waitPanel = null;
    }

    /// <summary>Replicates the vanilla leave (HideScreen -> open map) after the vote resolves.</summary>
    public static void LeaveShop()
    {
        try
        {
            IsShopFlowActive = false;
            RewardPool.IsShopPhaseActive = false;
            MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.Open(false);
        }
        catch (Exception e)
        {
            MainFile.LogDebug("Democracy: leave shop error: " + e.Message);
        }
    }
}
