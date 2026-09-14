using System.Buffers.Binary;
using System.Text;

namespace UdpBasics.Protocol;

public enum PacketType : byte
{
    Movement = 1,
    Shoot = 2,
    State = 100,
    Error = 255
}

public readonly record struct Vector3(float X, float Y, float Z);
public readonly record struct PlayerState(float X, float Y, float Z, int Shots);

public sealed record Packet(PacketType Type, ushort SequenceNumber, byte[] Payload)
{
    private const ushort Magic = 0x4D50;
    private const byte Version = 1;
    public const int HeaderSize = 8;

    public static Packet Movement(ushort sequenceNumber, Vector3 delta)
    {
        var payload = new byte[12];
        WriteSingle(payload.AsSpan(0, 4), delta.X);
        WriteSingle(payload.AsSpan(4, 4), delta.Y);
        WriteSingle(payload.AsSpan(8, 4), delta.Z);
        return new Packet(PacketType.Movement, sequenceNumber, payload);
    }

    public static Packet Shoot(ushort sequenceNumber, byte weaponId) =>
        new(PacketType.Shoot, sequenceNumber, [weaponId]);

    public static Packet State(ushort sequenceNumber, PlayerState state)
    {
        var payload = new byte[16];
        WriteSingle(payload.AsSpan(0, 4), state.X);
        WriteSingle(payload.AsSpan(4, 4), state.Y);
        WriteSingle(payload.AsSpan(8, 4), state.Z);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(12, 4), state.Shots);
        return new Packet(PacketType.State, sequenceNumber, payload);
    }

    public static Packet Error(ushort sequenceNumber, string message) =>
        new(PacketType.Error, sequenceNumber, Encoding.UTF8.GetBytes(message));

    public Vector3 ReadMovement()
    {
        RequirePayloadLength(12);
        return new Vector3(
            ReadSingle(Payload.AsSpan(0, 4)),
            ReadSingle(Payload.AsSpan(4, 4)),
            ReadSingle(Payload.AsSpan(8, 4)));
    }

    public byte ReadWeaponId()
    {
        RequirePayloadLength(1);
        return Payload[0];
    }

    public PlayerState ReadState()
    {
        RequirePayloadLength(16);
        return new PlayerState(
            ReadSingle(Payload.AsSpan(0, 4)),
            ReadSingle(Payload.AsSpan(4, 4)),
            ReadSingle(Payload.AsSpan(8, 4)),
            BinaryPrimitives.ReadInt32BigEndian(Payload.AsSpan(12, 4)));
    }

    public string ReadError() => Encoding.UTF8.GetString(Payload);

    public byte[] Serialize()
    {
        if (Payload.Length > ushort.MaxValue)
        {
            throw new InvalidDataException("UDP payload is larger than the protocol limit.");
        }

        var data = new byte[HeaderSize + Payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0, 2), Magic);
        data[2] = Version;
        data[3] = (byte)Type;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4, 2), SequenceNumber);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6, 2), (ushort)Payload.Length);
        Payload.CopyTo(data, HeaderSize);
        return data;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out Packet? packet, out string error)
    {
        packet = null;
        error = string.Empty;

        if (data.Length < HeaderSize)
        {
            error = "Packet is shorter than the 8-byte header.";
            return false;
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(data[..2]) != Magic)
        {
            error = "Unknown packet signature.";
            return false;
        }

        if (data[2] != Version)
        {
            error = $"Unsupported protocol version: {data[2]}.";
            return false;
        }

        var payloadSize = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6, 2));
        if (payloadSize != data.Length - HeaderSize)
        {
            error = "PayloadSize does not match the datagram length.";
            return false;
        }

        packet = new Packet(
            (PacketType)data[3],
            BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2)),
            data[HeaderSize..].ToArray());
        return true;
    }

    private void RequirePayloadLength(int expected)
    {
        if (Payload.Length != expected)
        {
            throw new InvalidDataException(
                $"{Type} payload must contain {expected} bytes, received {Payload.Length}.");
        }
    }

    private static void WriteSingle(Span<byte> destination, float value) =>
        BinaryPrimitives.WriteInt32BigEndian(destination, BitConverter.SingleToInt32Bits(value));

    private static float ReadSingle(ReadOnlySpan<byte> source) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(source));
}
