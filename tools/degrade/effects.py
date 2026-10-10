"""Image effects that make a clean page look scanned or photographed.

Every function takes and returns an OpenCV image: a numpy array of shape (height, width, 3), colour order BGR, uint8.
"""

import random

import cv2
import numpy as np
from augraphy import (
    AugraphyPipeline,
    BrightnessTexturize,
    Geometric,
    InkBleed,
    Jpeg,
    NoiseTexturize,
    ShadowCast,
    SubtleNoise,
)
from PIL import Image, ImageDraw, ImageFont


def scan_light(page: np.ndarray) -> np.ndarray:
    """A decent office scan: slightly crooked, a little noise, mild compression."""
    pipeline = AugraphyPipeline(
        ink_phase=[InkBleed(intensity_range=(0.1, 0.25), p=0.7)],
        paper_phase=[BrightnessTexturize(texturize_range=(0.9, 0.99), p=0.6)],
        post_phase=[
            Geometric(rotate_range=(-1, 1), padding_value=(255, 255, 255), p=1),  # rotate up to one degree, fill the corners white
            SubtleNoise(subtle_range=6, p=1),
            Jpeg(quality_range=(60, 90), p=1),
        ],
    )
    return pipeline(page)


def scan_heavy(page: np.ndarray) -> np.ndarray:
    """A bad scan from an old copier: fold lines, toner streaks, shadows, heavy compression, and a stamp.

    Augraphy's Folding, DirtyRollers and BadPhotoCopy give a different result on every run even with all seeds fixed
    (they use parallel numba code), so fold_lines, roller_streaks and toner_specks below do the same job reproducibly.
    """
    pipeline = AugraphyPipeline(
        ink_phase=[InkBleed(intensity_range=(0.3, 0.6), p=0.9)],
        paper_phase=[NoiseTexturize(sigma_range=(2, 4), turbulence_range=(2, 4), p=1)],
        post_phase=[
            ShadowCast(p=0.5),
            Geometric(rotate_range=(-3, 3), padding_value=(255, 255, 255), p=1),
            Jpeg(quality_range=(25, 50), p=1),
        ],
    )
    return add_stamp(toner_specks(roller_streaks(fold_lines(pipeline(page)))))


def fold_lines(page: np.ndarray) -> np.ndarray:
    """Two horizontal fold creases: a soft dark band with a thin bright line in the middle, like a letter folded in three."""
    height = page.shape[0]
    rows = np.arange(height, dtype=np.float32)
    shade = np.ones(height, dtype=np.float32)
    for centre in (height / 3 + random.uniform(-25, 25), 2 * height / 3 + random.uniform(-25, 25)):
        distance = rows - centre
        shade *= 1 - 0.22 * np.exp(-((distance / 14) ** 2))  # broad shadow next to the crease
        shade += 0.10 * np.exp(-((distance / 1.5) ** 2))  # sharp highlight on the crease itself
    return np.clip(page * shade[:, None, None], 0, 255).astype(np.uint8)


def toner_specks(page: np.ndarray) -> np.ndarray:
    """Scatter small grey specks over the page, like toner dust on a worn copier glass."""
    height, width = page.shape[:2]
    count = int(height * width * random.uniform(0.0006, 0.0012))
    ys = np.random.randint(0, height, count)
    xs = np.random.randint(0, width, count)
    grey = np.random.randint(60, 190, count).astype(np.uint8)
    result = page.copy()
    result[ys, xs] = grey[:, None]
    # Grow each speck from one pixel to roughly a 2x2 blob by smearing it right and down.
    result = np.minimum(result, np.roll(np.roll(result, 1, axis=0), 1, axis=1))
    return result


def roller_streaks(page: np.ndarray) -> np.ndarray:
    """Faint vertical grey streaks, as left by a dirty feed roller."""
    width = page.shape[1]
    columns = np.arange(width, dtype=np.float32)
    shade = np.ones(width, dtype=np.float32)
    for _ in range(random.randint(3, 6)):
        centre = random.uniform(0, width)
        strength = random.uniform(0.04, 0.12)
        shade *= 1 - strength * np.exp(-(((columns - centre) / random.uniform(1.5, 5)) ** 2))
    return np.clip(page * shade[None, :, None], 0, 255).astype(np.uint8)


