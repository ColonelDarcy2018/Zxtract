"""Encode WPF --docs animation-frames as a README GIF (requires Pillow).

Usage: python tools/Build-ReadmeMotion.py <animation-frames> <output.gif>
Frames are a controlled replay from the production renderer, not timing data.
"""

import argparse
from pathlib import Path

from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("frames", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    sources = sorted(args.frames.glob("[0-9][0-9][0-9].png"))
    if len(sources) != 49:
        parser.error(f"Expected 49 WPF phase frames, found {len(sources)}")
    # One shared palette keeps the stationary landscape stable between frames.
    with Image.open(sources[-1]) as last:
        palette = last.convert("RGB").quantize(colors=256)
    frames = []
    for source in sources:
        with Image.open(source) as image:
            frames.append(image.convert("RGB").quantize(palette=palette, dither=Image.Dither.NONE))
    durations = [60] * len(frames)
    durations[0] = 650
    durations[-1] = 1500
    args.output.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(args.output, save_all=True, append_images=frames[1:],
                   duration=durations, loop=0, disposal=1, optimize=False)
    with Image.open(args.output) as output:
        # GIF encoders merge identical quantized endpoint frames and add their
        # durations; verify motion and total timing instead of an exact count.
        total_duration = sum(output.seek(i) or output.info.get("duration", 0)
                             for i in range(output.n_frames))
        if output.n_frames < 40 or output.size != (1024, 350) or total_duration != sum(durations):
            raise RuntimeError("GIF frame count or dimensions differ from WPF replay")
        print(f"Verified {output.n_frames} frames, {output.size}: {args.output}")


if __name__ == "__main__":
    main()
