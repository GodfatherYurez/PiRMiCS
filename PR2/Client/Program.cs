using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using UdpTelemetry.Protocol;
using UdpTelemetry.Telemetry;
using UdpTelemetry.Transport;

// Аргументы задают адрес сервера и параметры одной серии измерений.
var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = GetInt(args, 1, 7777);
var experimentId = args.Length > 2 ? args[2] : "baseline";
var count = GetInt(args, 3, 50);
var intervalMs = GetInt(args, 4, 200);
var outputPath = args.Length > 5 ? args[5] : Path.Combine("docs", "latency_samples.csv");
// Имя хоста разрешается заранее; для текущего протокола выбирается IPv4-адрес.
var addresses = await Dns.GetHostAddressesAsync(host);
var server = new IPEndPoint(
    addresses.First(address => address.AddressFamily == AddressFamily.InterNetwork), port);
// Tracker хранит ожидаемые PONG и вычисляет все метрики серии.
var tracker = new TelemetryTracker();
var measurements = new List<Measurement>();
using var transport = new UdpTransport();
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // Пользователь может остановить серию Ctrl+C без потери уже собранных строк.
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

var sequence = (ushort)1;
var startUs = NowUs();
for (var sample = 1; sample <= count && !shutdown.IsCancellationRequested; sample++)
{
    // Время измеряется относительно старта серии, чтобы CSV был компактнее и понятнее.
    var sentUs = NowUs() - startUs;
    while (!tracker.TrackPing(sequence, sentUs, experimentId, sample))
    {
        sequence = unchecked((ushort)(sequence + 1));
    }

    // Сначала регистрируем PING в tracker, затем отправляем его по UDP.
    await transport.SendAsync(Packet.Ping(sequence, (ulong)sentUs).Serialize(), server, shutdown.Token);
    Console.WriteLine($"> PING sample={sample} seq={sequence}");

    // Для каждого PING создается отдельный тайм-аут длительностью одна секунда.
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
    timeout.CancelAfter(TimeSpan.FromSeconds(1));
    try
    {
        while (true)
        {
            var received = await transport.ReceiveAsync(timeout.Token);
            // Случайные или поврежденные UDP-датаграммы не должны прерывать эксперимент.
            if (!Packet.TryDeserialize(received.Buffer, out var packet, out var error))
            {
                Console.WriteLine($"< INVALID: {error}");
                continue;
            }

            if (packet!.Type != PacketType.Pong)
            {
                continue;
            }

            // Tracker сопоставляет PONG с исходным PING по SequenceNumber и рассчитывает RTT.
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
        // Все просроченные PING переводятся в статус timeout и попадут в CSV.
        measurements.AddRange(tracker.Expire(NowUs() - startUs));
        Console.WriteLine($"! TIMEOUT seq={sequence}");
    }

    sequence = unchecked((ushort)(sequence + 1));
    await Task.Delay(intervalMs, shutdown.Token);
}

// CSV записывается целиком после серии, чтобы в нем были единообразные заголовки и статусы.
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

// Переводим тики Stopwatch в микросекунды, не используя системные часы DateTime.
static long NowUs() => Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;

static int GetInt(string[] args, int index, int fallback) =>
    args.Length > index && int.TryParse(args[index], out var value) ? value : fallback;
