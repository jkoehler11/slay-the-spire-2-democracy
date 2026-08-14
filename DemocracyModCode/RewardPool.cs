using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Commands;
using DemocracyMod.DemocracyModCode.Networking;
using DemocracyMod.DemocracyModCode.Patches;

namespace DemocracyMod.DemocracyModCode;

public static class RewardPool
{
    internal static readonly Dictionary<ulong, int> PlayerWinCount = new();

    public class PoolEntry
    {
        public enum RewardType { CardReward, GoldPile, Potion, Relic, BossRelic }
        /// <summary>Deterministic id (identical on every machine) so claims match across the network.</summary>
        public string Id { get; init; } = "";
        public RewardType Type { get; init; }
        public ulong SourcePlayerId { get; init; }
        public ulong? WinnerPlayerId { get; set; }
        public bool Distributed { get; set; }
        public int GoldAmount { get; init; }
        public string? PotionName { get; init; }
        public string? RelicName { get; init; }
        public string? CardNames { get; init; }
        public int CardChoiceCount { get; init; }
        public CardModel? Card { get; set; }
        public RelicModel? Relic { get; set; }
        public PotionModel? Potion { get; set; }
        public string DisplayName => Type switch
        {
            RewardType.CardReward => Card?.Title ?? CardNames ?? string.Format("Card Reward ({0} choices)", CardChoiceCount),
            RewardType.GoldPile => string.Format("{0} Gold", GoldAmount),
            RewardType.Potion => PotionName ?? "Potion",
            RewardType.Relic => RelicName ?? "Relic",
            RewardType.BossRelic => string.Format("Boss Relic: {0}", RelicName ?? "Unknown"),
            _ => "Unknown Reward",
        };
    }

    private static readonly List<PoolEntry> Entries = new();
    private static readonly object LockObj = new();
    private static int _totalGoldPooled;
    private static int _totalCardsPooled;
    private static int _totalPotionsPooled;
    private static int _totalRelicsPooled;

    // Per-source-player sequence counter for deterministic, cross-machine-stable entry ids.
    private static readonly Dictionary<ulong, int> PoolSeq = new();

    // Captured granted objects, paired with the following AfterRewardTaken (same reward).
    private sealed class PendingGrant
    {
        public ulong PlayerId = 0;
        public PoolEntry.RewardType Type = PoolEntry.RewardType.CardReward;
        public object Model = null!;
    }
    private static readonly List<PendingGrant> PendingGrants = new();
    private static readonly object GrantLock = new();

    /// <summary>Only capture reward grants during the reward phase (not run-start or combat).</summary>
    public static volatile bool IsRewardPhaseActive;

    public static int TotalGoldPooled => _totalGoldPooled;
    public static int TotalCardsPooled => _totalCardsPooled;
    public static int TotalPotionsPooled => _totalPotionsPooled;
    public static int TotalRelicsPooled => _totalRelicsPooled;
    public static bool HasPending => GetPending().Count > 0;

    public static List<PoolEntry> GetPending()
    {
        lock (LockObj) return Entries.Where(e => !e.Distributed).ToList();
    }

    public static List<PoolEntry> GetNonGoldPending()
    {
        lock (LockObj) return Entries.Where(e => !e.Distributed && e.Type != PoolEntry.RewardType.GoldPile).ToList();
    }

    public static PoolEntry? GetEntry(string id)
    {
        lock (LockObj) return Entries.FirstOrDefault(e => e.Id == id);
    }

    public static void MarkDistributed(string id, ulong winnerId)
    {
        lock (LockObj)
        {
            var e = Entries.FirstOrDefault(x => x.Id == id);
            if (e != null) { e.WinnerPlayerId = winnerId; e.Distributed = true; }
            PlayerWinCount[winnerId] = PlayerWinCount.GetValueOrDefault(winnerId, 0) + 1;
        }
    }

    public static void MarkDiscarded(string id)
    {
        lock (LockObj)
        {
            var e = Entries.FirstOrDefault(x => x.Id == id);
            if (e != null) { e.WinnerPlayerId = null; e.Distributed = true; }
        }
    }

