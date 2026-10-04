namespace UdpTelemetry.Reliability;

/// <summary>Jacobson/Karels RTO estimator. Samples may come from PING/PONG or first-attempt ACKs.</summary>
public sealed class AdaptiveTimeout
{
    private const double Alpha = 0.125;
    private const double Beta = 0.25;
    private double? srttMs;
    private double? rttVarMs;

    public AdaptiveTimeout(double minimumRtoMs = 100, double maximumRtoMs = 3000)
    {
        if (!double.IsFinite(minimumRtoMs) || !double.IsFinite(maximumRtoMs) ||
            minimumRtoMs <= 0 || maximumRtoMs < minimumRtoMs)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRtoMs));
        }

        this.minimumRtoMs = minimumRtoMs;
        this.maximumRtoMs = maximumRtoMs;
    }

    private readonly double minimumRtoMs;
    private readonly double maximumRtoMs;
    public double? SrttMs => srttMs;
    public double? RttVarMs => rttVarMs;
    public double RtoMs => Math.Clamp((srttMs ?? minimumRtoMs) + 4 * (rttVarMs ?? 0),
        minimumRtoMs, maximumRtoMs);
    public long RtoUs => (long)Math.Round(RtoMs * 1000);

    public void OnSample(double rttSampleMs)
    {
        if (!double.IsFinite(rttSampleMs) || rttSampleMs <= 0)
        {
            return;
        }

        if (srttMs is null)
        {
            srttMs = rttSampleMs;
            rttVarMs = rttSampleMs / 2;
            return;
        }

        var previousSrttMs = srttMs.Value;
        rttVarMs = (1 - Beta) * rttVarMs!.Value + Beta * Math.Abs(previousSrttMs - rttSampleMs);
        srttMs = (1 - Alpha) * previousSrttMs + Alpha * rttSampleMs;
    }
}
