using HarmonyLib;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace DemocracyMod.DemocracyModCode.Patches;

/// <summary>
/// Gates reward-grant capture to the reward phase. The player selects their own loot
/// on the vanilla rewards screen; this mod does NOT auto-pick. Setting
/// IsRewardPhaseActive on every BeginRewardsSet ensures the CardPileCmd/PotionCmd/
/// RelicCmd capture postfixes (CombatRewardPatch) record granted models into
/// RewardPool's pending-grant queue so pooled entries carry the real granted object.
/// </summary>
public static class RewardPhasePatch
{
    [HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.BeginRewardsSet))]
    public static class BeginRewardsSetGate
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            if (CombatRewardPatch.IsDemocracyActive)
                RewardPool.IsRewardPhaseActive = true;
        }
    }
}
