using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Logging;

namespace DemocracyMod.DemocracyModCode.Networking;

public sealed class DemocracyPoolUpdateMessage : ICustomMessage
{
    public int EntryCount;
    public int TotalGoldPooled;
    public List<(string Id, string Type, ulong SourceId, string DisplayName, int GoldAmount)> Entries = new();
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter w)
    {
        w.WriteInt(EntryCount); w.WriteInt(TotalGoldPooled); w.WriteInt(Entries.Count);
        foreach (var (id, type, sid, dn, ga) in Entries) { w.WriteString(id); w.WriteString(type); w.WriteULong(sid); w.WriteString(dn); w.WriteInt(ga); }
    }
    public void Deserialize(PacketReader r)
    {
        EntryCount = r.ReadInt(); TotalGoldPooled = r.ReadInt(); var c = r.ReadInt();
        Entries = new(c); for (var i = 0; i < c; i++) Entries.Add((r.ReadString(), r.ReadString(), r.ReadULong(), r.ReadString(), r.ReadInt()));
    }
    public void HandleMessage(ulong senderId) => MultiplayerCoordinator.HandlePoolUpdate(this);
}

public sealed class DemocracyVoteStartMessage : ICustomMessage
{
    public string RewardId = ""; public string RewardName = ""; public int TimeoutSeconds;
    public bool ShouldBroadcast => true; public NetTransferMode Mode => NetTransferMode.Reliable; public LogLevel LogLevel => LogLevel.Debug;
    public void Serialize(PacketWriter w) { w.WriteString(RewardId); w.WriteString(RewardName); w.WriteInt(TimeoutSeconds); }
    public void Deserialize(PacketReader r) { RewardId = r.ReadString(); RewardName = r.ReadString(); TimeoutSeconds = r.ReadInt(); }
    public void HandleMessage(ulong senderId) => MultiplayerCoordinator.HandleVoteStart(this);
}

public sealed class DemocracyVoteCastMessage : ICustomMessage
{
    public string RewardId = ""; public ulong TargetPlayerId;
    public bool ShouldBroadcast => true; public NetTransferMode Mode => NetTransferMode.Reliable; public LogLevel LogLevel => LogLevel.Debug;
    public void Serialize(PacketWriter w) { w.WriteString(RewardId); w.WriteULong(TargetPlayerId); }
    public void Deserialize(PacketReader r) { RewardId = r.ReadString(); TargetPlayerId = r.ReadULong(); }
    public void HandleMessage(ulong senderId) => VoteManager.CastVote(senderId, RewardId, TargetPlayerId);
}

public sealed class DemocracyVoteResultMessage : ICustomMessage
{
    public string RewardId = ""; public ulong WinnerId; public int VoteCount;
    public List<(ulong VoterId, ulong TargetId)> Votes = new();
    public bool ShouldBroadcast => true; public NetTransferMode Mode => NetTransferMode.Reliable; public LogLevel LogLevel => LogLevel.Info;
    public void Serialize(PacketWriter w)
    {
        w.WriteString(RewardId); w.WriteULong(WinnerId); w.WriteInt(VoteCount); w.WriteInt(Votes.Count);
        foreach (var (v, t) in Votes) { w.WriteULong(v); w.WriteULong(t); }
    }
    public void Deserialize(PacketReader r)
    {
        RewardId = r.ReadString(); WinnerId = r.ReadULong(); VoteCount = r.ReadInt(); var c = r.ReadInt();
        Votes = new(c); for (var i = 0; i < c; i++) Votes.Add((r.ReadULong(), r.ReadULong()));
    }
    public void HandleMessage(ulong senderId) => MultiplayerCoordinator.HandleVoteResult(this);
}

public sealed class DemocracyInterestMessage : ICustomMessage
{
    public string RewardId = "";
    public bool ShouldBroadcast => true; public NetTransferMode Mode => NetTransferMode.Reliable; public LogLevel LogLevel => LogLevel.Debug;
    public void Serialize(PacketWriter w) => w.WriteString(RewardId);
    public void Deserialize(PacketReader r) => RewardId = r.ReadString();
    public void HandleMessage(ulong senderId) => VoteManager.ExpressInterest(senderId, RewardId);
}

public sealed class DemocracyPoolDistributedMessage : ICustomMessage
{
    public bool ShouldBroadcast => true; public NetTransferMode Mode => NetTransferMode.Reliable; public LogLevel LogLevel => LogLevel.Info;
    public void Serialize(PacketWriter w) { }
    public void Deserialize(PacketReader r) { }
    public void HandleMessage(ulong senderId) { }
}
