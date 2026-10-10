"""Turn part of the clean generated documents into scans and photos.

Reads  data/generated  (made by `DocGen generate`) and writes the full dataset to  data/dataset:
about 30 % of documents are degraded, the rest are copied unchanged, and manifest.csv and ground_truth/*.json
get the new `quality` (and `file`, because a photo is a .jpg).

Run:  .venv/bin/python tools/degrade/degrade.py
The random seed is fixed, so the same input always gives the same output.
"""

import argparse
import csv
import json
import random
import shutil
from collections import Counter
from pathlib import Path

import cv2
import numpy as np
import pymupdf

from effects import EFFECTS

BASE_SEED = 20261012
RENDER_DPI = 150
DEGRADED_SHARE = 0.30

# Repeated down the sorted list of picked documents, so every level is spread over all layouts and error types.
# 6 : 4 : 5 of 15 gives roughly 12 % light scans, 8 % heavy scans, 10 % photos of all documents.
LEVEL_PATTERN = ["scan_light"] * 6 + ["scan_heavy"] * 4 + ["photo"] * 5


def read_manifest(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8", newline="") as handle:
        return list(csv.DictReader(handle))


def write_manifest(path: Path, rows: list[dict[str, str]]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def pick_documents(rows: list[dict[str, str]]) -> dict[str, str]:
    """Choose which documents to degrade and how. Returns {document_id: quality}.

    Exact resends are skipped: they must stay byte-identical to the original, so they inherit its result later.
    Documents are sorted by layout, split and error type, then every n-th one is taken (systematic sampling),
    which is a cheap way to cover every group.
    """
    candidates = [r for r in rows if not r["duplicate_of"]]
    candidates.sort(key=lambda r: (r["layout"], r["split"], r["injected_codes"], r["document_type"], r["document_id"]))

    wanted = round(len(candidates) * DEGRADED_SHARE)
    step = len(candidates) / wanted
    start = random.Random(BASE_SEED).uniform(0, step)  # a random but fixed starting point inside the first step
    picked = [candidates[int(start + k * step)] for k in range(wanted)]

    return {row["document_id"]: LEVEL_PATTERN[i % len(LEVEL_PATTERN)] for i, row in enumerate(picked)}


def render_pages(pdf_path: Path) -> list[np.ndarray]:
    """Draw each PDF page to an OpenCV image."""
    pages = []
    with pymupdf.open(pdf_path) as document:
        for page in document:
            pixmap = page.get_pixmap(dpi=RENDER_DPI, alpha=False)  # rasterise at 150 dpi, no transparency
            rgb = np.frombuffer(pixmap.samples, dtype=np.uint8).reshape(pixmap.height, pixmap.width, 3)
            pages.append(cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR))  # OpenCV expects blue-green-red
    return pages


def save_as_scan_pdf(pages: list[np.ndarray], target: Path) -> None:
    """Write the pages as an image-only PDF: no text layer, like a real scan, so text must be read with OCR."""
    with pymupdf.open() as document:
        for image in pages:
            ok, jpeg = cv2.imencode(".jpg", image, [cv2.IMWRITE_JPEG_QUALITY, 80])
            assert ok
            page = document.new_page(width=595, height=842)  # A4 in points
            page.insert_image(page.rect, stream=jpeg.tobytes())
        document.save(target, deflate=True, no_new_id=True)  # no random trailer ID, so the file is reproducible


def degrade_document(source: Path, target_stem: Path, quality: str, seed: int) -> Path:
    """Degrade one document and return the path that was written."""
    # The effects draw random numbers from three places: Python's random, numpy's random and OpenCV's own generator.
    # Seeding all three makes each document come out the same on every run.
    random.seed(seed)
    np.random.seed(seed % (2**32))
    cv2.setRNGSeed(seed)

    effect = EFFECTS[quality]
    pages = render_pages(source)

    if quality == "photo":
        target = target_stem.with_suffix(".jpg")
        ok, jpeg = cv2.imencode(".jpg", effect(pages[0]), [cv2.IMWRITE_JPEG_QUALITY, 82])
        assert ok
        target.write_bytes(jpeg.tobytes())
    else:
        target = target_stem.with_suffix(".pdf")
        save_as_scan_pdf([effect(p) for p in pages], target)
    return target


def update_ground_truth(source: Path, target: Path, quality: str, file: str) -> None:
    truth = json.loads(source.read_text(encoding="utf-8"))
    truth["quality"] = quality
    truth["file"] = file
    target.write_text(json.dumps(truth, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--input", type=Path, default=Path("data/generated"))
    parser.add_argument("--output", type=Path, default=Path("data/dataset"))
    args = parser.parse_args()

    rows = read_manifest(args.input / "manifest.csv")
    chosen = pick_documents(rows)

    if args.output.exists():
        shutil.rmtree(args.output)  # always rebuild from the clean input, never from an earlier output
    for folder in ("invoices", "delivery-notes", "ground_truth"):
        (args.output / folder).mkdir(parents=True)

    written: dict[str, Path] = {}  # document id -> file written, so resends can copy their original
    for row in rows:
        document_id = row["document_id"]
        source = args.input / row["file"]

        if row["duplicate_of"]:
            continue  # handled in the second pass

        quality = chosen.get(document_id, "clean")
        if quality == "clean":
            target = args.output / row["file"]
            shutil.copyfile(source, target)
        else:
            number = int("".join(ch for ch in document_id if ch.isdigit()))
            seed = BASE_SEED + number + (10_000 if document_id.startswith("dn-") else 0)
            target = degrade_document(source, (args.output / row["file"]).with_suffix(""), quality, seed)

        written[document_id] = target
        row["quality"] = quality
        row["file"] = target.relative_to(args.output).as_posix()

    # Second pass: an exact resend is the same file again, degraded or not.
    for row in rows:
        original = row["duplicate_of"]
        if not original:
            continue
        original_target = written[original]
        target = args.output / row["file"].replace(".pdf", original_target.suffix)
        shutil.copyfile(original_target, target)
        written[row["document_id"]] = target
        row["quality"] = next(r["quality"] for r in rows if r["document_id"] == original)
        row["file"] = target.relative_to(args.output).as_posix()

    for row in rows:
        update_ground_truth(
            args.input / "ground_truth" / f"{row['document_id']}.json",
            args.output / "ground_truth" / f"{row['document_id']}.json",
            row["quality"],
            row["file"],
        )

    write_manifest(args.output / "manifest.csv", rows)

    counts = Counter(r["quality"] for r in rows)
    print(f"Wrote {len(rows)} documents to {args.output}")
    for quality in ("clean", "scan_light", "scan_heavy", "photo"):
        print(f"  {quality:<11} {counts[quality]:>4}  ({counts[quality] / len(rows):.0%})")
    for layout, count in sorted(Counter(r["layout"] for r in rows if r["quality"] != "clean").items()):
        print(f"  degraded in {layout}: {count}")


if __name__ == "__main__":
    main()