def add_stamp(page: np.ndarray) -> np.ndarray:
    """Press a rotated, semi-transparent 'RECEIVED' stamp somewhere on the page, as an accounts clerk would."""
    height, width = page.shape[:2]
    layer = Image.new("RGBA", (width, height), (0, 0, 0, 0))  # transparent canvas the size of the page
    draw = ImageDraw.Draw(layer)
    font = ImageFont.load_default(size=max(36, width // 18))

    box_w, box_h = width // 3, height // 12
    left = random.randint(width // 20, width - box_w - width // 20)
    top = random.randint(height // 3, height - box_h - height // 10)
    colour = (170, 30, 40, 150)  # dark red, 60 % opaque
    draw.rounded_rectangle((left, top, left + box_w, top + box_h), radius=12, outline=colour, width=6)
    draw.text((left + box_w // 2, top + box_h // 2), "RECEIVED", font=font, fill=colour, anchor="mm")

    layer = layer.rotate(random.uniform(-18, 18), center=(left + box_w // 2, top + box_h // 2))
    base = Image.fromarray(cv2.cvtColor(page, cv2.COLOR_BGR2RGB)).convert("RGBA")
    merged = Image.alpha_composite(base, layer).convert("RGB")
    return cv2.cvtColor(np.array(merged), cv2.COLOR_RGB2BGR)


def photo(page: np.ndarray) -> np.ndarray:
    """A phone photo of a page lying on a table: perspective, uneven light, blur, sensor noise."""
    height, width = page.shape[:2]
    canvas_w, canvas_h = int(width * 1.25), int(height * 1.15)

    # Where the four page corners land in the photo: the page shrinks and tilts a little.
    margin_x, margin_y = (canvas_w - width) / 2, (canvas_h - height) / 2
    jitter = lambda limit: random.uniform(-limit, limit)  # noqa: E731
    source = np.float32([[0, 0], [width, 0], [width, height], [0, height]])
    target = np.float32([
        [margin_x + jitter(60), margin_y + jitter(50)],
        [margin_x + width + jitter(60), margin_y + jitter(50)],
        [margin_x + width + jitter(60), margin_y + height + jitter(50)],
        [margin_x + jitter(60), margin_y + height + jitter(50)],
    ])

    # A brown wooden "table" behind the page, so the page edges are visible. OpenCV colours are blue, green, red.
    table = np.full((canvas_h, canvas_w, 3), (random.randint(45, 75), random.randint(80, 110), random.randint(120, 160)), np.uint8)
    matrix = cv2.getPerspectiveTransform(source, target)
    warped = cv2.warpPerspective(page, matrix, (canvas_w, canvas_h), borderValue=(0, 0, 0))
    mask = cv2.warpPerspective(np.full((height, width), 255, np.uint8), matrix, (canvas_w, canvas_h))
    image = np.where(mask[..., None] > 0, warped, table).astype(np.float32)

    # Uneven lighting: a brightness ramp in a random direction.
    angle = random.uniform(0, 2 * np.pi)
    ys, xs = np.mgrid[0:canvas_h, 0:canvas_w]
    ramp = (xs * np.cos(angle) + ys * np.sin(angle)) / max(canvas_w, canvas_h)
    image *= (1.0 + random.uniform(0.15, 0.3) * (ramp - ramp.mean()))[..., None]

    image = cv2.GaussianBlur(image, (0, 0), sigmaX=random.uniform(1.2, 2.4))  # focus blur
    image += np.random.normal(0, random.uniform(3, 7), image.shape)  # sensor noise
    return np.clip(image, 0, 255).astype(np.uint8)


EFFECTS = {"scan_light": scan_light, "scan_heavy": scan_heavy, "photo": photo}
