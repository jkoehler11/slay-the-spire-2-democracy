using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using DemocracyMod.DemocracyModCode;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using Godot;

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
        static void Postfix(IRunState runState, Player player, Reward reward)
        {
            if (!IsDemocracyActive) return;
            // Deterministic gate: only pool COMBAT rewards. runState.CurrentRoom is
            // network-synced and identical on both machines. Do NOT use set.Room here
            // (host-local, null on the remote machine) — that caused a cross-machine
            // desync where the client missed rewards the host pooled.
            if (runState?.CurrentRoom is not CombatRoom)
            {
                MainFile.LogDebug(string.Format(
                    "Democracy: skip pool (room={0})",
                    runState?.CurrentRoom?.GetType().Name ?? "null"));
                return;
            }
            _playersById[player.NetId] = player;

            switch (reward)
            {
                case GoldReward gold:
                    RewardPool.AddGoldReward(player.NetId, gold.Amount);
                    MainFile.LogReward(string.Format("Democracy: {0}g from P{1} pool {2}g", gold.Amount, player.NetId, RewardPool.TotalGoldPooled));
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
                    MainFile.LogReward(string.Format("Democracy: card [{0}] from P{1} pool {2}c",
                        cardModel?.Title ?? cardNames, player.NetId, RewardPool.TotalCardsPooled));
                    break;
                case PotionReward potion:
                    var potionModel = RewardPool.TakePendingGrant(player.NetId, RewardPool.PoolEntry.RewardType.Potion) as PotionModel;
                    RewardPool.AddPotionReward(player.NetId, LocName(potion.Potion?.Title), potionModel);
                    MainFile.LogReward(string.Format("Democracy: potion [{0}] from P{1} pool {2}p",
                        potionModel?.Id.ToString() ?? LocName(potion.Potion?.Title), player.NetId, RewardPool.TotalPotionsPooled));
                    break;
                case RelicReward relic:
                    var relicModel = RewardPool.TakePendingGrant(player.NetId, RewardPool.PoolEntry.RewardType.Relic) as RelicModel;
                    RewardPool.AddRelicReward(player.NetId, LocName(relic.Relic?.Title), false, relicModel);
                    MainFile.LogReward(string.Format("Democracy: relic [{0}] from P{1} pool {2}r",
                        relicModel?.Id.ToString() ?? LocName(relic.Relic?.Title), player.NetId, RewardPool.TotalRelicsPooled));
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
    /// Card rewards are granted by the synced CardPileCmd.Add(card, PileType.Deck, ...)
    /// command, which runs on EVERY machine for EVERY player's card (local picks and
    /// remote-picked cards both flow through it). Capture the granted card here.
    /// </summary>
    [HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add),
        new[] { typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool) })]
    public static class CaptureCardGrant
    {
        [HarmonyPostfix]
        static void Postfix(CardModel card, PileType newPileType)
        {
            if (!IsDemocracyActive) return;
            if (!RewardPool.IsRewardPhaseActive) return;
            if (newPileType != PileType.Deck) return;
            if (card == null || card.Owner == null) return;
            RewardPool.NoteGrantedCard(card.Owner.NetId, card);
        }
    }

    [HarmonyPatch(typeof(PotionCmd), nameof(PotionCmd.TryToProcure),
        new[] { typeof(PotionModel), typeof(Player), typeof(int) })]
    public static class CapturePotionGrant
    {
        [HarmonyPostfix]
        static void Postfix(PotionModel potion, Player player)
        {
            if (!IsDemocracyActive) return;
            if (!RewardPool.IsRewardPhaseActive) return;
            if (potion == null || player == null) return;
            RewardPool.NoteGrantedPotion(player.NetId, potion);
        }
    }

    [HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain),
        new[] { typeof(RelicModel), typeof(Player), typeof(int) })]
    public static class CaptureRelicGrant
    {
        [HarmonyPostfix]
        static void Postfix(RelicModel relic, Player player)
        {
            if (!IsDemocracyActive) return;
            if (!RewardPool.IsRewardPhaseActive) return;
            if (relic == null || player == null) return;
            RewardPool.NoteGrantedRelic(player.NetId, relic);
        }
    }

    public static int GetSeenPlayerCount() => _playersById.Count;

    public static List<ulong> GetSeenPlayerIds() => _playersById.Keys.OrderBy(id => id).ToList();

    public static Player? GetPlayer(ulong id) => _playersById.GetValueOrDefault(id);

    public static List<Player> GetSeenPlayers() => new(_playersById.Values);

    /// <summary>
    /// The deck view renders a private `_cards` snapshot refreshed only in _Ready
    /// (fires ONCE per scene instance) and OnPileContentsChanged. Reward transfers
    /// mutate the deck outside combat while the deck view is closed, so a cached scene
    /// reopened afterward repaints the stale snapshot. ShowScreen is the STATIC factory
    /// invoked by NTopBarDeckButton.OnRelease on every open, so it is the reliable
    /// per-open trigger (a cached screen may not re-fire _EnterTree). It returns the
    /// screen instance in __result; re-snapshot _cards from the live pile and re-render,
    /// deferred one frame so _grid is guaranteed bound. (Do NOT read __instance here —
    /// it is null because ShowScreen is static.)
    /// </summary>
    [HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen.ShowScreen))]
    public static class DeckViewRefreshFix
    {
        [HarmonyPostfix]
        static void Postfix(NDeckViewScreen __result)
        {
            var screen = __result;
            if (screen == null) return;
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            // Wait long enough for the async InitGrid (AnimateOut -> allocate ->
            // AnimateIn) to fully settle, then dump the ACTUAL rendered holder state.
            var timer = tree.CreateTimer(0.6);
            timer.Timeout += () =>
            {
                try
                {
                    if (!GodotObject.IsInstanceValid(screen)) return;
                    var pile = screen._pile;
                    var live = pile?.Cards?.ToList();

                    int slide = -1, cols = -1, rows = -1;
                    string slideInfo;
                    try
                    {
                        slide = screen._grid._slidingWindowCardIndex;
                        cols = screen._grid.Columns;
                        rows = screen._grid._cardRows?.Count ?? -1;
                        slideInfo = string.Format("slide={0} cols={1} rows={2}", slide, cols, rows);
                    }
                    catch { slideInfo = "grid-null"; }

                    int pileC = live?.Count ?? -1;
                    ulong pid = 0;
                    int modelDeck = -1;
                    string bottomLabel = "?";
                    try { pid = screen._player?.NetId ?? 0; } catch { }
                    try { modelDeck = screen._player?.Deck?.Cards?.Count ?? -1; } catch { }
                    try { bottomLabel = screen._bottomLabel?.Text ?? "?"; } catch { }

                    MainFile.LogDebug(string.Format(
                        "Democracy: DECKVIEW-DUMP P{0} pile={1}c model={2}c {3} bottom=\"{4}\"",
                        pid, pileC, modelDeck, slideInfo, bottomLabel));

                    // Per-holder ground truth: what card is actually assigned + visible.
                    try
                    {
                        if (screen._grid != null && screen._grid._cardRows != null)
                        {
                            int r = 0;
                            foreach (var row in screen._grid._cardRows)
                            {
                                if (row == null) continue;
                                var parts = new List<string>();
                                foreach (var h in row)
                                {
                                    if (h == null) { parts.Add("NULL"); continue; }
                                    string t = "?";
                                    try { t = h._baseCard?.Title ?? "EMPTY"; } catch { t = "?"; }
                                    bool vis = false;
                                    try { vis = h.Visible; } catch { }
                                    parts.Add(t + (vis ? "" : "(h)"));
                                }
                                MainFile.LogDebug(string.Format(
                                    "Democracy:   row{0}: {1}", r, string.Join(" | ", parts)));
                                r++;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        MainFile.LogDebug("Democracy: holder dump error: " + e.Message);
                    }

                    // Only re-render if the rendered holders clearly diverge from the live pile.
                    if (live == null) return;
                    bool needRefresh = screen._grid == null
                        || screen._grid._cards == null
                        || screen._grid._cards.Count != live.Count
                        || live.Any(c => !screen._grid._cards.Contains(c));

                    if (needRefresh)
                    {
                        MainFile.LogDebug("Democracy: deck view grid STALE — re-rendering");
                        screen._cards = live;
                        screen.DisplayCards();
                    }
                }
                catch (Exception e)
                {
                    MainFile.LogDebug("Democracy: deck view refresh error: " + e.Message);
                }
            };
        }
    }

    /// <summary>
    /// The top-bar deck button shows the live deck count in `_countLabel`, updated only
    /// via OnPileContentsChanged (fired by the pile's CardAddFinished/CardRemoveFinished
    /// events). Log the actual label text vs the pile count to pinpoint the off-by-one
    /// the user reports (a transfer that doesn't re-fire this event leaves a stale count).
    /// </summary>
    [HarmonyPatch(typeof(NTopBarDeckButton), nameof(NTopBarDeckButton.OnPileContentsChanged))]
    public static class DeckCountLabelProbe
    {
        [HarmonyPostfix]
        static void Postfix(NTopBarDeckButton __instance)
        {
            try
            {
                int pile = __instance._pile?.Cards?.Count ?? -1;
                string label;
                try { label = __instance._countLabel?.Text ?? "null"; } catch { label = "ERR"; }
                ulong pid = __instance._player?.NetId ?? 0;
                MainFile.LogDebug(string.Format("Democracy: DECKBUTTON P{0} pile={1} label=\"{2}\"", pid, pile, label));
            }
            catch (Exception e)
            {
                MainFile.LogDebug("Democracy: DECKBUTTON probe error: " + e.Message);
            }
        }
    }

    private static NTopBarDeckButton? _deckButton;

    [HarmonyPatch(typeof(NTopBarDeckButton), nameof(NTopBarDeckButton.Initialize))]
    public static class TrackDeckButton
    {
        [HarmonyPostfix]
        static void Postfix(NTopBarDeckButton __instance)
        {
            _deckButton = __instance;
        }
    }

    /// <summary>
    /// CardPileCmd.GiveToAnotherPlayer moves a card between decks WITHOUT firing the
    /// pile's CardAddFinished/CardRemoveFinished events, so the top-bar deck button's
    /// _countLabel (updated only in OnPileContentsChanged) goes stale after every
    /// transfer — winner shows one too few, source shows one too many. Call this after
    /// distribution to force the label to re-read the live pile count.
    /// </summary>
    public static void RefreshDeckCount()
    {
        try
        {
            if (_deckButton != null && GodotObject.IsInstanceValid(_deckButton))
                _deckButton.OnPileContentsChanged();
        }
        catch (Exception e)
        {
            MainFile.LogDebug("Democracy: refresh deck count error: " + e.Message);
        }
    }

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
