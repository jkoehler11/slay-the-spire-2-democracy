using HarmonyLib;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Entities.Players;

namespace DemocracyMod.DemocracyModCode.Patches;

public static class ShopPatch
{
    [HarmonyPatch(typeof(Hook), "AfterItemPurchased")]
    public static class TrackPurchases
    {
        [HarmonyPostfix]
        static void Postfix(Player player)
        {
            if (!CombatRewardPatch.IsDemocracyActive) return;
            MainFile.LogShop(string.Format("Democracy: Player P{0} purchased an item.", player.NetId));
        }
    }
}
