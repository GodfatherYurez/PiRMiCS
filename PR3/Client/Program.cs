using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using UdpTelemetry.Protocol;
using UdpTelemetry.Reliability;
using UdpTelemetry.Telemetry;

var host = args.ElementAtOrDefault(0) ?? "127.0.0.1";
var port = GetInt(args, 1, 7777);
var experimentId = args.ElementAtOrDefault(2) ?? "baseline";
var count = GetInt(args, 3, 50);
var intervalMs = GetInt(args, 4, 200);
var maxAttempts = GetInt(args, 5, 5);
var csvPath = args.ElementAtOrDefault(6) ?? Path.Combine("PR3", "docs", "reliability_samples.csv");

if (port is < 1 or > 65535 || count is < 1 or > 32767 || intervalMs < 1 || maxAttempts < 1)
{
    Console.Error.WriteLine("Usage: Client [host] [port] [experiment_id] [count:1..32767] " +
                            "[interval_ms] [maxAttempts] [csv_path]");
    return 2;
}

var address = (await Dns.GetHostAddressesAsync(host))
    .FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork);
if (address is null)
{
    Console.Error.WriteLine($"No IPv4 address found for '{host}'.");
    return 2;
}

var server = new IPEndPoint(address, port);
var channel = new ReliableChannel((uint)maxAttempts);
var adaptiveTimeout = new AdaptiveTimeout();
var telemetry = new TelemetryTracker();
var measurements = new List<string>();
using var udp = new UdpClient(AddressFamily.InterNetwork);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine($"Reliable SHOOT experiment '{experimentId}' -> {server}, count={count}, " +
                  $"initial RTO={adaptiveTimeout.RtoMs:F0}ms, maxAttempts={maxAttempts}");
var firstAttemptDelivered = 0;
var failed = 0;
var totalAttempts = 0;
double totalAckMs = 0;
ushort nextSequence = 1;
var runStartUs = NowUs();

for (var sample = 1; sample <= count && !shutdown.IsCancellationRequested; sample++)
{
    // Feed the adaptive timeout from PR2's PING/PONG telemetry before each reliable command.
    var pingSequence = nextSequence++;
    var pingSentUs = NowUs();
    telemetry.TrackPing(pingSequence, pingSentUs, experimentId, sample);
    await udp.SendAsync(Packet.Ping(pingSequence, (ulong)pingSentUs).Serialize(), server, shutdown.Token);
    await ReceivePongAsync(pingSequence, telemetry, adaptiveTimeout, udp, server, shutdown.Token);

    var sequence = nextSequence++;
    var data = Packet.Shoot(sequence, weaponId: 2, requiresAck: true).Serialize();
    var firstSentUs = NowUs();
    channel.OnSent(sequence, data, firstSentUs);
    var attempts = 1;
    var acknowledged = false;
    double? timeToAckMs = null;

    await udp.SendAsync(data, server, shutdown.Token);
    Console.WriteLine($"> SHOOT seq={sequence} attempt=1 rto={adaptiveTimeout.RtoMs:F0}ms");

    while (!acknowledged && channel.PendingCount > 0 && !shutdown.IsCancellationRequested)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(adaptiveTimeout.RtoMs));
        try
        {
            while (true)
            {
                var received = await udp.ReceiveAsync(timeout.Token);
                if (!received.RemoteEndPoint.Equals(server))
                {
                    Console.WriteLine($"IGNORED from={received.RemoteEndPoint}: unexpected endpoint");
                    continue;
                }

                if (!Packet.TryDeserialize(received.Buffer, out var packet, out var error))
                {
                    Console.WriteLine($"IGNORED from={received.RemoteEndPoint}: {error}");
                    continue;
                }

                if (packet!.Type == PacketType.Pong)
                {
                    UpdatePingTelemetry(packet, telemetry, adaptiveTimeout, NowUs());
                    continue;
                }

                if (packet.Type != PacketType.Ack || ReadAcknowledgedSequence(packet) != sequence)
                {
                    Console.WriteLine($"IGNORED type={packet.Type} seq={packet.SequenceNumber}");
                    continue;
                }

                acknowledged = channel.OnAckReceived(sequence, NowUs(), out var rttSampleMs);
                if (!acknowledged)
                {
                    Console.WriteLine($"DUPLICATE_ACK seq={sequence}");
                    continue;
                }

                if (rttSampleMs is { } rtt)
                {
                    adaptiveTimeout.OnSample(rtt);
                }
                timeToAckMs = (NowUs() - firstSentUs) / 1000.0;
                totalAckMs += timeToAckMs.Value;
                totalAttempts += attempts;
                if (attempts == 1)
                {
                    firstAttemptDelivered++;
                }

                Console.WriteLine($"< ACK seq={sequence} attempts={attempts} time_to_ack={timeToAckMs:F2}ms " +
                                  $"rto={adaptiveTimeout.RtoMs:F2}ms");
                break;
            }
        }
        catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
        {
            var retransmissions = channel.CollectForRetransmission(NowUs(), adaptiveTimeout.RtoUs);
            if (retransmissions.Count == 0)
            {
                if (channel.FailedSequences.Contains(sequence))
                {
                    failed++;
                    totalAttempts += attempts;
                    Console.WriteLine($"FAILED seq={sequence} attempts={attempts}: ACK not received");
                    break;
                }

                continue;
            }

            foreach (var pending in retransmissions)
            {
                attempts = checked((int)pending.Attempts);
                await udp.SendAsync(pending.RawBytes, server, shutdown.Token);
                Console.WriteLine($"> RETRANSMIT seq={sequence} attempt={attempts} " +
                                  $"rto={adaptiveTimeout.RtoMs:F0}ms");
            }
        }
    }

    measurements.Add(string.Join(";", [
        experimentId,
        sample.ToString(CultureInfo.InvariantCulture),
        sequence.ToString(CultureInfo.InvariantCulture),
        attempts.ToString(CultureInfo.InvariantCulture),
        (acknowledged && attempts == 1 ? 1 : 0).ToString(CultureInfo.InvariantCulture),
        (acknowledged ? 0 : 1).ToString(CultureInfo.InvariantCulture),
        timeToAckMs?.ToString("F3", CultureInfo.InvariantCulture) ?? "",
        adaptiveTimeout.RtoMs.ToString("F3", CultureInfo.InvariantCulture),
        acknowledged ? "acknowledged" : "failed"]));

    await Task.Delay(intervalMs, shutdown.Token);
}

