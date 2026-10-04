using AssettoServer.Shared.Network.Packets.Outgoing;

namespace AssettoServer.Shared.Network.Packets.Incoming;

public struct HandshakeRequest : IIncomingNetworkPacket, IOutgoingNetworkPacket
{
    public ushort ClientVersion;
    public ulong Guid;
    public string Name;
    public string Team;
    public string Nation;
    public string RequestedCar;
    public string Password;
    public string? Features;
    public byte[]? SessionTicket;

    /// <summary>
    /// O tamanho que o cliente DECLAROU para o ticket, e o que de fato sobrou no
    /// pacote. Diagnóstico.
    /// </summary>
    /// <remarks>
    /// O ticket só é aceito quando os dois batem EXATAMENTE. Divergindo, ele é
    /// descartado em silêncio e o sintoma vira "Missing session ticket" -- que
    /// parece problema de Steam e não de formato. Estes dois números separam uma
    /// coisa da outra: -1 nos dois significa que o cliente não mandou nada.
    /// </remarks>
    public int TicketDeclarado;
    public int TicketDisponivel;

    public void FromReader(PacketReader reader)
    {
        ClientVersion = reader.Read<ushort>();
        // -1 = o cliente nem chegou a declarar tamanho. Zero seria ambiguo com
        // "declarou zero", e e justamente essa diferenca que se quer medir.
        TicketDeclarado = -1;
        TicketDisponivel = -1;

        if (ulong.TryParse(reader.ReadUTF8String(), out var guid))
            Guid = guid;
        Name = reader.ReadUTF32String();
        Team = reader.ReadUTF8String();
        Nation = reader.ReadUTF8String();
        RequestedCar = reader.ReadUTF8String();
        Password = reader.ReadUTF8String();

        if (reader.Buffer.Length > reader.ReadPosition + 2)
        {
            Features = reader.ReadUTF8String(true);

            if (reader.Buffer.Length > reader.ReadPosition + 2)
            {
                short ticketLength = reader.Read<short>();
                TicketDeclarado = ticketLength;
                TicketDisponivel = reader.Buffer.Length - reader.ReadPosition;

                if (ticketLength == reader.Buffer.Length - reader.ReadPosition)
                {
                    SessionTicket = new byte[ticketLength];
                    reader.ReadBytes(SessionTicket);
                }
            }
        }
    }

    public void ToWriter(ref PacketWriter writer)
    {
        writer.Write(ACServerProtocol.RequestNewConnection);
        writer.Write<ushort>(ClientVersion);
        writer.WriteUTF8String(Guid.ToString());
        writer.WriteUTF32String(Name);
        writer.WriteUTF8String(Team);
        writer.WriteUTF8String(Nation);
        writer.WriteUTF8String(RequestedCar);
        writer.WriteUTF8String(Password);
        if (Features == null) return;
        writer.WriteUTF8String(Features, true);
        if (SessionTicket == null) return;
        writer.Write((short)SessionTicket.Length);
        writer.WriteBytes(SessionTicket);
    }
}
