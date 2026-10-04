using System.Buffers.Binary;

namespace UdpTelemetry.Protocol;

public enum PacketType : byte
{
    // Типы из ПР1 оставлены для совместимости протокола.
    Movement = 1,
    Shoot = 2,
    Ack = 3,
    // Пакеты, используемые во второй практике для измерения RTT.
    Ping = 10,
    Pong = 11
}

public sealed record Packet(PacketType Type, ushort SequenceNumber, byte[] Payload, bool RequiresAck = false)
{
    // Сигнатура помогает отсеять случайные UDP-датаграммы не нашего протокола.
    private const ushort Magic = 0x4D50;
    public const ushort ProtocolVersion = 2;
    public const int HeaderSize = 10;
    public const int AckPayloadSize = sizeof(ushort);
    public const int PingPayloadSize = sizeof(ulong);
    public const int PongPayloadSize = sizeof(ulong) * 3;

    public static Packet Movement(ushort sequenceNumber, float x, float y, float z, bool requiresAck = false)
    {
        var payload = new byte[sizeof(float) * 3];
        WriteFloat(payload, 0, x);
        WriteFloat(payload, 4, y);
        WriteFloat(payload, 8, z);
        return new(PacketType.Movement, sequenceNumber, payload, requiresAck);
    }

    public static Packet Shoot(ushort sequenceNumber, byte weaponId, bool requiresAck = true) =>
        new(PacketType.Shoot, sequenceNumber, [weaponId], requiresAck);

    public static Packet Acknowledgement(ushort acknowledgedSequence) =>
        new(PacketType.Ack, acknowledgedSequence, U16Payload(acknowledgedSequence));

    public static Packet Ping(ushort sequenceNumber, ulong clientSendTimeUs)
    {
        // В PING передается только время отправки по монотонным часам клиента.
        var payload = new byte[PingPayloadSize];
        WriteU64(payload, 0, clientSendTimeUs);
        return new(PacketType.Ping, sequenceNumber, payload);
    }

    public static Packet Pong(
        ushort sequenceNumber,
        ulong clientSendTimeUs,
        ulong serverReceiveTimeUs,
        ulong serverSendTimeUs)
    {
        // PONG возвращает номер PING и добавляет две серверные временные метки.
        var payload = new byte[PongPayloadSize];
        WriteU64(payload, 0, clientSendTimeUs);
        WriteU64(payload, 8, serverReceiveTimeUs);
        WriteU64(payload, 16, serverSendTimeUs);
        return new(PacketType.Pong, sequenceNumber, payload);
    }

    public ulong ClientSendTimeUs => ReadU64(Payload, 0);

    public ulong ServerReceiveTimeUs => ReadU64(Payload, 8);

    public ulong ServerSendTimeUs => ReadU64(Payload, 16);

    public byte[] Serialize()
    {
        if (Payload.Length > ushort.MaxValue)
        {
            throw new InvalidDataException("Payload is larger than the protocol limit.");
        }

        // Заголовок и полезная нагрузка собираются вручную в сетевом порядке big-endian.
        var data = new byte[HeaderSize + Payload.Length];
        WriteU16(data, 0, Magic);
        data[2] = (byte)Type;
        WriteU16(data, 3, SequenceNumber);
        WriteU16(data, 5, (ushort)Payload.Length);
        WriteU16(data, 7, ProtocolVersion);
        data[9] = RequiresAck ? (byte)1 : (byte)0;
        Payload.CopyTo(data, HeaderSize);
        return data;
    }

    public static bool TryDeserialize(
        ReadOnlySpan<byte> data,
        out Packet? packet,
        out string error)
    {
        packet = null;
        error = string.Empty;

        // Проверки выполняются до чтения payload, чтобы поврежденный пакет не вызвал исключение.
        if (data.Length < HeaderSize)
        {
            error = $"Datagram is shorter than the {HeaderSize}-byte header.";
            return false;
        }

        if (ReadU16(data, 0) != Magic)
        {
            error = "Unknown packet signature.";
            return false;
        }

        var type = (PacketType)data[2];
        if (!Enum.IsDefined(type))
        {
            error = $"Unknown packet type: {data[2]}.";
            return false;
        }

        var payloadSize = ReadU16(data, 5);
        if (payloadSize != data.Length - HeaderSize)
        {
            error = "PayloadSize does not match datagram length.";
            return false;
        }

        if (ReadU16(data, 7) != ProtocolVersion)
        {
            error = $"Unsupported protocol version: {ReadU16(data, 7)}.";
            return false;
        }

        if (data[9] > 1)
        {
            error = "requiresAck must be 0 or 1.";
            return false;
        }

        var requiresAck = data[9] == 1;
        var validPayload = type switch
        {
            PacketType.Movement => payloadSize == sizeof(float) * 3,
            PacketType.Shoot => payloadSize == 1,
            PacketType.Ack => payloadSize == AckPayloadSize && !requiresAck,
            PacketType.Ping => payloadSize == PingPayloadSize && !requiresAck,
            PacketType.Pong => payloadSize == PongPayloadSize && !requiresAck,
            _ => false
        };
        if (!validPayload)
        {
            error = $"Invalid payload size {payloadSize} for {type}.";
            return false;
        }

        // После всех проверок можно безопасно выделить payload для нового пакета.
        packet = new Packet(type, ReadU16(data, 3), data[HeaderSize..].ToArray(), requiresAck);
        if (type == PacketType.Ack && ReadU16(packet.Payload, 0) != packet.SequenceNumber)
        {
            packet = null;
            error = "ACK payload does not match its sequence number.";
            return false;
        }

        return true;
    }

    private static byte[] U16Payload(ushort value)
    {
        var payload = new byte[AckPayloadSize];
        WriteU16(payload, 0, value);
        return payload;
    }

    private static void WriteFloat(Span<byte> destination, int offset, float value) =>
        WriteU32(destination, offset, BitConverter.SingleToUInt32Bits(value));

    private static void WriteU32(Span<byte> destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, sizeof(uint)), value);

    public static void WriteU16(Span<byte> destination, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(offset, sizeof(ushort)), value);

    public static void WriteU64(Span<byte> destination, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(offset, sizeof(ulong)), value);

    public static ushort ReadU16(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset, sizeof(ushort)));

    public static ulong ReadU64(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt64BigEndian(source.Slice(offset, sizeof(ulong)));
}
