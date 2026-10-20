"""Build a native-pixel crop comparison from GPU validation output (no exposure gain)."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument("render_folder", type=Path)
parser.add_argument("report", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
report = json.loads(args.report.read_text(encoding="utf-8-sig"))["dust"]
canvas = Image.new("RGB", (1096, 720), "#171c24")
draw = ImageDraw.Draw(canvas)
font_path = "C:/Windows/Fonts/segoeui.ttf"
font = ImageFont.truetype(font_path, 20)
small = ImageFont.truetype(font_path, 15)
draw.text((24, 16), "Clustered dust / point quads", fill="white", font=font)
draw.text((24, 48), "RTX 4060 | D3D12 | 1920 x 1080 | solar shadows | native 512 x 512 centre crops", fill="#b4c3d8", font=small)
for x, stage, title, ms, pixels in [
    (24, 0, "64 clustered cards", report["clustersGpuMs"], report["benchmarkClusterPixels"]),
    (560, 2, "1,000,000 nearby point quads", report["millionLocalGpuMs"], report["benchmarkLocalPixels"]),
]:
    image = Image.open(args.render_folder / f"dust-benchmark-{stage}.png").convert("RGB")
    width, height = image.size
    crop = image.crop((width//2-256, height//2-256, width//2+256, height//2+256))
    canvas.paste(crop, (x, 112))
    draw.text((x, 80), title, fill="white", font=font)
    draw.text((x, 640), f"Dust draw: {ms:.3f} ms | changed pixels: {pixels:,}", fill="#d2e3f8", font=small)
draw.text((24, 681), "Same brightness/medium. Different representations and coverage; not an equal-image speed benchmark.", fill="#a4b1c4", font=small)
args.output.parent.mkdir(parents=True, exist_ok=True)
canvas.save(args.output)