var absoluteCsvPath = Path.GetFullPath(csvPath);
Directory.CreateDirectory(Path.GetDirectoryName(absoluteCsvPath)!);
var writeHeader = !File.Exists(absoluteCsvPath) || new FileInfo(absoluteCsvPath).Length == 0;
await using (var writer = new StreamWriter(absoluteCsvPath, append: true))
{
    if (writeHeader)
    {
        await writer.WriteLineAsync("experiment_id;sample;sequence;attempts;delivered_first_try;failed;" +
                                    "time_to_ack_ms;final_rto_ms;status");
    }

    foreach (var row in measurements)
    {
        await writer.WriteLineAsync(row);
    }
}

var elapsedMs = (NowUs() - runStartUs) / 1000.0;
Console.WriteLine($"Saved {measurements.Count} rows to {absoluteCsvPath}");
Console.WriteLine($"Summary: sent={measurements.Count} acknowledged={measurements.Count - failed} failed={failed} " +
                  $"first_try={firstAttemptDelivered} avg_attempts=" +
                  $"{(measurements.Count == 0 ? 0 : totalAttempts / (double)measurements.Count):F3} " +
                  $"avg_time_to_ack_ms={(measurements.Count == failed ? 0 : totalAckMs / (measurements.Count - failed)):F3} " +
                  $"final_rto_ms={adaptiveTimeout.RtoMs:F3} elapsed_ms={elapsedMs:F0}");
return failed == 0 ? 0 : 1;

static async Task ReceivePongAsync(ushort sequence, TelemetryTracker telemetry,
    AdaptiveTimeout adaptiveTimeout, UdpClient udp, IPEndPoint server, CancellationToken cancellationToken)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromMilliseconds(1000));
    try
    {
        while (true)
        {
            var received = await udp.ReceiveAsync(timeout.Token);
            if (!received.RemoteEndPoint.Equals(server))
            {
                Console.WriteLine($"IGNORED_PING_REPLY from={received.RemoteEndPoint}: unexpected endpoint");
                continue;
            }

            if (!Packet.TryDeserialize(received.Buffer, out var packet, out var error))
            {
                Console.WriteLine($"IGNORED_PING_REPLY: {error}");
                continue;
            }

            if (packet!.Type != PacketType.Pong || packet.SequenceNumber != sequence)
            {
                Console.WriteLine($"IGNORED_PING_REPLY type={packet!.Type} seq={packet.SequenceNumber}");
                continue;
            }

            UpdatePingTelemetry(packet, telemetry, adaptiveTimeout, NowUs());
            return;
        }
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        telemetry.Expire(NowUs());
        Console.WriteLine($"PING_TIMEOUT seq={sequence}; retaining current RTO");
    }
}

static void UpdatePingTelemetry(Packet packet, TelemetryTracker telemetry,
    AdaptiveTimeout adaptiveTimeout, long nowUs)
{
    var status = telemetry.OnPong(packet.SequenceNumber, nowUs, out var measurement);
    if (status == ResponseStatus.Received && measurement?.RttMs is { } rttMs)
    {
        adaptiveTimeout.OnSample(rttMs);
        Console.WriteLine($"PING_RTT seq={packet.SequenceNumber} rtt={rttMs:F2}ms " +
                          $"rto={adaptiveTimeout.RtoMs:F2}ms");
    }
}

static ushort ReadAcknowledgedSequence(Packet packet) =>
    System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(packet.Payload);

static long NowUs() => Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;

static int GetInt(string[] values, int index, int fallback) =>
    values.Length > index && int.TryParse(values[index], out var parsed) ? parsed : fallback;
