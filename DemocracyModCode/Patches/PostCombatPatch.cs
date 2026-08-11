using HarmonyLib;
using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace DemocracyMod.DemocracyModCode.Patches;

public static class PostCombatPatch
{
    [HarmonyPatch(typeof(RewardSynchronizer), "OnCombatEnded")]
    public static class TriggerDemocracyAfterCombat
    {
        [HarmonyPostfix]
        static void Postfix(RewardSynchronizer __instance, CombatRoom room)
        {
            if (!CombatRewardPatch.IsDemocracyActive) return;
            MainFile.Logger.Info("=== Combat ended. Collecting rewards for Democracy pool ===");
            MultiplayerCoordinator.InitializeForRun();
            CollectAndPoolRewards(room);

            if (RewardPool.HasPending)
            {
                MainFile.Logger.Info($"Pool has {RewardPool.GetPending().Count} entries. Starting vote flow.");
                if (MultiplayerCoordinator.IsHost)
                {
                    MultiplayerCoordinator.BroadcastPoolUpdate();
                    var playerIds = MultiplayerCoordinator.GetPlayers().Select(p => p.NetId).ToList();
                    VoteManager.StartNextEntry(playerIds);
                }
            }
            else MainFile.Logger.Info("No rewards to pool. Skipping Democracy.");
        }

        private static void CollectAndPoolRewards(CombatRoom room)
        {
            var extraRewards = room.ExtraRewards;
            if (extraRewards == null || extraRewards.Count == 0) { MainFile.Logger.Info("No ExtraRewards to pool."); return; }
            foreach (var kvp in extraRewards)
            {
                var player = kvp.Key;
                MainFile.Logger.Info($"Pooling {kvp.Value.Count} rewards from P{player.NetId}");
                foreach (var reward in kvp.Value) PoolReward(reward, player);
                kvp.Value.Clear();
            }
        }

        private static void PoolReward(Reward reward, Player player)
        {
            switch (reward)
            {
                case GoldReward gold: RewardPool.AddGoldReward(player.NetId, gold.Amount); break;
                case CardReward card: RewardPool.AddCardReward(player.NetId, card.OptionCount); break;
                case PotionReward potion: RewardPool.AddPotionReward(player.NetId, potion.Potion?.Title?.ToString() ?? "Unknown Potion"); break;
                case RelicReward relic: RewardPool.AddRelicReward(player.NetId, relic.Relic?.Title?.ToString() ?? "Unknown Relic", false); break;
                case SpecialCardReward: RewardPool.AddCardReward(player.NetId, 1); break;
            }
        }
    }

    [HarmonyPatch(typeof(NCombatUi), "ShowRewards")]
    public static class SuppressVanillaRewardScreen
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            if (!CombatRewardPatch.IsDemocracyActive) return true;
            if (!RewardPool.HasPending) return true;
            MainFile.Logger.Info("Democracy: Suppressing vanilla reward screen.");
            return false;
        }
    }

    [HarmonyPatch(typeof(NCombatUi), "_Process")]
    public static class DemocracyUpdateTicker
    {
        [HarmonyPostfix]
        static void Postfix() { if (CombatRewardPatch.IsDemocracyActive) VoteManager.Update(); }
    }

    [HarmonyPatch(typeof(RunManager), "InitializeShared")]
    public static class OnRunStartReset
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RewardPool.Clear();
            VoteManager.Reset();
            MultiplayerCoordinator.InitializeForRun();
            var pc = MultiplayerCoordinator.GetPlayerCount();
            CombatRewardPatch.IsDemocracyActive = pc > 1;
            MainFile.Logger.Info(pc > 1 ? $"Democracy ACTIVATED: {pc} players." : "Single-player. Democracy idle.");
        }
    }
}
