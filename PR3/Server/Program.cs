using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using UdpTelemetry.Protocol;

var port = GetInt(args, 0, 7777);
var lossRate = GetDouble(args, 1, 0);
var fixedDelayMs = GetInt(args, 2, 0);
var jitterMinMs = GetInt(args, 3, 0);
var jitterMaxMs = GetInt(args, 4, jitterMinMs);
var seed = GetInt(args, 5, 20261004);
if (port is < 1 or > 65535 || lossRate is < 0 or > 1 || fixedDelayMs < 0 ||
    jitterMinMs < 0 || jitterMaxMs < jitterMinMs || jitterMaxMs > 60000)
{
    Console.Error.WriteLine("Usage: Server [port] [lossRate:0..1] [delayMs] [jitterMinMs] " +
                            "[jitterMaxMs] [seed]");
    return 2;
}

var random = new Random(seed);
var recentByClient = new Dictionary<string, RecentSequenceWindow>();
var positionByClient = new Dictionary<string, Vector3>();
using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine($"PR3 UDP server on 0.0.0.0:{port}; loss={lossRate:P0}, delay={fixedDelayMs}ms, " +
                  $"jitter={jitterMinMs}-{jitterMaxMs}ms, seed={seed}");
while (!shutdown.IsCancellationRequested)
{
    try
    {
        var received = await udp.ReceiveAsync(shutdown.Token);
        if (random.NextDouble() < lossRate)
        {
            Console.WriteLine($"SIMULATED_INCOMING_LOSS {received.RemoteEndPoint} bytes={received.Buffer.Length}");
            continue;
        }

        if (!Packet.TryDeserialize(received.Buffer, out var packet, out var error))
        {
            Console.WriteLine($"INVALID {received.RemoteEndPoint}: {error}");
            continue;
        }

        var request = packet!;
        var clientKey = received.RemoteEndPoint.ToString();
        switch (request.Type)
        {
            case PacketType.Ping:
            {
                var serverReceiveUs = NowUs();
                var serverSendUs = NowUs();
                var pong = Packet.Pong(request.SequenceNumber, request.ClientSendTimeUs,
                    (ulong)serverReceiveUs, (ulong)serverSendUs);
                _ = SendResponseAsync(udp, pong, received.RemoteEndPoint, random, lossRate,
                    fixedDelayMs, jitterMinMs, jitterMaxMs, shutdown.Token);
                Console.WriteLine($"PONG seq={request.SequenceNumber} response scheduled");
                break;
            }

            case PacketType.Movement:
            {
                var firstDelivery = !request.RequiresAck || RememberFirstDelivery(
                    clientKey, request.SequenceNumber, recentByClient);
                if (request.RequiresAck)
                {
                    Acknowledge(udp, request, received.RemoteEndPoint, random,
                        lossRate, fixedDelayMs, jitterMinMs, jitterMaxMs, shutdown.Token);
                }

                if (!firstDelivery)
                {
                    Console.WriteLine($"DUPLICATE_MOVEMENT from={clientKey} seq={request.SequenceNumber}; effect skipped");
                    break;
                }

                var delta = new Vector3(ReadFloat(request.Payload, 0), ReadFloat(request.Payload, 4),
                    ReadFloat(request.Payload, 8));
                positionByClient.TryGetValue(clientKey, out var position);
                position += delta;
                positionByClient[clientKey] = position;
                Console.WriteLine($"MOVEMENT from={clientKey} delta={delta} position={position}");
                break;
            }

            case PacketType.Shoot:
            {
                var firstDelivery = true;
                if (request.RequiresAck)
                {
                    firstDelivery = RememberFirstDelivery(clientKey, request.SequenceNumber, recentByClient);

                    // ACK is issued before the gameplay effect; repeated SHOT datagrams only get re-ACKed.
                    Acknowledge(udp, request, received.RemoteEndPoint, random,
                        lossRate, fixedDelayMs, jitterMinMs, jitterMaxMs, shutdown.Token);
                }

                if (firstDelivery)
                {
                    Console.WriteLine($"SHOOT from={clientKey} seq={request.SequenceNumber} " +
                                      $"weaponId={request.Payload[0]} requiresAck={request.RequiresAck}");
                }
                else
                {
                    Console.WriteLine($"DUPLICATE_SHOOT from={clientKey} seq={request.SequenceNumber}; effect skipped");
                }

                break;
            }

            case PacketType.Ack:
                Console.WriteLine($"UNEXPECTED_ACK from={clientKey} seq={request.SequenceNumber}");
                break;

            default:
                Console.WriteLine($"UNSUPPORTED_PACKET from={clientKey} type={request.Type}");
                break;
        }
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
        break;
    }
    catch (SocketException exception) when (shutdown.IsCancellationRequested)
    {
        Console.WriteLine($"Stopping: {exception.Message}");
        break;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"ERROR: {exception.Message}");
    }
}

