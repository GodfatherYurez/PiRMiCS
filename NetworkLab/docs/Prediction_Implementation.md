# Prediction and server reconciliation

## Project

- Project: NetworkLab.
- Engine: Unreal Engine 5.8.3, Windows 11 SDK 10.0.26100.
- Player class: `ANetworkLabCharacter`, derived from `ACharacter`.
- Movement class: `UNetworkLabMovementComponent`, derived from `UCharacterMovementComponent`.
- Version control: the project directory was created locally and does not yet have a GitVerse remote. Record the demonstration commit hash after the course repository is connected.

## Data flow

`Shift pressed -> client sets sprint intent -> sprint bit is recorded in FNetworkLabSavedMove -> packed movement RPC reaches the server -> server rebuilds movement and checks that the character is on the ground -> valid movement is acknowledged, or an invalid position is corrected -> client replays unacknowledged saved moves.`

The additional sprint request RPC is used to authorize the non-predicted comparison mode. It sends the requested button state and a sequence number. It never sends an end position or a speed value. The movement packet remains responsible for carrying the saved sprint flag.

## Responsibilities

- The GameMode is authoritative. It creates 50 profiling actors and selects the baseline or optimized replication profile.
- The PlayerController handles the F2 server-only replication profile switch.
- The Character reads the local Shift and F1/F3 keys and sends the server sprint request.
- The custom CharacterMovementComponent computes a walk speed of 500 and a sprint speed of 900 units per second. It accepts the sprint flag only while grounded. In the no-prediction mode, the server first approves the request.
- FNetworkLabSavedMove stores both the sprint intent and prediction mode with the movement step, prevents moves with different sprint states from being merged, and restores the state when Unreal replays moves after a correction.
- Other players see the server-replicated character movement, with Unreal's built-in network smoothing.

## Demonstration controls

- WASD and Space: standard Third Person movement.
- Hold Left Shift while moving: sprint.
- F1: switch the locally controlled player between immediate prediction and server-response mode. Prediction starts enabled.
- F3 on a connected client, while prediction is enabled and Shift is held: for 1.5 seconds, locally use 1.8 times the normal speed. The server continues to calculate its configured speed, so the normal CharacterMovementComponent correction and saved-move replay can be observed. This is a development demonstration hook; it does not transmit client coordinates.
- F2 in the listen-server window: switch the 50 actor replication test between baseline and optimized settings.

## Test status and limits

The baseline Listen Server and a second client connected successfully on localhost. Logs confirm that the server spawned 50 actors and the client joined. T2–T5 still need an interactive demonstration at the specified network delays, plus video or screenshots; T1 still needs an input-level walk/jump check on the updated build. The local machine has no Network Profiler viewer for actor-level breakdown. F3 intentionally distorts only the owning client's prediction for a brief test; it is not a player feature.

Known limits: sprint duration and stamina are not implemented. The server-side action rule in this prototype is that sprinting is allowed only while grounded. No GitVerse link or team/author metadata was available in the project directory.
