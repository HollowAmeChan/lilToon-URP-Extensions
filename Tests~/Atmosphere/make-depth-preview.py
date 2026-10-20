"""Compare two thickness settings using native GPU pixels, with no exposure gain."""
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
canvas = Image.new("RGB", (1096, 716), "#171c24")
draw = ImageDraw.Draw(canvas)
font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 20)
small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 15)
draw.text((24, 16), "Dust depth coverage | 200 metres | 128 cards", fill="white", font=font)
draw.text((24, 48), "Same density and brightness | native 512 x 512 crops | no exposure gain", fill="#b4c3d8", font=small)
for x, name, title in [(24, "dust-depth-thin", "Space thickness: 0.5"), (560, "dust-depth-thick", "Space thickness: 3")]:
    image = Image.open(args.render_folder / (name + ".png")).convert("RGB")
    w, h = image.size
    canvas.paste(image.crop((w//2-256, h//2-256, w//2+256, h//2+256)), (x, 112))
    draw.text((x, 80), title, fill="white", font=font)
draw.text((24, 641), "Near / middle / far card counts: " + " / ".join(str(report[key]) for key in
    ["deepNearClusters", "deepMiddleClusters", "deepFarClusters"]), fill="#d2e3f8", font=small)
draw.text((24, 677), f'Mean contribution ratio: {report["thicknessRatio"]:.2f}x | thickness increases pixel overlap and GPU cost.', fill="#a4b1c4", font=small)
args.output.parent.mkdir(parents=True, exist_ok=True)
canvas.save(args.output)
