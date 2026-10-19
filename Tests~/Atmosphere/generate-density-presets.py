"""Generate periodic scalar density fields for the bundled Texture3D assets.

Requires numpy and Pillow. Run with an output directory, then use
HoAtmosphereDensityPresetBake.Run in an isolated Unity project to bake .asset files.
Fields are sampled at voxel centres on a periodic domain; no image generator or
third-party texture assets are used. The PNG is a XY centre slice, not a render.
"""
from pathlib import Path
import argparse
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
n = 64
z, y, x = np.meshgrid(*([((np.arange(n, dtype=np.float32) + .5) / n)] * 3), indexing="ij")
p = np.stack([x, y, z], axis=-1)


def hash3(x, y, z, seed):
    with np.errstate(over="ignore"):
        h = x.astype(np.uint32) * np.uint32(0x8DA6B343)
        h ^= y.astype(np.uint32) * np.uint32(0xD8163841)
        h ^= z.astype(np.uint32) * np.uint32(0xCB1AB31F)
        h ^= np.uint32(seed)
        h ^= h >> 16
        h *= np.uint32(0x7FEB352D)
        h ^= h >> 15
        h *= np.uint32(0x846CA68B)
        h ^= h >> 16
    return (h >> 8).astype(np.float32) / 16777215


def noise(point, frequency, seed):
    frequency = np.array(frequency, dtype=np.int32)
    q = point * frequency
    base = np.floor(q).astype(np.int32)
    f = q - base
    f = f * f * f * (f * (f * 6 - 15) + 10)
    result = np.zeros(point.shape[:-1], dtype=np.float32)
    for iz in range(2):
        for iy in range(2):
            for ix in range(2):
                corner = (base + [ix, iy, iz]) % frequency
                weight = (f[..., 0] if ix else 1 - f[..., 0]) * (f[..., 1] if iy else 1 - f[..., 1]) * (f[..., 2] if iz else 1 - f[..., 2])
                result += hash3(corner[..., 0], corner[..., 1], corner[..., 2], seed) * weight
    return result


def fbm(point, frequency, seed, octaves=3):
    frequency = np.array(frequency)
    value = np.zeros(point.shape[:-1], dtype=np.float32)
    total = 0
    for octave in range(octaves):
        weight = .5**octave
        value += noise(point, frequency * 2**octave, seed + octave * 113) * weight
        total += weight
    return value / total


def normalized(value, deviation):
    value = (value - value.mean()) / max(1e-8, value.std()) * deviation
    lo, hi = -1., 1.
    for _ in range(32):
        offset = (lo + hi) / 2
        if np.clip(value + offset, 0, 1).mean() < .5:
            lo = offset
        else:
            hi = offset
    return np.round(np.clip(value + (lo + hi) / 2, 0, 1) * 255).astype(np.uint8)


warp = np.stack([fbm(p, [2, 2, 2], 3301 + axis * 19, 2) for axis in range(3)], axis=-1) - .5
fields = [
    ("HoAtmosphereFineDust64", "细密灰尘", fbm(p, [8, 8, 8], 1709), .16),
    ("HoAtmosphereSoftFog64", "柔和雾团", fbm(p, [2, 2, 2], 2017), .21),
    ("HoAtmosphereWisps64", "丝状流雾", fbm(p + warp * .35, [2, 12, 6], 4091, 2), .23),
    ("HoAtmospherePatchyFog64", "斑驳浓雾", fbm(p + warp * .18, [3, 3, 3], 5501, 3), .37),
]
font_file = Path("C:/Windows/Fonts/msyh.ttc")
font = ImageFont.truetype(str(font_file), 20) if font_file.exists() else ImageFont.load_default()
sheet = Image.new("RGB", (1060, 335), (22, 24, 28))
draw = ImageDraw.Draw(sheet)
draw.text((18, 10), "大气密度 Texture3D 预设 · XY 中央切片", font=font, fill="white")
stats = {}
for index, (name, label, field, deviation) in enumerate(fields):
    data = normalized(field, deviation)
    (args.output / (name + ".raw")).write_bytes(data.tobytes())
    image = Image.fromarray(data[n // 2]).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    image.save(args.output / (name + "-slice.png"))
    sheet.paste(image.resize((244, 244), Image.Resampling.BILINEAR).convert("RGB"), (18 + 260 * index, 46))
    draw.text((18 + 260 * index, 302), label, font=font, fill="white")
    stats[name] = {"size": n, "format": "R8_UNorm", "min": float(data.min() / 255), "max": float(data.max() / 255), "mean": float(data.mean() / 255), "std": float(data.std() / 255)}
(args.output / "stats.json").write_text(json.dumps(stats, indent=2) + "\n", encoding="utf-8")
sheet.save(args.output / "Atmosphere-Density-Presets.png")
print(json.dumps(stats, indent=2))
