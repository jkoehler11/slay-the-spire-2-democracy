using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Players;
using DemocracyMod.DemocracyModCode.Networking;

namespace DemocracyMod.DemocracyModCode;

public static class RewardPool
{
    internal static readonly SpireField<Player, int> PlayerWinCount = new(() => 0);

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
        public int CardChoiceCount { get; init; }
        public string DisplayName => Type switch
        {
            RewardType.CardReward => $"Card Reward ({CardChoiceCount} choices)",
            RewardType.GoldPile => $"{GoldAmount} Gold",
            RewardType.Potion => PotionName ?? "Potion",
            RewardType.Relic => RelicName ?? "Relic",
            RewardType.BossRelic => $"Boss Relic: {RelicName ?? "Unknown"}",
            _ => "Unknown Reward"
        };
    }

    private static readonly List<PoolEntry> Entries = new();
    private static readonly object LockObj = new();
    private static int _totalGoldPooled;

    public static int TotalGoldPooled => _totalGoldPooled;
    public static bool HasPending => GetPending().Count > 0;

    public static List<PoolEntry> GetPending()
    { lock (LockObj) return Entries.Where(e => !e.Distributed).ToList(); }

    public static PoolEntry? GetEntry(string id)
    { lock (LockObj) return Entries.FirstOrDefault(e => e.Id == id); }

    public static void MarkDistributed(string id, ulong winnerId)
    {
        lock (LockObj) { var e = Entries.FirstOrDefault(x => x.Id == id); if (e != null) { e.WinnerPlayerId = winnerId; e.Distributed = true; } }
    }

    public static void AddGoldReward(ulong sourceId, int amount)
    { _totalGoldPooled += amount; lock (LockObj) Entries.Add(new PoolEntry { Type = PoolEntry.RewardType.GoldPile, SourcePlayerId = sourceId, GoldAmount = amount }); }

    public static void AddCardReward(ulong sourceId, int choiceCount)
    { lock (LockObj) Entries.Add(new PoolEntry { Type = PoolEntry.RewardType.CardReward, SourcePlayerId = sourceId, CardChoiceCount = choiceCount }); }

    public static void AddPotionReward(ulong sourceId, string name)
    { lock (LockObj) Entries.Add(new PoolEntry { Type = PoolEntry.RewardType.Potion, SourcePlayerId = sourceId, PotionName = name }); }

    public static void AddRelicReward(ulong sourceId, string name, bool isBoss)
    { lock (LockObj) Entries.Add(new PoolEntry { Type = isBoss ? PoolEntry.RewardType.BossRelic : PoolEntry.RewardType.Relic, SourcePlayerId = sourceId, RelicName = name }); }

    public static void Clear()
    { lock (LockObj) { Entries.Clear(); _totalGoldPooled = 0; } }

    internal static void RegisterSpireFields() { }
}
