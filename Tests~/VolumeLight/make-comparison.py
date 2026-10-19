"""Create the diagnostic comparison from the GPU suite's PNG previews (Pillow)."""
import argparse
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument("results", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
font_path = Path("C:/Windows/Fonts/msyh.ttc")
font = ImageFont.truetype(str(font_path), 20) if font_path.exists() else ImageFont.load_default()
small = ImageFont.truetype(str(font_path), 15) if font_path.exists() else font
canvas = Image.new("RGB", (1000, 430), (22, 24, 28))
draw = ImageDraw.Draw(canvas)
draw.text((20, 12), "太阳 HD：细窗格采样对照（同一阴影图与曝光）", font=font, fill="white")
entries = [
    ("aa-legacy-256.png", "旧实现 · 256 步"),
    ("aa-temporal-48.png", "有效区间＋时域 · 48 步"),
    ("aa-reference-4096.png", "参考 · 4096 步"),
]
for i, (file, label) in enumerate(entries):
    x = 20 + 330 * i
    image = Image.open(args.results / file).convert("RGB").resize((300, 300), Image.Resampling.NEAREST)
    canvas.paste(image, (x, 58))
    draw.text((x, 368), label, font=font, fill="white")
draw.text((20, 405), "隔离 GPU 回归场景；时域为 32 次渲染后的结果；不是用户教堂场景截图。", font=small, fill=(188, 195, 205))
args.output.parent.mkdir(parents=True, exist_ok=True)
canvas.save(args.output)
print(args.output)
