using System.Buffers.Binary;

namespace UdpTelemetry.Protocol;

public enum PacketType : byte
{
    Movement = 1,
    Shoot = 2,
    Ping = 10,
    Pong = 11
}

public sealed record Packet(PacketType Type, ushort SequenceNumber, byte[] Payload)
{
    private const ushort Magic = 0x4D50;
    public const ushort ProtocolVersion = 1;
    public const int HeaderSize = 9;
    public const int PingPayloadSize = sizeof(ulong);
    public const int PongPayloadSize = sizeof(ulong) * 3;

    public static Packet Ping(ushort sequenceNumber, ulong clientSendTimeUs)
    {
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

        var data = new byte[HeaderSize + Payload.Length];
        WriteU16(data, 0, Magic);
        data[2] = (byte)Type;
        WriteU16(data, 3, SequenceNumber);
        WriteU16(data, 5, (ushort)Payload.Length);
        WriteU16(data, 7, ProtocolVersion);
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

        if (data.Length < HeaderSize)
        {
            error = "Datagram is shorter than the 9-byte header.";
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

        if (type == PacketType.Ping && payloadSize != PingPayloadSize ||
            type == PacketType.Pong && payloadSize != PongPayloadSize)
        {
            error = $"Invalid payload size {payloadSize} for {type}.";
            return false;
        }

        packet = new Packet(type, ReadU16(data, 3), data[HeaderSize..].ToArray());
        return true;
    }

    public static void WriteU16(Span<byte> destination, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(offset, sizeof(ushort)), value);

    public static void WriteU64(Span<byte> destination, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(offset, sizeof(ulong)), value);

    public static ushort ReadU16(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset, sizeof(ushort)));

    public static ulong ReadU64(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt64BigEndian(source.Slice(offset, sizeof(ulong)));
}