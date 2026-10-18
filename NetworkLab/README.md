# NetworkLab

Unreal Engine 5.8.3 Third Person prototype for practical works 4 and 5.

## Run

Open `NetworkLab.uproject`. Start two players with a Listen Server and one client. The server creates 50 visible replicated profiling markers on the Third Person map.

## Controls

- WASD / Space: move and jump.
- Hold Left Shift: sprint.
- F1: select predicted or server-response sprint.
- F3 on the connected client while predicted sprint is held: create a short local prediction error to show server reconciliation.
- F2 in the Listen Server: switch baseline and optimized replication profiles.

See `docs/Prediction_Implementation.md` and the test/profiling matrix for the work details.
