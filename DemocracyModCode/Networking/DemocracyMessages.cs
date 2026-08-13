using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Logging;

namespace DemocracyMod.DemocracyModCode.Networking;

public sealed class DemocracyClaimMessage : ICustomMessage
{
    public int GoldAmount;
    public List<string> RewardIds = new();
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter w)
    {
        w.WriteInt(GoldAmount);
        w.WriteInt(RewardIds.Count);
        foreach (var id in RewardIds) w.WriteString(id);
    }
    public void Deserialize(PacketReader r)
    {
        GoldAmount = r.ReadInt();
        var c = r.ReadInt();
        RewardIds = new(c);
        for (var i = 0; i < c; i++) RewardIds.Add(r.ReadString());
    }
    public void HandleMessage(ulong senderId) => MultiplayerCoordinator.HandleClaim(senderId, this);
}

public sealed class DemocracyPoolDistributedMessage : ICustomMessage
{
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;
    public void Serialize(PacketWriter w) { }
    public void Deserialize(PacketReader r) { }
    public void HandleMessage(ulong senderId) { }
}
