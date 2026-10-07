"""Convert between RGB normal maps and GuidaSharedCode's RGBA twist maps.

Run from the ReverieMod directory (requires Pillow):
    python tools/normal_twist.py to-twist Content/KingSlime/KingSlimeBodyNormal.png Content/KingSlime/KingSlimeBodyTwist.png --invert
    python tools/normal_twist.py to-normal Content/KingSlime/KingSlimeBodyTwist.png normal_roundtrip.png --mask Content/KingSlime/KingSlimeBody.png --invert

The twist shader inverts RGBA, then calculates screen offsets as (G-R, A-B)
from the source texture. White is neutral. A is a direction channel, so twist
maps cannot store the normal map's transparency; pass --mask when converting
back to restore it. Normal Z is reconstructed from X and Y. Use --invert for
convex magnification with the existing screen shader and RGB channel offsets.
"""

import argparse
import math
from pathlib import Path
from typing import Optional

from PIL import Image


def normal_component(channel: int) -> float:
    # Treat 128 as exactly flat while retaining the full range at 0 and 255.
    return (channel - 128) / (127 if channel >= 128 else 128)


def encode_normal_component(value: float) -> int:
    return round(128 + value * (127 if value >= 0 else 128))


def normal_to_twist(source: Image.Image, invert: bool = False) -> Image.Image:
    pixels = []
    for red, green, _, alpha in source.convert("RGBA").getdata():
        coverage = alpha / 255
        x = normal_component(red) * coverage
        y = normal_component(green) * coverage
        if invert:
            x = -x
            y = -y
        pixels.append((
            255 - round(max(x, 0) * 255),
            255 - round(max(-x, 0) * 255),
            255 - round(max(y, 0) * 255),
            255 - round(max(-y, 0) * 255),
        ))
    result = Image.new("RGBA", source.size)
    result.putdata(pixels)
    return result


def twist_to_normal(source: Image.Image, mask: Optional[Image.Image],
                    invert: bool = False) -> Image.Image:
    if mask is not None and mask.size != source.size:
        raise ValueError("Mask and twist map must have the same dimensions")
    if mask is None:
        alphas = [255] * (source.width * source.height)
    elif "A" in mask.getbands():
        alphas = list(mask.getchannel("A").getdata())
    else:
        alphas = list(mask.convert("L").getdata())

    pixels = []
    for (red, green, blue, alpha), opacity in zip(
        source.convert("RGBA").getdata(), alphas
    ):
        if opacity == 0:
            pixels.append((128, 128, 255, 0))
            continue
        coverage = opacity / 255
        x = (green - red) / (255 * coverage)
        y = (alpha - blue) / (255 * coverage)
        if invert:
            x = -x
            y = -y
        length = math.hypot(x, y)
        if length > 1:
            x /= length
            y /= length
        z = math.sqrt(max(0, 1 - x * x - y * y))
        pixels.append((
            encode_normal_component(x),
            encode_normal_component(y),
            encode_normal_component(z),
            opacity,
        ))
    result = Image.new("RGBA", source.size)
    result.putdata(pixels)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("to-twist", "to-normal"))
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--mask", type=Path,
                        help="Original alpha mask for to-normal; omit for opaque output")
    parser.add_argument("--invert", action="store_true",
                        help="Reverse displacement direction for convex magnification")
    args = parser.parse_args()
    if args.source.resolve() == args.destination.resolve():
        parser.error("Source and destination must differ")
    if args.mode == "to-twist" and args.mask is not None:
        parser.error("--mask applies only to to-normal")

    with Image.open(args.source) as source:
        if args.mode == "to-twist":
            result = normal_to_twist(source, args.invert)
        elif args.mask is None:
            result = twist_to_normal(source, None, args.invert)
        else:
            with Image.open(args.mask) as mask:
                result = twist_to_normal(source, mask, args.invert)
    args.destination.parent.mkdir(parents=True, exist_ok=True)
    result.save(args.destination)


if __name__ == "__main__":
    main()
