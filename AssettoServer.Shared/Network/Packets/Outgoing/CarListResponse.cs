using AssettoServer.Shared.Model;

namespace AssettoServer.Shared.Network.Packets.Outgoing;

public class CarListResponse : IOutgoingNetworkPacket
{
    public int PageIndex;
    public int EntryCarsCount;
    public required IEnumerable<IEntryCar<IClient>> EntryCars;
    public required Dictionary<byte, EntryCarResult> CarResults;

    /// <summary>
    /// Quem vai RECEBER esta lista. O carro dele nunca sai marcado como vaga de
    /// transmissao: o cliente descarta o carro marcado mesmo quando o carro e ele
    /// proprio, e a partir dai nenhum pacote endereçado a ele encontra destino.
    /// </summary>
    public byte ViewerSessionId = 255;

    public void ToWriter(ref PacketWriter writer)
    {
        writer.Write((byte)ACServerProtocol.CarList);
        writer.Write((byte)PageIndex);
        writer.Write((byte)EntryCarsCount);
        foreach (var car in EntryCars)
        {
            writer.Write(car.SessionId);
            writer.WriteUTF8String(car.Model);
            writer.WriteUTF8String(car.Skin);
            writer.WriteUTF8String(CarResults[car.SessionId].Name);
            writer.WriteUTF8String(CarResults[car.SessionId].Team);
            writer.WriteUTF8String(CarResults[car.SessionId].NationCode);
            writer.Write(car.IsSpectator && car.SessionId != ViewerSessionId);
            writer.Write(car.Status.DamageZoneLevel);
        }
    }
}
