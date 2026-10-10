"""Pick documents for the real-photo set, and add the photos to the dataset afterwards.

Step 1  .venv/bin/python tools/degrade/real_photos.py suggest
        Prints 5 test-split documents. Print them on paper, photograph each with a phone, and save the photos as
        data/real_photos/<document_id>.jpg  (for example data/real_photos/inv-0123.jpg).
Step 2  .venv/bin/python tools/degrade/real_photos.py register
        Replaces those documents in data/dataset with your photos and marks them quality=photo_real.

Real photos are test-only, so `register` refuses a document from the dev split.
`degrade.py` rebuilds data/dataset from scratch, so run `register` again after every `degrade.py` run.
"""

import argparse
import csv
import json
from pathlib import Path

DATASET = Path("data/dataset")
PHOTOS = Path("data/real_photos")


def read_manifest() -> list[dict[str, str]]:
    with (DATASET / "manifest.csv").open(encoding="utf-8", newline="") as handle:
        return list(csv.DictReader(handle))


def suggest() -> None:
    rows = [r for r in read_manifest() if r["split"] == "test" and r["quality"] == "clean" and not r["duplicate_of"]]
    chosen = []
    # One invoice from each layout, preferring documents that carry an injected error, plus the held-out layout.
    for layout in ("L1", "L2", "L3", "L4", "L5"):
        in_layout = [r for r in rows if r["layout"] == layout and r["document_type"] == "Invoice"]
        in_layout.sort(key=lambda r: (not r["injected_codes"], r["document_id"]))
        if in_layout:
            chosen.append(in_layout[0])

    print("Print these, photograph each one, save as data/real_photos/<document_id>.jpg:\n")
    for row in chosen:
        print(f"  {row['document_id']}  layout {row['layout']}  {row['injected_codes'] or 'no injected error'}")
        print(f"      {DATASET / row['file']}")


def register() -> None:
    rows = read_manifest()
    by_id = {r["document_id"]: r for r in rows}
    photos = sorted(PHOTOS.glob("*.jpg"))
    if not photos:
        raise SystemExit(f"No photos found in {PHOTOS}")

    for photo in photos:
        row = by_id.get(photo.stem)
        if row is None:
            raise SystemExit(f"{photo.name}: no document with id {photo.stem!r} in the manifest")
        if row["split"] != "test":
            raise SystemExit(f"{photo.name}: real photos must be test-split documents, but {row['document_id']} is in dev")

        old_file = DATASET / row["file"]
        new_file = old_file.with_suffix(".jpg")
        new_file.write_bytes(photo.read_bytes())
        if old_file != new_file:
            old_file.unlink()  # the photo replaces the PDF in the dataset

        row["quality"] = "photo_real"
        row["file"] = new_file.relative_to(DATASET).as_posix()

        truth_path = DATASET / "ground_truth" / f"{row['document_id']}.json"
        truth = json.loads(truth_path.read_text(encoding="utf-8"))
        truth["quality"] = "photo_real"
        truth["file"] = row["file"]
        truth_path.write_text(json.dumps(truth, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"registered {photo.name} -> {row['file']}")

    with (DATASET / "manifest.csv").open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["suggest", "register"])
    args = parser.parse_args()
    suggest() if args.command == "suggest" else register()


if __name__ == "__main__":
    main()
