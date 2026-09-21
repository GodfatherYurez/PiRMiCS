using UdpTelemetry.Protocol;

namespace UdpTelemetry.Telemetry;

public enum ResponseStatus
{
    // Эти статусы попадают в CSV и позволяют отличать потерю от необычного ответа.
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
    // Ответ позже одной секунды считается тайм-аутом.
    private const long TimeoutUs = 1_000_000;
    private readonly int capacity;
    // inFlight хранит PING, для которых клиент еще ожидает PONG.
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
        // Джиттер - среднее изменение RTT между соседними успешными ответами.
        ? jitterSumMs / (receivedRtts.Count - 1)
        : 0;

    public int ReceivedCount => receivedRtts.Count;

    public bool TrackPing(ushort sequence, long sentAtUs, string experimentId, int sample)
    {
        // Номер нельзя переиспользовать, пока старый запрос еще находится в inFlight.
        if (inFlight.ContainsKey(sequence))
        {
            return false;
        }

        // Ограничение защищает измеритель от неограниченного роста памяти.
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
        // Сервер мог прислать старый или чужой номер, которого клиент не ожидает.
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

        // RTT измеряется только часами клиента, поэтому часы сервера синхронизировать не нужно.
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
        // Экспоненциальное сглаживание уменьшает влияние одиночного скачка задержки.
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
            // Запрос помечается timeout, но сохраняется: поздний PONG нужно распознать отдельно.
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
        // Накапливаем модуль разницы соседних RTT, среднее считается в свойстве MeanJitterMs.
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
