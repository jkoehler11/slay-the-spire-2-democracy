using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Logging;

namespace DemocracyMod.DemocracyModCode.Networking;

/// <summary>
/// One player's vote for a single stage of the synchronized reward flow.
/// Stage 0 (gold) carries a GoldMode; stages 1-3 carry the reward entry ids the
/// player wants (potions / relics / cards). The host collects these and, once
/// every player has voted for the current stage, broadcasts a
/// DemocracyAdvanceMessage so everyone advances together.
/// </summary>
public sealed class DemocracyStageMessage : ICustomMessage
{
    public int Stage;
    public int GoldMode = -1;              // GoldVoteMode for stage 0, -1 otherwise
    public List<string> RewardIds = new();

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter w)
    {
        w.WriteInt(Stage);
        w.WriteInt(GoldMode);
        w.WriteInt(RewardIds.Count);
        foreach (var id in RewardIds) w.WriteString(id);
    }

    public void Deserialize(PacketReader r)
    {
        Stage = r.ReadInt();
        GoldMode = r.ReadInt();
        var c = r.ReadInt();
        RewardIds = new(c);
        for (var i = 0; i < c; i++) RewardIds.Add(r.ReadString());
    }

    public void HandleMessage(ulong senderId) => MultiplayerCoordinator.HandleStage(senderId, this);
}

/// <summary>
/// Host broadcast: every player has voted for the current stage, so everyone
/// advances to the next stage's screen. After the final stage (cards) the host
/// instead broadcasts the DemocracyResolvedMessage.
/// </summary>
public sealed class DemocracyAdvanceMessage : ICustomMessage
{
    public int NextStage;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter w) => w.WriteInt(NextStage);
    public void Deserialize(PacketReader r) => NextStage = r.ReadInt();
    public void HandleMessage(ulong senderId) => MultiplayerCoordinator.HandleAdvance(senderId, this);
}

/// <summary>
/// The HOST's authoritative distribution decision, broadcast after the final stage.
/// Carries the exact winner for each non-gold reward and the exact gold
/// reclaims/grants, so every client applies the SAME transfers instead of resolving
/// independently. WinnerId 0 means "discarded".
/// </summary>
public sealed class DemocracyResolvedMessage : ICustomMessage
{
    // Non-gold rewards (parallel arrays). WinnerId 0 == discarded.
    public List<string> EntryIds = new();
    public List<ulong> WinnerIds = new();

    // Gold reclaims (parallel arrays) — amount taken back from each source.
    public List<ulong> ReclaimPlayerIds = new();
    public List<int> ReclaimAmounts = new();

    // Gold grants (parallel arrays) — amount given to each player.
    public List<ulong> GoldPlayerIds = new();
    public List<int> GoldAmounts = new();

    // The winning gold mode (GoldVoteMode as int), for the results UI.
    public int GoldMode = (int)DemocracyModCode.GoldVoteMode.OriginalAmount;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter w)
    {
        w.WriteInt(EntryIds.Count);
        foreach (var id in EntryIds) w.WriteString(id);
        foreach (var wid in WinnerIds) w.WriteULong(wid);

        w.WriteInt(ReclaimPlayerIds.Count);
        foreach (var pid in ReclaimPlayerIds) w.WriteULong(pid);
        foreach (var amt in ReclaimAmounts) w.WriteInt(amt);

        w.WriteInt(GoldPlayerIds.Count);
        foreach (var pid in GoldPlayerIds) w.WriteULong(pid);
        foreach (var amt in GoldAmounts) w.WriteInt(amt);

        w.WriteInt(GoldMode);
    }

    public void Deserialize(PacketReader r)
    {
        var c = r.ReadInt();
        EntryIds = new(c);
        WinnerIds = new(c);
        for (var i = 0; i < c; i++) EntryIds.Add(r.ReadString());
        for (var i = 0; i < c; i++) WinnerIds.Add(r.ReadULong());

        c = r.ReadInt();
        ReclaimPlayerIds = new(c);
        ReclaimAmounts = new(c);
        for (var i = 0; i < c; i++) ReclaimPlayerIds.Add(r.ReadULong());
        for (var i = 0; i < c; i++) ReclaimAmounts.Add(r.ReadInt());

        c = r.ReadInt();
        GoldPlayerIds = new(c);
        GoldAmounts = new(c);
        for (var i = 0; i < c; i++) GoldPlayerIds.Add(r.ReadULong());
        for (var i = 0; i < c; i++) GoldAmounts.Add(r.ReadInt());

        GoldMode = r.ReadInt();
    }

    public void HandleMessage(ulong senderId) => MultiplayerCoordinator.HandleResolved(senderId, this);
}

/// <summary>
/// Live selection broadcast (cosmetic only). Sent every time a player toggles an
/// option on the current claim stage — or when a stage first renders, so their
/// default selection (e.g. the gold stage's "original amount") is visible too.
/// Each peer shows the sender's character icon on every option they have selected.
/// This is NOT the authoritative vote — that is still DemocracyStageMessage,
/// submitted when the player presses Next/Finish.
/// </summary>
public sealed class DemocracySelectionMessage : ICustomMessage
{
    public int Stage;
    public List<string> SelectedIds = new();

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter w)
    {
        w.WriteInt(Stage);
        w.WriteInt(SelectedIds.Count);
        foreach (var id in SelectedIds) w.WriteString(id);
    }

    public void Deserialize(PacketReader r)
    {
        Stage = r.ReadInt();
        var c = r.ReadInt();
        SelectedIds = new(c);
        for (var i = 0; i < c; i++) SelectedIds.Add(r.ReadString());
    }

    public void HandleMessage(ulong senderId) => MultiplayerCoordinator.HandleSelection(senderId, this);
}

/// <summary>
/// The HOST's authoritative gameplay config, broadcast to all clients at run launch so
/// every machine follows the host's settings instead of its own (per-machine config
/// would diverge the synchronized reward flow). Applied via HostConfig.ApplyRemote.
/// </summary>
public sealed class DemocracyConfigMessage : ICustomMessage
{
    public bool ShowGoldScreen;
    public bool ShowPotionsScreen;
    public bool ShowRelicsScreen;
    public bool ShowCardsScreen;
    public bool ShowResultsPanel;
    public bool EnableAncients;
    public float TieBreakFairness;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter w)
    {
        w.WriteBool(ShowGoldScreen);
        w.WriteBool(ShowPotionsScreen);
        w.WriteBool(ShowRelicsScreen);
        w.WriteBool(ShowCardsScreen);
        w.WriteBool(ShowResultsPanel);
        w.WriteBool(EnableAncients);
        w.WriteFloat(TieBreakFairness);
    }

    public void Deserialize(PacketReader r)
    {
        ShowGoldScreen = r.ReadBool();
        ShowPotionsScreen = r.ReadBool();
        ShowRelicsScreen = r.ReadBool();
        ShowCardsScreen = r.ReadBool();
        ShowResultsPanel = r.ReadBool();
        EnableAncients = r.ReadBool();
        TieBreakFairness = r.ReadFloat();
    }

    public void HandleMessage(ulong senderId) => MultiplayerCoordinator.HandleConfig(senderId, this);
}

