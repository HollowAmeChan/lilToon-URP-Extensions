"""Small output presets. They never change the scene's lighting or effects."""

QUALITY_SHORT_EDGES = {"draft": 360, "preview": 720, "review": 1080, "high": 2160}
ASPECT_RATIOS = {
    "landscape": (16, 9),
    "portrait": (9, 16),
    "4:3": (4, 3),
    "square": (1, 1),
}


def resolve_output(quality="draft", aspect="portrait", fps=30, width=None, height=None):
    if quality not in QUALITY_SHORT_EDGES or aspect not in ASPECT_RATIOS:
        raise ValueError("Unknown output quality or aspect.")
    if fps not in (30, 60):
        raise ValueError("Choose 30 or 60 fps.")
    if (width is None) != (height is None):
        raise ValueError("Custom output requires both --width and --height.")
    if width is None:
        x, y = ASPECT_RATIOS[aspect]
        edge = QUALITY_SHORT_EDGES[quality]
        width, height = edge * x // min(x, y), edge * y // min(x, y)
    elif not (16 <= width <= 16384 and 16 <= height <= 16384 and width % 2 == height % 2 == 0):
        raise ValueError("Custom video dimensions must be even and between 16 and 16384.")
    else:
        quality, aspect = "custom", "custom"
    return dict(qualityPreset=quality, aspectPreset=aspect, width=width, height=height, fps=fps)


def list_presets():
    return {
        quality: {aspect: resolve_output(quality, aspect) for aspect in ASPECT_RATIOS}
        for quality in QUALITY_SHORT_EDGES
    }
