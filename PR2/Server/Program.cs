using System.Diagnostics;
using System.Globalization;
using System.Net;
using UdpTelemetry.Protocol;
using UdpTelemetry.Transport;

// Параметры позволяют воспроизводимо эмулировать задержку, джиттер и потери пакетов.
var port = GetInt(args, 0, 7777);
var fixedDelayMs = GetInt(args, 1, 0);
var jitterMinMs = GetInt(args, 2, 0);
var jitterMaxMs = GetInt(args, 3, jitterMinMs);
var lossRate = GetDouble(args, 4, 0);
var seed = GetInt(args, 5, 20260914);
// Фиксированный seed делает последовательность потерь и джиттера повторяемой.
var random = new Random(seed);

using var transport = new UdpTransport(port);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // Ctrl+C отменяет ReceiveAsync и завершает сервер без аварийного исключения.
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine($"Telemetry UDP server listening on 0.0.0.0:{port}");
Console.WriteLine($"delay={fixedDelayMs}ms jitter={jitterMinMs}-{jitterMaxMs}ms loss={lossRate:P0} seed={seed}");

while (!shutdown.IsCancellationRequested)
{
    try
    {
        // Сервер работает в бесконечном цикле: одна датаграмма - один ответ PONG.
        var received = await transport.ReceiveAsync(shutdown.Token);
        var receiveUs = NowUs();

        // Пакет сначала валидируется, и только затем используется его содержимое.
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

        // Потеря моделируется намеренным отсутствием PONG.
        if (random.NextDouble() < lossRate)
        {
            Console.WriteLine($"DROP seq={packet.SequenceNumber}");
            continue;
        }

        // Джиттер добавляется к фиксированной задержке как случайное значение из заданного диапазона.
        var delayMs = fixedDelayMs + (jitterMaxMs > jitterMinMs
            ? random.Next(jitterMinMs, jitterMaxMs + 1)
            : jitterMinMs);
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, shutdown.Token);
        }

        // PONG сохраняет метки приема и отправки сервера для диагностики.
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
    // Параметры эксперимента записываются с точкой (например, 0.05) независимо от локали Windows.
    args.Length > index && double.TryParse(
        args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        ? value
        : fallback;
