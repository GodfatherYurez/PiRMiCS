using UdpTelemetry.Protocol;

namespace UdpTelemetry.Telemetry;

public enum ResponseStatus
{
    Received,
    Timeout,
    LateResponse,
    DuplicateResponse,
    UnknownResponse
}

public sealed record Measurement(
    string ExperimentId,
    int Sample,
    ushort Sequence,
    double SentAtMs,
    double? RttMs,
    double? SrttMs,
    ResponseStatus Status);

public sealed class TelemetryTracker
{
    private const long TimeoutUs = 1_000_000;
    private readonly int capacity;
    private readonly Dictionary<ushort, InFlight> inFlight = [];
    private readonly Queue<ushort> insertionOrder = [];
    private readonly List<double> receivedRtts = [];
    private double? srttMs;
    private double? previousRttMs;
    private double jitterSumMs;

    public TelemetryTracker(int capacity = 4096)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.capacity = capacity;
    }

    public double? SrttMs => srttMs;

    public double MeanJitterMs => receivedRtts.Count > 1
        ? jitterSumMs / (receivedRtts.Count - 1)
        : 0;

    public int ReceivedCount => receivedRtts.Count;

    public bool TrackPing(ushort sequence, long sentAtUs, string experimentId, int sample)
    {
        if (inFlight.ContainsKey(sequence))
        {
            return false;
        }

        while (inFlight.Count >= capacity)
        {
            inFlight.Remove(insertionOrder.Dequeue());
        }

        inFlight[sequence] = new InFlight(sequence, sentAtUs, experimentId, sample);
        insertionOrder.Enqueue(sequence);
        return true;
    }

    public ResponseStatus OnPong(ushort sequence, long receivedAtUs, out Measurement? measurement)
    {
        measurement = null;
        if (!inFlight.TryGetValue(sequence, out var flight))
        {
            return ResponseStatus.UnknownResponse;
        }

        if (flight.Status is ResponseStatus.Received or ResponseStatus.LateResponse)
        {
            return ResponseStatus.DuplicateResponse;
        }

        if (flight.Status == ResponseStatus.Timeout)
        {
            flight.Status = ResponseStatus.LateResponse;
            measurement = CreateMeasurement(flight, null);
            return ResponseStatus.LateResponse;
        }

        var rttMs = (receivedAtUs - flight.SentAtUs) / 1000.0;
        if (receivedAtUs - flight.SentAtUs > TimeoutUs)
        {
            flight.Status = ResponseStatus.LateResponse;
            measurement = CreateMeasurement(flight, rttMs);
            return ResponseStatus.LateResponse;
        }

        flight.Status = ResponseStatus.Received;
        previousRttMs = previousRttMs is null ? rttMs :
            UpdateJitter(previousRttMs.Value, rttMs);
        srttMs = srttMs is null ? rttMs : 0.875 * srttMs + 0.125 * rttMs;
        flight.SrttMs = srttMs;
        receivedRtts.Add(rttMs);
        measurement = CreateMeasurement(flight, rttMs);
        return ResponseStatus.Received;
    }

    public IReadOnlyList<Measurement> Expire(long nowUs)
    {
        var expired = new List<Measurement>();
        foreach (var flight in inFlight.Values)
        {
            if (flight.Status == null && nowUs - flight.SentAtUs > TimeoutUs)
            {
                flight.Status = ResponseStatus.Timeout;
                expired.Add(CreateMeasurement(flight, null));
            }
        }

        return expired;
    }

    private double UpdateJitter(double previous, double current)
    {
        jitterSumMs += Math.Abs(current - previous);
        return current;
    }

    private static Measurement CreateMeasurement(InFlight flight, double? rttMs) =>
        new(flight.ExperimentId, flight.Sample, flight.Sequence, flight.SentAtUs / 1000.0,
            rttMs, rttMs is null ? null : flight.SrttMs, flight.Status!.Value);

    private sealed class InFlight(ushort sequence, long sentAtUs, string experimentId, int sample)
    {
        public readonly ushort Sequence = sequence;
        public readonly long SentAtUs = sentAtUs;
        public readonly string ExperimentId = experimentId;
        public readonly int Sample = sample;
        public ResponseStatus? Status;
        public double? SrttMs;
    }
}