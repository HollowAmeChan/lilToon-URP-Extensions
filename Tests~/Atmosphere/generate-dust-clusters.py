"""Bake a repeatable clustered dust alpha tile. Requires numpy and Pillow."""
from pathlib import Path
import argparse
import numpy as np
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("output", type=Path)
args = parser.parse_args()
rng = np.random.default_rng(193741)
size = 512
image = np.zeros((size, size), np.float32)
centres = rng.uniform(0, size, (48, 2))
for index in range(8000):
    if index % 4:
        centre = centres[rng.integers(len(centres))] + rng.normal(0, 38, 2)
    else:
        centre = rng.uniform(0, size, 2)
    x, y = centre % size
    sigma = rng.uniform(.42, .9)
    amplitude = rng.uniform(.3, 1)
    for oy in range(-3, 4):
        for ox in range(-3, 4):
            px, py = int(x) + ox, int(y) + oy
            value = amplitude * np.exp(-((px + .5 - x)**2 + (py + .5 - y)**2) / (2 * sigma**2))
            image[py % size, px % size] = max(image[py % size, px % size], value)
alpha = np.uint8(np.clip(image * 255, 0, 255))
rgba = np.full((size, size, 4), 255, dtype=np.uint8)
rgba[..., 3] = alpha
args.output.parent.mkdir(parents=True, exist_ok=True)
Image.fromarray(rgba).save(args.output)
print(args.output, 'alpha coverage:', float((alpha > 8).mean()))