static void Acknowledge(UdpClient udp, Packet request, IPEndPoint endpoint,
    Random random, double lossRate, int fixedDelayMs, int jitterMinMs, int jitterMaxMs,
    CancellationToken cancellationToken)
{
    var acknowledgement = Packet.Acknowledgement(request.SequenceNumber);
    _ = SendResponseAsync(udp, acknowledgement, endpoint, random, lossRate,
        fixedDelayMs, jitterMinMs, jitterMaxMs, cancellationToken);
}

static bool RememberFirstDelivery(string clientKey, ushort sequence,
    Dictionary<string, RecentSequenceWindow> recentByClient)
{
    if (!recentByClient.TryGetValue(clientKey, out var recent))
    {
        recent = new RecentSequenceWindow(4096);
        recentByClient.Add(clientKey, recent);
    }

    return recent.TryAdd(sequence);
}

static async Task SendResponseAsync(UdpClient udp, Packet packet, IPEndPoint endpoint,
    Random random, double lossRate, int fixedDelayMs, int jitterMinMs, int jitterMaxMs,
    CancellationToken cancellationToken)
{
    try
    {
        if (random.NextDouble() < lossRate)
        {
            Console.WriteLine($"SIMULATED_OUTGOING_LOSS type={packet.Type} seq={packet.SequenceNumber}");
            return;
        }

        var delay = NextDelayMs(random, fixedDelayMs, jitterMinMs, jitterMaxMs);
        if (delay > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }

        await udp.SendAsync(packet.Serialize(), endpoint, cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // Normal shutdown: delayed responses are discarded.
    }
    catch (SocketException exception)
    {
        Console.WriteLine($"RESPONSE_SEND_ERROR type={packet.Type} seq={packet.SequenceNumber}: {exception.Message}");
    }
}

static int NextDelayMs(Random random, int fixedDelayMs, int jitterMinMs, int jitterMaxMs) =>
    fixedDelayMs + (jitterMaxMs > jitterMinMs
        ? random.Next(jitterMinMs, jitterMaxMs + 1)
        : jitterMinMs);

static float ReadFloat(ReadOnlySpan<byte> payload, int offset) =>
    BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, sizeof(uint))));

static long NowUs() => Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;

static int GetInt(string[] values, int index, int fallback) =>
    values.Length > index && int.TryParse(values[index], out var parsed) ? parsed : fallback;

static double GetDouble(string[] values, int index, double fallback) =>
    values.Length > index && double.TryParse(values[index], NumberStyles.Float,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

sealed class RecentSequenceWindow(int capacity)
{
    private readonly HashSet<ushort> sequences = [];
    private readonly Queue<ushort> insertionOrder = [];

    public bool TryAdd(ushort sequence)
    {
        if (!sequences.Add(sequence))
        {
            return false;
        }

        insertionOrder.Enqueue(sequence);
        if (insertionOrder.Count > capacity)
        {
            sequences.Remove(insertionOrder.Dequeue());
        }

        return true;
    }
}
