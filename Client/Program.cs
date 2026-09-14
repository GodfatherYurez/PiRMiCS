using System.Net;
using System.Net.Sockets;
using UdpBasics.Protocol;

var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 7777;
var addresses = await Dns.GetHostAddressesAsync(host);
var serverAddress = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
    ?? throw new InvalidOperationException("The server host has no IPv4 address.");
var server = new IPEndPoint(serverAddress, port);

using var udp = new UdpClient(AddressFamily.InterNetwork);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine($"UDP client sends commands to {server} once per second.");
Console.WriteLine("Press Ctrl+C to stop.");

ushort sequenceNumber = 1;
while (!shutdown.IsCancellationRequested)
{
    var packet = sequenceNumber % 3 == 0
        ? Packet.Shoot(sequenceNumber, weaponId: 2)
        : Packet.Movement(sequenceNumber, new Vector3(0.5f, -0.1f, 0.25f));

    try
    {
        await udp.SendAsync(packet.Serialize(), server, shutdown.Token);
        Console.WriteLine($"> seq={packet.SequenceNumber} type={packet.Type} bytes={packet.Payload.Length}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var received = await udp.ReceiveAsync(timeout.Token);

        if (!Packet.TryDeserialize(received.Buffer, out var reply, out var error))
        {
            Console.WriteLine($"< Invalid response: {error}");
        }
        else if (reply!.Type == PacketType.State)
        {
            var state = reply.ReadState();
            Console.WriteLine(
                $"< seq={reply.SequenceNumber} STATE position=({state.X:F2}, {state.Y:F2}, {state.Z:F2}) shots={state.Shots}");
        }
        else
        {
            Console.WriteLine($"< seq={reply.SequenceNumber} ERROR {reply.ReadError()}");
        }
    }
    catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
    {
        Console.WriteLine($"! No response for seq={sequenceNumber} within 2 seconds.");
    }

    sequenceNumber = unchecked((ushort)(sequenceNumber + 1));
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(1), shutdown.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

Console.WriteLine("Client stopped.");
