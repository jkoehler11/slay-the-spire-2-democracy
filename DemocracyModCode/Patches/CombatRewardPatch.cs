using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;

namespace DemocracyMod.DemocracyModCode.Patches;

public static class CombatRewardPatch
{
    public static bool IsDemocracyActive { get; set; }

    // Cache real Player objects by NetId so we can grant rewards / read gold later
    private static readonly Dictionary<ulong, Player> _playersById = new();

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "AfterRewardTaken")]
    public static class PoolAfterRewardTaken
    {
        [HarmonyPostfix]
        static void Postfix(Player player, Reward reward)
        {
            if (!IsDemocracyActive) return;
            _playersById[player.NetId] = player;

            switch (reward)
            {
                case GoldReward gold:
                    RewardPool.AddGoldReward(player.NetId, gold.Amount);
                    MainFile.Logger.Info(string.Format("Democracy: {0}g from P{1} pool {2}g", gold.Amount, player.NetId, RewardPool.TotalGoldPooled));
                    break;
                case CardReward card:
                    string cardNames;
                    try
                    {
                        var cards = card.Cards?.ToList();
                        cardNames = (cards != null && cards.Count > 0)
                            ? string.Join(", ", cards.Select(c => c.Title))
                            : "Card Reward";
                    }
                    catch { cardNames = "Card Reward"; }
                    var cardModel = RewardPool.TakePendingGrant(player.NetId, RewardPool.PoolEntry.RewardType.CardReward) as CardModel;
                    RewardPool.AddCardReward(player.NetId, card.OptionCount, cardNames, cardModel);
                    MainFile.Logger.Info(string.Format("Democracy: card [{0}] from P{1} pool {2}c", cardModel?.Title ?? cardNames, player.NetId, RewardPool.TotalCardsPooled));
                    break;
                case PotionReward potion:
                    var potionModel = RewardPool.TakePendingGrant(player.NetId, RewardPool.PoolEntry.RewardType.Potion) as PotionModel;
                    RewardPool.AddPotionReward(player.NetId, LocName(potion.Potion?.Title), potionModel);
                    MainFile.Logger.Info(string.Format("Democracy: potion from P{0} pool {1}p", player.NetId, RewardPool.TotalPotionsPooled));
                    break;
                case RelicReward relic:
                    var relicModel = RewardPool.TakePendingGrant(player.NetId, RewardPool.PoolEntry.RewardType.Relic) as RelicModel;
                    RewardPool.AddRelicReward(player.NetId, LocName(relic.Relic?.Title), false, relicModel);
                    MainFile.Logger.Info(string.Format("Democracy: relic from P{0} pool {1}r", player.NetId, RewardPool.TotalRelicsPooled));
                    break;
                default:
                    RewardPool.AddCardReward(player.NetId, 1, reward.GetType().Name);
                    break;
            }

            // Re-check completion — this is the signal that a reward was actually picked.
            PostCombatPatch.NotifyRewardPooled();
        }
    }

    /// <summary>
    /// Captures the ACTUAL granted object (card/potion/relic) from the game's
    /// reward-obtained message. This fires on every client for every player's
    /// reward, before AfterRewardTaken, so the pool can store the real granted
    /// model (not just the display name / option list).
    /// </summary>
    [HarmonyPatch(typeof(RewardSynchronizer), nameof(RewardSynchronizer.HandleRewardObtainedMessage))]
    public static class CaptureGrantedReward
    {
        [HarmonyPostfix]
        static void Postfix(RewardObtainedMessage message, ulong senderId)
        {
            if (!IsDemocracyActive) return;
            if (message.wasSkipped) return;
            switch (message.rewardType)
            {
                case RewardType.Card:
                    if (message.cardModel != null) RewardPool.NoteGrantedCard(senderId, message.cardModel);
                    break;
                case RewardType.Relic:
                    if (message.relicModel != null) RewardPool.NoteGrantedRelic(senderId, message.relicModel);
                    break;
                case RewardType.Potion:
                    if (message.potionModel != null) RewardPool.NoteGrantedPotion(senderId, message.potionModel);
                    break;
            }
        }
    }

    public static int GetSeenPlayerCount() => _playersById.Count;

    public static List<ulong> GetSeenPlayerIds() => new(_playersById.Keys);

    public static Player? GetPlayer(ulong id) => _playersById.GetValueOrDefault(id);

    public static List<Player> GetSeenPlayers() => new(_playersById.Values);

    /// <summary>Resolve a localized title to its display text.</summary>
    private static string LocName(LocString? ls)
    {
        if (ls == null) return "?";
        try
        {
            var text = ls.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("LocString table", StringComparison.Ordinal))
                return text;
        }
        catch { }

        try
        {
            var key = ls.LocEntryKey ?? "";
            var id = key;
            var dot = id.LastIndexOf('.');
            if (dot > 0) id = id[..dot];
            return TitleCase(id);
        }
        catch { return "?"; }
    }

    /// <summary>"RADIANT_TINCTURE" -> "Radiant Tincture".</summary>
    private static string TitleCase(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "?";
        var words = id.ToLowerInvariant().Split('_');
        for (var i = 0; i < words.Length; i++)
            if (words[i].Length > 0)
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        return string.Join(" ", words.Where(w => w.Length > 0));
    }

    public static void ResetTracking()
    {
        _playersById.Clear();
    }
}