    private static string NextId(ulong sourceId, PoolEntry.RewardType type)
    {
        int n = PoolSeq.GetValueOrDefault(sourceId, 0);
        PoolSeq[sourceId] = n + 1;
        return string.Format("{0}:{1}:{2}", sourceId, type, n);
    }

    // ---- Pending-grant capture (from the synced grant commands) ----
    public static void NoteGrantedCard(ulong playerId, CardModel card)
    { lock (GrantLock) PendingGrants.Add(new PendingGrant { PlayerId = playerId, Type = PoolEntry.RewardType.CardReward, Model = card }); }
    public static void NoteGrantedRelic(ulong playerId, RelicModel relic)
    { lock (GrantLock) PendingGrants.Add(new PendingGrant { PlayerId = playerId, Type = PoolEntry.RewardType.Relic, Model = relic }); }
    public static void NoteGrantedPotion(ulong playerId, PotionModel potion)
    { lock (GrantLock) PendingGrants.Add(new PendingGrant { PlayerId = playerId, Type = PoolEntry.RewardType.Potion, Model = potion }); }

    public static object? TakePendingGrant(ulong playerId, PoolEntry.RewardType type)
    {
        lock (GrantLock)
        {
            var g = PendingGrants.FirstOrDefault(x => x.PlayerId == playerId && x.Type == type);
            if (g == null) return null;
            PendingGrants.Remove(g);
            return g.Model;
        }
    }

    public static void AddGoldReward(ulong sourceId, int amount)
    {
        lock (LockObj)
        {
            _totalGoldPooled += amount;
            Entries.Add(new PoolEntry { Id = NextId(sourceId, PoolEntry.RewardType.GoldPile), Type = PoolEntry.RewardType.GoldPile, SourcePlayerId = sourceId, GoldAmount = amount });
        }
    }

    public static void AddCardReward(ulong sourceId, int choiceCount, string? cardNames, CardModel? card = null)
    {
        lock (LockObj)
        {
            _totalCardsPooled++;
            Entries.Add(new PoolEntry { Id = NextId(sourceId, PoolEntry.RewardType.CardReward), Type = PoolEntry.RewardType.CardReward, SourcePlayerId = sourceId, CardChoiceCount = choiceCount, CardNames = cardNames, Card = card });
        }
    }

    public static void AddPotionReward(ulong sourceId, string name, PotionModel? potion = null)
    {
        lock (LockObj)
        {
            _totalPotionsPooled++;
            Entries.Add(new PoolEntry { Id = NextId(sourceId, PoolEntry.RewardType.Potion), Type = PoolEntry.RewardType.Potion, SourcePlayerId = sourceId, PotionName = name, Potion = potion });
        }
    }

    public static void AddRelicReward(ulong sourceId, string name, bool isBoss, RelicModel? relic = null)
    {
        lock (LockObj)
        {
            _totalRelicsPooled++;
            Entries.Add(new PoolEntry { Id = NextId(sourceId, isBoss ? PoolEntry.RewardType.BossRelic : PoolEntry.RewardType.Relic), Type = isBoss ? PoolEntry.RewardType.BossRelic : PoolEntry.RewardType.Relic, SourcePlayerId = sourceId, RelicName = name, Relic = relic });
        }
    }

    // ---- Gold grant / reclaim ----
    public static bool GrantGold(ulong playerId, int amount)
    {
        var player = CombatRewardPatch.GetPlayer(playerId);
        if (player == null)
        {
            MainFile.LogVote(string.Format("Democracy: grant gold failed — no cached player P{0}", playerId));
            return false;
        }
        var current = Traverse.Create(player).Property<int>("Gold").Value;
        Traverse.Create(player).Property<int>("Gold").Value = current + amount;
        MainFile.LogVote(string.Format("Democracy: Granted {0}g to P{1} (now {2}g)", amount, playerId, current + amount));
        return true;
    }

