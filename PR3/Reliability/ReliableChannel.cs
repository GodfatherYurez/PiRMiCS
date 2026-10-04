namespace UdpTelemetry.Reliability;

/// <summary>Tracks reliable datagrams without performing any socket I/O.</summary>
public sealed class ReliableChannel(uint maxAttempts = 5)
{
    private readonly Dictionary<ushort, PendingPacket> pending = [];
    private readonly List<ushort> failed = [];

    public uint MaxAttempts { get; } = maxAttempts > 0
        ? maxAttempts
        : throw new ArgumentOutOfRangeException(nameof(maxAttempts));

    public int PendingCount => pending.Count;
    public int FailedCount => failed.Count;
    public IReadOnlyList<ushort> FailedSequences => failed;
    public IReadOnlyCollection<PendingPacket> PendingPackets => pending.Values;

    public void OnSent(ushort sequence, byte[] rawBytes, long nowUs)
    {
        ArgumentNullException.ThrowIfNull(rawBytes);
        if (pending.ContainsKey(sequence))
        {
            throw new InvalidOperationException($"Sequence {sequence} is already pending.");
        }

        pending.Add(sequence, new PendingPacket(sequence, rawBytes.ToArray(), nowUs, nowUs, 1));
    }

    /// <summary>Returns false for a duplicate, unknown, or stale ACK.</summary>
    public bool OnAckReceived(ushort sequence) => OnAckReceived(sequence, 0, out _);

    /// <summary>Provides an RTT sample only when no retransmission makes its timing ambiguous.</summary>
    public bool OnAckReceived(ushort sequence, long nowUs, out double? rttSampleMs)
    {
        rttSampleMs = null;
        if (!pending.Remove(sequence, out var acknowledged))
        {
            return false;
        }

        if (acknowledged.Attempts == 1 && nowUs >= acknowledged.FirstSentAtUs)
        {
            rttSampleMs = (nowUs - acknowledged.FirstSentAtUs) / 1000.0;
        }

        return true;
    }

    /// <summary>
    /// Removes and returns due packets for retransmission. maxAttempts includes the first send;
    /// a packet is failed when its final allowed attempt also expires.
    /// </summary>
    public IReadOnlyList<PendingPacket> CollectForRetransmission(long nowUs, long adaptiveTimeoutUs)
    {
        if (adaptiveTimeoutUs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(adaptiveTimeoutUs));
        }

        var retransmit = new List<PendingPacket>();
        foreach (var item in pending.Values.ToArray())
        {
            if (nowUs < item.LastSentAtUs || nowUs - item.LastSentAtUs < adaptiveTimeoutUs)
            {
                continue;
            }

            if (item.Attempts >= MaxAttempts)
            {
                pending.Remove(item.Sequence);
                failed.Add(item.Sequence);
                continue;
            }

            var updated = item with { Attempts = item.Attempts + 1, LastSentAtUs = nowUs };
            pending[item.Sequence] = updated;
            retransmit.Add(updated);
        }

        return retransmit;
    }
}
