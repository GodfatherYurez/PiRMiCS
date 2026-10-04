using UdpTelemetry.Protocol;
using UdpTelemetry.Reliability;

Run("SHOOT and ACK preserve requiresAck and acknowledged sequence", () =>
{
    var shoot = Packet.Shoot(42, 2, requiresAck: true);
    Assert(Packet.TryDeserialize(shoot.Serialize(), out var parsedShoot, out _), "Reliable SHOOT rejected");
    Assert(parsedShoot!.RequiresAck && parsedShoot.Payload[0] == 2, "SHOOT fields changed");

    var ack = Packet.Acknowledgement(42);
    Assert(Packet.TryDeserialize(ack.Serialize(), out var parsedAck, out _), "ACK rejected");
    Assert(!parsedAck!.RequiresAck && parsedAck.SequenceNumber == 42, "ACK header changed");
    Assert(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(parsedAck.Payload) == 42,
        "ACK payload changed");
});

Run("Protocol rejects malformed requiresAck and ACK payload", () =>
{
    var bytes = Packet.Shoot(1, 1, true).Serialize();
    bytes[9] = 2;
    Assert(!Packet.TryDeserialize(bytes, out _, out _), "Invalid requiresAck value accepted");

    bytes = Packet.Acknowledgement(1).Serialize();
    bytes[11] = 0;
    Assert(!Packet.TryDeserialize(bytes, out _, out _), "Mismatched ACK payload accepted");
});

Run("Packet becomes due at RTO and is not returned too early again", () =>
{
    var channel = new ReliableChannel(maxAttempts: 3);
    channel.OnSent(1, [0x01, 0x02], nowUs: 0);
    Assert(channel.CollectForRetransmission(199_999, 200_000).Count == 0, "Early retransmission returned");
    var due = channel.CollectForRetransmission(200_000, 200_000);
    Assert(due.Count == 1 && due[0].Attempts == 2, "First retransmission was not tracked");
    Assert(channel.CollectForRetransmission(399_999, 200_000).Count == 0,
        "Packet was returned more often than the RTO");
    Assert(channel.CollectForRetransmission(400_000, 200_000).Count == 1,
        "Second timeout was not observed");
});

Run("Max attempts move packet to failed", () =>
{
    var channel = new ReliableChannel(maxAttempts: 2);
    channel.OnSent(7, [], nowUs: 0);
    Assert(channel.CollectForRetransmission(100_000, 100_000).Count == 1, "Retry missing");
    Assert(channel.CollectForRetransmission(200_000, 100_000).Count == 0, "Failed packet retransmitted");
    Assert(channel.FailedCount == 1 && channel.PendingCount == 0, "Packet did not move to failed");
});

Run("Duplicate ACK is ignored", () =>
{
    var channel = new ReliableChannel();
    channel.OnSent(3, [], nowUs: 100);
    Assert(channel.OnAckReceived(3, 5_100, out var rtt) && rtt == 5, "First ACK was not accepted");
    Assert(!channel.OnAckReceived(3, 6_000, out _), "Duplicate ACK was accepted");
});

Run("Retransmitted packet does not provide an ambiguous RTT sample", () =>
{
    var channel = new ReliableChannel();
    channel.OnSent(8, [], nowUs: 0);
    channel.CollectForRetransmission(100_000, 100_000);
    Assert(channel.OnAckReceived(8, 150_000, out var rtt) && rtt is null,
        "ACK after retransmission was used as an RTT sample");
});

Run("Adaptive timeout follows SRTT/RTTVAR and clamps to configured bounds", () =>
{
    var timeout = new AdaptiveTimeout();
    timeout.OnSample(100);
    Assert(timeout.SrttMs == 100 && timeout.RttVarMs == 50 && timeout.RtoMs == 300,
        "Initial SRTT/RTTVAR/RTO is wrong");
    timeout.OnSample(120);
    Assert(timeout.SrttMs == 102.5 && timeout.RttVarMs == 42.5 && timeout.RtoMs == 272.5,
        "Adaptive update is wrong");
    timeout.OnSample(10_000);
    Assert(timeout.RtoMs == 3000, "RTO upper bound was not applied");
});

Console.WriteLine("All PR3 reliability tests passed.");

static void Run(string name, Action test)
{
    test();
    Console.WriteLine($"PASS {name}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
