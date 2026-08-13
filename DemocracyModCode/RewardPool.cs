using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using DemocracyMod.DemocracyModCode.Networking;
using DemocracyMod.DemocracyModCode.Patches;

namespace DemocracyMod.DemocracyModCode;

public static class RewardPool
{
    internal static readonly Dictionary<ulong, int> PlayerWinCount = new();

    public class PoolEntry
    {
        public enum RewardType { CardReward, GoldPile, Potion, Relic, BossRelic }
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
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

    // Actual granted objects captured from HandleRewardObtainedMessage, matched up
    // with the following AfterRewardTaken (same reward, fires right after).
    private sealed class PendingGrant
    {
        public ulong PlayerId = 0;
        public PoolEntry.RewardType Type = PoolEntry.RewardType.CardReward;
        public object Model = null!;
    }
    private static readonly List<PendingGrant> PendingGrants = new();
    private static readonly object GrantLock = new();

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

    // ---- Pending-grant capture (called from CombatRewardPatch's HandleRewardObtainedMessage hook) ----
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
        _totalGoldPooled += amount;
        lock (LockObj) Entries.Add(new PoolEntry { Type = PoolEntry.RewardType.GoldPile, SourcePlayerId = sourceId, GoldAmount = amount });
    }

    public static void AddCardReward(ulong sourceId, int choiceCount, string? cardNames, CardModel? card = null)
    {
        _totalCardsPooled++;
        lock (LockObj) Entries.Add(new PoolEntry { Type = PoolEntry.RewardType.CardReward, SourcePlayerId = sourceId, CardChoiceCount = choiceCount, CardNames = cardNames, Card = card });
    }

    public static void AddPotionReward(ulong sourceId, string name, PotionModel? potion = null)
    {
        _totalPotionsPooled++;
        lock (LockObj) Entries.Add(new PoolEntry { Type = PoolEntry.RewardType.Potion, SourcePlayerId = sourceId, PotionName = name, Potion = potion });
    }

    public static void AddRelicReward(ulong sourceId, string name, bool isBoss, RelicModel? relic = null)
    {
        _totalRelicsPooled++;
        lock (LockObj) Entries.Add(new PoolEntry { Type = isBoss ? PoolEntry.RewardType.BossRelic : PoolEntry.RewardType.Relic, SourcePlayerId = sourceId, RelicName = name, Relic = relic });
    }

    // ---- Gold grant / reclaim ----
    public static bool GrantGold(ulong playerId, int amount)
    {
        var player = CombatRewardPatch.GetPlayer(playerId);
        if (player == null)
        {
            MainFile.Logger.Info(string.Format("Democracy: grant gold failed — no cached player P{0}", playerId));
            return false;
        }
        var current = Traverse.Create(player).Property<int>("Gold").Value;
        Traverse.Create(player).Property<int>("Gold").Value = current + amount;
        MainFile.Logger.Info(string.Format("Democracy: Granted {0}g to P{1} (now {2}g)", amount, playerId, current + amount));
        return true;
    }

    public static bool RemoveGold(ulong playerId, int amount)
    {
        var player = CombatRewardPatch.GetPlayer(playerId);
        if (player == null) return false;
        var current = Traverse.Create(player).Property<int>("Gold").Value;
        var next = Math.Max(0, current - amount);
        Traverse.Create(player).Property<int>("Gold").Value = next;
        MainFile.Logger.Info(string.Format("Democracy: Reclaimed {0}g from P{1} (now {2}g)", amount, playerId, next));
        return true;
    }

    // ---- Transfer / discard of the actual granted objects ----
    public static void TransferReward(PoolEntry e, ulong winnerId)
    {
        var source = CombatRewardPatch.GetPlayer(e.SourcePlayerId);
        var winner = CombatRewardPatch.GetPlayer(winnerId);
        if (source == null || winner == null)
        {
            MainFile.Logger.Info(string.Format("Democracy: transfer {0} skipped — missing P{1} or P{2}", e.DisplayName, e.SourcePlayerId, winnerId));
            return;
        }
        try
        {
            switch (e.Type)
            {
                case PoolEntry.RewardType.CardReward:
                    if (e.Card == null) return;
                    source.RunState.RemoveCard(e.Card);
                    winner.RunState.AddCard(e.Card, winner);
                    break;
                case PoolEntry.RewardType.Relic:
                case PoolEntry.RewardType.BossRelic:
                    if (e.Relic == null) return;
                    var relic = source.Relics.FirstOrDefault(r => r.Id.Equals(e.Relic.Id));
                    if (relic == null) return;
                    source.RemoveRelicInternal(relic, true);
                    winner.AddRelicInternal(relic, 0, true);
                    break;
                case PoolEntry.RewardType.Potion:
                    if (e.Potion == null) return;
                    var potion = source.PotionSlots.FirstOrDefault(p => p != null && p.Id.Equals(e.Potion.Id));
                    if (potion == null) return;
                    source.RemovePotionInternal(potion);
                    winner.AddPotionInternal(potion, -1, true);
                    break;
            }
            MainFile.Logger.Info(string.Format("Democracy: transferred {0} P{1} -> P{2}", e.DisplayName, e.SourcePlayerId, winnerId));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info(string.Format("Democracy: transfer {0} error: {1}", e.DisplayName, ex.Message));
        }
    }

    public static void DiscardReward(PoolEntry e)
    {
        var source = CombatRewardPatch.GetPlayer(e.SourcePlayerId);
        if (source == null) return;
        try
        {
            switch (e.Type)
            {
                case PoolEntry.RewardType.CardReward:
                    if (e.Card != null) source.RunState.RemoveCard(e.Card);
                    break;
                case PoolEntry.RewardType.Relic:
                case PoolEntry.RewardType.BossRelic:
                    if (e.Relic != null)
                    {
                        var relic = source.Relics.FirstOrDefault(r => r.Id.Equals(e.Relic.Id));
                        if (relic != null) source.RemoveRelicInternal(relic, true);
                    }
                    break;
                case PoolEntry.RewardType.Potion:
                    if (e.Potion != null)
                    {
                        var potion = source.PotionSlots.FirstOrDefault(p => p != null && p.Id.Equals(e.Potion.Id));
                        if (potion != null) source.RemovePotionInternal(potion);
                    }
                    break;
            }
            MainFile.Logger.Info(string.Format("Democracy: discarded {0}", e.DisplayName));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info(string.Format("Democracy: discard {0} error: {1}", e.DisplayName, ex.Message));
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
            MainFile.Logger.Info(string.Format("Democracy: Distributed {0} from P{1} -> P{2}",
                entry.DisplayName, entry.SourcePlayerId, winner));
            playerIndex++;
        }
    }

    public static void Clear()
    {
        lock (LockObj) { Entries.Clear(); _totalGoldPooled = 0; _totalCardsPooled = 0; _totalPotionsPooled = 0; _totalRelicsPooled = 0; }
        lock (GrantLock) PendingGrants.Clear();
    }

    internal static void RegisterSpireFields() { }
}
