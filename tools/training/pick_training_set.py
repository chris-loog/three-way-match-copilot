"""Pick the delivery notes to label for the custom model: 7 per dev layout, never the held-out layout.

Per layout: up to 2 light scans, 1 photo, 1 heavy scan, the rest clean. Deterministic (sorted by id).
Copies the files into data/training/ and prints the list. Run: python3 tools/training/pick_training_set.py
"""
import csv
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATASET = ROOT / "data" / "dataset"
OUT = ROOT / "data" / "training"
PER_LAYOUT = 7
QUOTA = [("scan_light", 2), ("photo", 1), ("scan_heavy", 1), ("clean", PER_LAYOUT)]


def pick(rows):
    chosen = []
    for quality, limit in QUOTA:
        for row in rows:
            if len(chosen) == PER_LAYOUT:
                return chosen
            if row["quality"] == quality and row not in chosen and sum(r["quality"] == quality for r in chosen) < limit:
                chosen.append(row)
    return chosen


def main():
    with open(DATASET / "manifest.csv", newline="", encoding="utf-8") as f:
        rows = [r for r in csv.DictReader(f) if r["document_type"] == "DeliveryNote" and r["split"] == "dev"]
    rows.sort(key=lambda r: r["document_id"])
    OUT.mkdir(parents=True, exist_ok=True)
    total = 0
    for layout in sorted({r["layout"] for r in rows}):
        for row in pick([r for r in rows if r["layout"] == layout]):
            source = DATASET / row["file"]
            shutil.copy2(source, OUT / source.name)
            print(f'{layout}  {row["quality"]:<11} {source.name}')
            total += 1
    print(f"{total} files in {OUT}")


if __name__ == "__main__":
    main()
