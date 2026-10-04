import argparse
import csv
from collections import defaultdict
from pathlib import Path

import matplotlib.pyplot as plt


def read_rows(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream, delimiter=";"))


def main() -> None:
    parser = argparse.ArgumentParser(description="Plot measured PR3 reliability runs.")
    parser.add_argument("--input", type=Path, default=Path("docs/reliability_samples.csv"))
    parser.add_argument("--output", type=Path, default=Path("docs/graphs"))
    args = parser.parse_args()
    rows = read_rows(args.input)
    if not rows:
        raise SystemExit(f"No experiment rows in {args.input}")
    args.output.mkdir(parents=True, exist_ok=True)

    by_experiment: dict[str, list[dict[str, str]]] = defaultdict(list)
    for row in rows:
        by_experiment[row["experiment_id"]].append(row)

    loss_series = [("baseline", 0), ("loss_5", 5), ("loss_10", 10), ("loss_20", 20)]
    present = [(name, loss) for name, loss in loss_series if name in by_experiment]
    if present:
        means = [
            sum(float(row["attempts"]) for row in by_experiment[name]) / len(by_experiment[name])
            for name, _ in present
        ]
        plt.figure(figsize=(8, 4.5))
        plt.plot([loss for _, loss in present], means, marker="o")
        plt.xlabel("Configured packet loss (%)")
        plt.ylabel("Mean attempts per command")
        plt.title("Reliable UDP: attempts vs packet loss")
        plt.grid(True, alpha=0.3)
        plt.tight_layout()
        plt.savefig(args.output / "average-attempts-by-loss.png", dpi=160)
        plt.close()

    jitter = by_experiment.get("jitter_loss_10", [])
    jitter = [row for row in jitter if row["final_rto_ms"]]
    if jitter:
        jitter.sort(key=lambda row: int(row["sample"]))
        plt.figure(figsize=(9, 4.5))
        plt.plot([int(row["sample"]) for row in jitter],
                 [float(row["final_rto_ms"]) for row in jitter], marker=".", linewidth=1)
        plt.xlabel("Command sample")
        plt.ylabel("RTO (ms)")
        plt.title("Adaptive RTO: jitter_loss_10")
        plt.grid(True, alpha=0.3)
        plt.tight_layout()
        plt.savefig(args.output / "rto-over-time-jitter-loss-10.png", dpi=160)
        plt.close()

    print(f"Created plots in {args.output.resolve()} from {len(rows)} measured rows.")


if __name__ == "__main__":
    main()
