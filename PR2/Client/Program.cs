using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using UdpTelemetry.Protocol;
using UdpTelemetry.Telemetry;
using UdpTelemetry.Transport;

var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = GetInt(args, 1, 7777);
var experimentId = args.Length > 2 ? args[2] : "baseline";
var count = GetInt(args, 3, 50);
var intervalMs = GetInt(args, 4, 200);
var outputPath = args.Length > 5 ? args[5] : Path.Combine("docs", "latency_samples.csv");
var addresses = await Dns.GetHostAddressesAsync(host);
var server = new IPEndPoint(
    addresses.First(address => address.AddressFamily == AddressFamily.InterNetwork), port);
var tracker = new TelemetryTracker();
var measurements = new List<Measurement>();
using var transport = new UdpTransport();
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

var sequence = (ushort)1;
var startUs = NowUs();
for (var sample = 1; sample <= count && !shutdown.IsCancellationRequested; sample++)
{
    var sentUs = NowUs() - startUs;
    while (!tracker.TrackPing(sequence, sentUs, experimentId, sample))
    {
        sequence = unchecked((ushort)(sequence + 1));
    }

    await transport.SendAsync(Packet.Ping(sequence, (ulong)sentUs).Serialize(), server, shutdown.Token);
    Console.WriteLine($"> PING sample={sample} seq={sequence}");

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
    timeout.CancelAfter(TimeSpan.FromSeconds(1));
    try
    {
        while (true)
        {
            var received = await transport.ReceiveAsync(timeout.Token);
            if (!Packet.TryDeserialize(received.Buffer, out var packet, out var error))
            {
                Console.WriteLine($"< INVALID: {error}");
                continue;
            }

            if (packet!.Type != PacketType.Pong)
            {
                continue;
            }

            var status = tracker.OnPong(packet.SequenceNumber, NowUs() - startUs, out var measurement);
            if (measurement is not null)
            {
                measurements.Add(measurement);
                Print(measurement);
            }

            if (status is ResponseStatus.Received or ResponseStatus.LateResponse or
                ResponseStatus.DuplicateResponse)
            {
                break;
            }
        }
    }
    catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
    {
        measurements.AddRange(tracker.Expire(NowUs() - startUs));
        Console.WriteLine($"! TIMEOUT seq={sequence}");
    }

    sequence = unchecked((ushort)(sequence + 1));
    await Task.Delay(intervalMs, shutdown.Token);
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
await File.WriteAllLinesAsync(outputPath, [
    "experiment_id;sample;sequence;sent_at_ms;rtt_ms;srtt_ms;status",
    .. measurements.OrderBy(item => item.Sample).Select(ToCsv)]);
Console.WriteLine($"Saved {measurements.Count} measurements to {outputPath}");
Console.WriteLine($"received={measurements.Count(item => item.Status == ResponseStatus.Received)} " +
                  $"timeout={measurements.Count(item => item.Status == ResponseStatus.Timeout)} " +
                  $"srtt={tracker.SrttMs?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}ms " +
                  $"jitter={tracker.MeanJitterMs:F3}ms");

static string ToCsv(Measurement item) => string.Join(";", [
    item.ExperimentId,
    item.Sample.ToString(CultureInfo.InvariantCulture),
    item.Sequence.ToString(CultureInfo.InvariantCulture),
    item.SentAtMs.ToString("F3", CultureInfo.InvariantCulture),
    item.RttMs?.ToString("F3", CultureInfo.InvariantCulture) ?? "",
    item.SrttMs?.ToString("F3", CultureInfo.InvariantCulture) ?? "",
    item.Status.ToString().ToLowerInvariant()]);

static void Print(Measurement item) =>
    Console.WriteLine($"< {item.Status} seq={item.Sequence} rtt={item.RttMs?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}ms");

static long NowUs() => Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;

static int GetInt(string[] args, int index, int fallback) =>
    args.Length > index && int.TryParse(args[index], out var value) ? value : fallback;