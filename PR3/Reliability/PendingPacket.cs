namespace UdpTelemetry.Reliability;

public sealed record PendingPacket(
    ushort Sequence,
    byte[] RawBytes,
    long FirstSentAtUs,
    long LastSentAtUs,
    uint Attempts);
