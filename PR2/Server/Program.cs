using System.Diagnostics;
using System.Net;
using UdpTelemetry.Protocol;
using UdpTelemetry.Transport;

var port = GetInt(args, 0, 7777);
var fixedDelayMs = GetInt(args, 1, 0);
var jitterMinMs = GetInt(args, 2, 0);
var jitterMaxMs = GetInt(args, 3, jitterMinMs);
var lossRate = GetDouble(args, 4, 0);
var seed = GetInt(args, 5, 20260914);
var random = new Random(seed);

using var transport = new UdpTransport(port);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine($"Telemetry UDP server listening on 0.0.0.0:{port}");
Console.WriteLine($"delay={fixedDelayMs}ms jitter={jitterMinMs}-{jitterMaxMs}ms loss={lossRate:P0} seed={seed}");

while (!shutdown.IsCancellationRequested)
{
    try
    {
        var received = await transport.ReceiveAsync(shutdown.Token);
        var receiveUs = NowUs();

        if (!Packet.TryDeserialize(received.Buffer, out var packet, out var error))
        {
            Console.WriteLine($"INVALID {received.RemoteEndPoint}: {error}");
            continue;
        }

        if (packet!.Type != PacketType.Ping)
        {
            Console.WriteLine($"IGNORED seq={packet.SequenceNumber} type={packet.Type}");
            continue;
        }

        if (random.NextDouble() < lossRate)
        {
            Console.WriteLine($"DROP seq={packet.SequenceNumber}");
            continue;
        }

        var delayMs = fixedDelayMs + (jitterMaxMs > jitterMinMs
            ? random.Next(jitterMinMs, jitterMaxMs + 1)
            : jitterMinMs);
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, shutdown.Token);
        }

        var sendUs = NowUs();
        var pong = Packet.Pong(packet.SequenceNumber, packet.ClientSendTimeUs, (ulong)receiveUs, (ulong)sendUs);
        await transport.SendAsync(pong.Serialize(), received.RemoteEndPoint, shutdown.Token);
        Console.WriteLine($"PONG seq={packet.SequenceNumber} delay={delayMs}ms");
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
        break;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"ERROR: {exception.Message}");
    }
}

static long NowUs() => Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;

static int GetInt(string[] args, int index, int fallback) =>
    args.Length > index && int.TryParse(args[index], out var value) ? value : fallback;

static double GetDouble(string[] args, int index, double fallback) =>
    args.Length > index && double.TryParse(args[index], out var value) ? value : fallback;