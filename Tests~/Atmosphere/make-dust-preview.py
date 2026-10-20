"""Create a labelled contact sheet from the dust GPU suite's sRGB PNG previews."""
from pathlib import Path
import argparse
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument("results", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 19)
small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 14)
sheet = Image.new("RGB", (1000, 435), (22, 24, 28))
draw = ImageDraw.Draw(sheet)
draw.text((20, 12), "共享大气尘粒 · GPU 验证预览", font=font, fill="white")
entries = [("dust-on.png", "雾与尘粒"), ("dust-contribution.png", "独立颗粒贡献"), ("dust-liltoon-cutout.png", "lilToon Cutout 孔洞遮挡")]
for index, (file, label) in enumerate(entries):
    x = 20 + index * 330
    image = Image.open(args.results / file).convert("RGB").resize((300, 300), Image.Resampling.NEAREST)
    sheet.paste(image, (x, 55)); draw.text((x, 368), label, font=font, fill="white")
draw.text((20, 407), "隔离回归场景；此图为便于检查而放大粒径，实际默认粒径更小；PNG 为压亮度后 sRGB 预览。", font=small, fill=(188, 195, 205))
args.output.parent.mkdir(parents=True, exist_ok=True)
sheet.save(args.output)
print(args.output)