    public static bool RemoveGold(ulong playerId, int amount)
    {
        var player = CombatRewardPatch.GetPlayer(playerId);
        if (player == null) return false;
        var current = Traverse.Create(player).Property<int>("Gold").Value;
        var next = Math.Max(0, current - amount);
        Traverse.Create(player).Property<int>("Gold").Value = next;
        MainFile.LogVote(string.Format("Democracy: Reclaimed {0}g from P{1} (now {2}g)", amount, playerId, next));
        return true;
    }

    // ---- Transfer / discard of the actual granted objects ----
    public static async Task TransferReward(PoolEntry e, ulong winnerId)
    {
        var source = CombatRewardPatch.GetPlayer(e.SourcePlayerId);
        var winner = CombatRewardPatch.GetPlayer(winnerId);
        if (source == null || winner == null)
        {
            MainFile.LogDebug(string.Format("Democracy: transfer {0} skipped — missing P{1} or P{2}", e.DisplayName, e.SourcePlayerId, winnerId));
            return;
        }
        try
        {
            switch (e.Type)
            {
                case PoolEntry.RewardType.CardReward:
                    if (e.Card == null) { MainFile.LogDebug(string.Format("Democracy: transfer {0} skipped — no captured card", e.DisplayName)); return; }
                    {
                        // ---- diagnostics ----
                        string pileName = e.Card.Pile?.Type.ToString() ?? "NULL";
                        ulong ownerId = e.Card.Owner?.NetId ?? 0;
                        bool removed = e.Card.HasBeenRemovedFromState;
                        int srcDeck = source.Deck?.Cards.Count ?? -1;
                        int winDeck = winner.Deck?.Cards.Count ?? -1;
                        MainFile.LogDebug(string.Format(
                            "Democracy: XFER-BEFORE card={0} owner={1} pile={2} removed={3} srcDeck={4} winDeck={5}",
                            e.Card.Title, ownerId, pileName, removed, srcDeck, winDeck));
                        MainFile.LogDebug("Democracy:   SRC-DECK-BEFORE " + DeckTitles(source));
                        MainFile.LogDebug("Democracy:   WIN-DECK-BEFORE " + DeckTitles(winner));

                        await CardPileCmd.GiveToAnotherPlayer(e.Card, winner, PileType.Deck, CardPilePosition.Bottom, null);

                        MainFile.LogDebug("Democracy:   SRC-DECK-AFTER " + DeckTitles(source));
                        MainFile.LogDebug("Democracy:   WIN-DECK-AFTER " + DeckTitles(winner));

                        string pileName2 = e.Card.Pile?.Type.ToString() ?? "NULL";
                        ulong ownerId2 = e.Card.Owner?.NetId ?? 0;
                        int srcDeck2 = source.Deck?.Cards.Count ?? -1;
                        int winDeck2 = winner.Deck?.Cards.Count ?? -1;
                        MainFile.LogDebug(string.Format(
                            "Democracy: XFER-AFTER  card={0} owner={1} pile={2} srcDeck={3} winDeck={4}",
                            e.Card.Title, ownerId2, pileName2, srcDeck2, winDeck2));
                    }
                    break;
                case PoolEntry.RewardType.Relic:
                case PoolEntry.RewardType.BossRelic:
                    if (e.Relic == null) { MainFile.LogDebug(string.Format("Democracy: transfer {0} skipped — no captured relic", e.DisplayName)); return; }
                    var relic = source.Relics.FirstOrDefault(r => r.Id.Equals(e.Relic.Id));
                    if (relic == null) { MainFile.LogDebug(string.Format("Democracy: transfer {0} skipped — relic not in source inventory", e.DisplayName)); return; }
                    await RelicCmd.Remove(relic);
                    var relicCanonical = ModelDb.AllRelics.FirstOrDefault(r => r.Id.Equals(e.Relic.Id));
                    if (relicCanonical == null) { MainFile.LogDebug(string.Format("Democracy: transfer {0} skipped — no canonical relic template", e.DisplayName)); return; }
                    await RelicCmd.Obtain(relicCanonical.ToMutable(), winner, -1);
                    break;
                case PoolEntry.RewardType.Potion:
                    if (e.Potion == null) { MainFile.LogDebug(string.Format("Democracy: transfer {0} skipped — no captured potion", e.DisplayName)); return; }
                    var potion = source.PotionSlots.FirstOrDefault(p => p != null && p.Id.Equals(e.Potion.Id));
                    if (potion != null)
                        await PotionCmd.Discard(potion);
                    var potionCanonical = ModelDb.AllPotions.FirstOrDefault(p => p.Id.Equals(e.Potion.Id));
                    if (potionCanonical == null) { MainFile.LogDebug(string.Format("Democracy: transfer {0} skipped — no canonical potion template", e.DisplayName)); return; }
                    await PotionCmd.TryToProcure(potionCanonical.ToMutable(), winner, -1);
                    break;
            }
            MainFile.LogVote(string.Format("Democracy: transferred {0} P{1} -> P{2}", e.DisplayName, e.SourcePlayerId, winnerId));
        }
        catch (Exception ex)
        {
            MainFile.LogDebug(string.Format("Democracy: transfer {0} error: {1}", e.DisplayName, ex.Message));
        }
    }


