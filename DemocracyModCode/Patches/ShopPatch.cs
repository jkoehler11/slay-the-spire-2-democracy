using HarmonyLib;
using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace DemocracyMod.DemocracyModCode.Patches;

public static class ShopPatch
{
    private static int _shopPooledGold;
    private static bool _inShop;

    public static void EnsureShopPooled()
    {
        if (_inShop || !CombatRewardPatch.IsDemocracyActive || !DemocracyConfig.ShopDemocracy) return;
        _inShop = true; _shopPooledGold = 0;
        foreach (var player in MultiplayerCoordinator.GetPlayers())
        {
            var gold = Traverse.Create(player).Property<int>("Gold").Value;
            _shopPooledGold += gold;
            Traverse.Create(player).Property<int>("Gold").Value = 0;
        }
    }

    [HarmonyPatch(typeof(NMerchantCard), "OnTryPurchase")]
    public static class VoteOnPurchase
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            if (!CombatRewardPatch.IsDemocracyActive || !DemocracyConfig.ShopDemocracy) return true;
            EnsureShopPooled();
            return false;
        }
    }

    [HarmonyPatch(typeof(NMerchantCardRemoval), "OnTryPurchase")]
    public static class VoteOnCardRemoval
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            if (!CombatRewardPatch.IsDemocracyActive || !DemocracyConfig.ShopDemocracy) return true;
            EnsureShopPooled();
            return false;
        }
    }

    [HarmonyPatch(typeof(Hook), "AfterItemPurchased",
                  [typeof(MegaCrit.Sts2.Core.Runs.IRunState), typeof(Player), typeof(MerchantEntry), typeof(int)])]
    public static class TrackShopGoldAfterPurchase
    {
        [HarmonyPostfix]
        static void Postfix(int goldSpent)
        {
            if (_inShop && CombatRewardPatch.IsDemocracyActive && DemocracyConfig.ShopDemocracy)
                _shopPooledGold -= goldSpent;
        }
    }

    private static void OnShopLeave()
    {
        if (!_inShop) return;
        _inShop = false;
        if (DemocracyConfig.ShopRedistribute && _shopPooledGold > 0)
        {
            var players = MultiplayerCoordinator.GetPlayers();
            if (players.Count == 0) return;
            var perPlayer = _shopPooledGold / players.Count;
            foreach (var player in players)
                Traverse.Create(player).Property<int>("Gold").Value += perPlayer;
        }
        _shopPooledGold = 0;
    }

    [HarmonyPatch(typeof(RewardsSetSynchronizer), "BeforeLeavingRoom")]
    public static class DetectShopLeave
    {
        [HarmonyPostfix]
        static void Postfix() => OnShopLeave();
    }
}
