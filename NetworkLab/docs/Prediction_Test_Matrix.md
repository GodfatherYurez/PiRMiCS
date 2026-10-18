# Prediction test matrix

Engine: Unreal Engine 5.8.3. Use a Listen Server and one connected client. For network emulation, apply the profile on the client and record the actual incoming/outgoing settings used.

| Test | Network conditions | Action | Expected result | Actual result | Evidence |
| --- | --- | --- | --- | --- | --- |
| T1 | No emulation | Walk and jump on client | Character moves locally; remote character follows server movement | Server/client connection and replication smoke tests passed after the update; input-level walk/jump confirmation is still needed. | Capture both windows |
| T2 | 100 ms one-way latency, 0% loss | Disable prediction with F1; hold Shift while moving | Client begins sprint only after server approval | Pending interactive capture | `baseline_no_prediction.*` |
| T3 | 100 ms one-way latency, 0% loss | Enable prediction with F1; hold Shift while moving | Immediate sprint response; server checks the saved move | Pending interactive capture | `prediction_enabled.*` |
| T4 | 150–200 ms latency, 3–5% packet loss | Walk, jump, and sprint in each mode | No client-authored position is accepted as authority; corrections remain usable | Pending interactive capture | Capture both windows |
| T5 | 150–200 ms latency, 3–5% loss | On the connected client, hold Shift with prediction enabled and press F3 | The local demo speed lasts 1.5 seconds; the server sends movement corrections, then the client returns to configured speed | Pending interactive capture | `correction_example.*` and `[Prediction]` / `[Server]` logs |

Unreal's normal walking already uses CharacterMovementComponent prediction and reconciliation. T2 and T3 compare the extra sprint action, not a replacement of the engine's ordinary movement path.

## Applying network emulation

Run the console commands in the connected client's Unreal console (`~`). These use Unreal's built-in `Net` packet simulation settings. For T2/T3, set one-way client-to-server delay to 100 ms and leave loss/delay on the incoming direction at zero:

```text
Net PktLag=100 PktLagMin=0 PktLagMax=0 PktIncomingLagMin=0 PktIncomingLagMax=0 PktLoss=0 PktIncomingLoss=0
```

For T4/T5, use 75–100 ms each way (150–200 ms round trip) and 3–5% packet loss each way:

```text
Net PktLag=0 PktLagMin=75 PktLagMax=100 PktIncomingLagMin=75 PktIncomingLagMax=100 PktLoss=3 PktIncomingLoss=3
```

To clear emulation after each run:

```text
Net PktLag=0 PktLagMin=0 PktLagMax=0 PktIncomingLagMin=0 PktIncomingLagMax=0 PktLoss=0 PktIncomingLoss=0
```

Capture the client and listen-server windows during each test. For T5, keep the server Output Log visible so the movement-correction message can be matched with the client view.