    public static async Task DiscardReward(PoolEntry e)
    {
        var source = CombatRewardPatch.GetPlayer(e.SourcePlayerId);
        if (source == null) return;
        try
        {
            switch (e.Type)
            {
                case PoolEntry.RewardType.CardReward:
                    if (e.Card != null) await CardPileCmd.RemoveFromDeck(e.Card, false);
                    break;
                case PoolEntry.RewardType.Relic:
                case PoolEntry.RewardType.BossRelic:
                    if (e.Relic != null)
                    {
                        var relic = source.Relics.FirstOrDefault(r => r.Id.Equals(e.Relic.Id));
                        if (relic != null) await RelicCmd.Remove(relic);
                    }
                    break;
                case PoolEntry.RewardType.Potion:
                    if (e.Potion != null)
                    {
                        var potion = source.PotionSlots.FirstOrDefault(p => p != null && p.Id.Equals(e.Potion.Id));
                        if (potion != null) await PotionCmd.Discard(potion);
                    }
                    break;
            }
            MainFile.LogVote(string.Format("Democracy: discarded {0}", e.DisplayName));
        }
        catch (Exception ex)
        {
            MainFile.LogDebug(string.Format("Democracy: discard {0} error: {1}", e.DisplayName, ex.Message));
        }
    }


    public static void DistributeEvenly()
    {
        var pending = GetPending();
        var playerIds = CombatRewardPatch.GetSeenPlayerIds();
        if (playerIds.Count == 0 || pending.Count == 0) return;

        var playerIndex = 0;
        foreach (var entry in pending)
        {
            var winner = playerIds[playerIndex % playerIds.Count];
            MarkDistributed(entry.Id, winner);
            if (entry.Type == PoolEntry.RewardType.GoldPile)
                GrantGold(winner, entry.GoldAmount);
            MainFile.LogVote(string.Format("Democracy: Distributed {0} from P{1} -> P{2}",
                entry.DisplayName, entry.SourcePlayerId, winner));
            playerIndex++;
        }
    }

    public static void Clear()
    {
        lock (LockObj) { Entries.Clear(); _totalGoldPooled = 0; _totalCardsPooled = 0; _totalPotionsPooled = 0; _totalRelicsPooled = 0; PoolSeq.Clear(); }
        lock (GrantLock) PendingGrants.Clear();
        IsRewardPhaseActive = false;
    }


    private static string DeckTitles(Player p)
    {
        try
        {
            var d = p?.Deck;
            if (d == null || d.Cards == null) return "NODECK";
            return string.Join(", ", d.Cards.Select(c => c.Title));
        }
        catch { return "ERR"; }
    }

    internal static void RegisterSpireFields() { }
}
