using HarmonyLib;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Entities.Players;

namespace DemocracyMod.DemocracyModCode.Patches;

public static class CombatRewardPatch
{
    public static bool IsDemocracyActive { get; set; }

    [HarmonyPatch(typeof(GoldReward), "OnSelect")]
    public static class GoldRewardRedirect
    {
        [HarmonyPrefix]
        static bool Prefix(GoldReward __instance, ref System.Threading.Tasks.Task<bool> __result)
        {
            if (!IsDemocracyActive) return true;
            var p = __instance.Player;
            RewardPool.AddGoldReward(p.NetId, __instance.Amount);
            __result = System.Threading.Tasks.Task.FromResult(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(CardReward), "OnSelect")]
    public static class CardRewardRedirect
    {
        [HarmonyPrefix]
        static bool Prefix(CardReward __instance, ref System.Threading.Tasks.Task<bool> __result)
        {
            if (!IsDemocracyActive) return true;
            var p = __instance.Player;
            RewardPool.AddCardReward(p.NetId, __instance.OptionCount);
            __result = System.Threading.Tasks.Task.FromResult(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(PotionReward), "OnSelect")]
    public static class PotionRewardRedirect
    {
        [HarmonyPrefix]
        static bool Prefix(PotionReward __instance, ref System.Threading.Tasks.Task<bool> __result)
        {
            if (!IsDemocracyActive) return true;
            var p = __instance.Player;
            var pot = __instance.Potion;
            var name = pot?.Title?.ToString() ?? pot?.Id.ToString() ?? "Unknown Potion";
            RewardPool.AddPotionReward(p.NetId, name);
            __result = System.Threading.Tasks.Task.FromResult(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(RelicReward), "OnSelect")]
    public static class RelicRewardRedirect
    {
        [HarmonyPrefix]
        static bool Prefix(RelicReward __instance, ref System.Threading.Tasks.Task<bool> __result)
        {
            if (!IsDemocracyActive) return true;
            var p = __instance.Player;
            var r = __instance.Relic;
            var name = r?.Title?.ToString() ?? r?.Id.ToString() ?? "Unknown Relic";
            RewardPool.AddRelicReward(p.NetId, name, false);
            __result = System.Threading.Tasks.Task.FromResult(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(SpecialCardReward), "OnSelect")]
    public static class SpecialCardRewardRedirect
    {
        [HarmonyPrefix]
        static bool Prefix(SpecialCardReward __instance, ref System.Threading.Tasks.Task<bool> __result)
        {
            if (!IsDemocracyActive) return true;
            RewardPool.AddCardReward(__instance.Player.NetId, 1);
            __result = System.Threading.Tasks.Task.FromResult(false);
            return false;
        }
    }
}
