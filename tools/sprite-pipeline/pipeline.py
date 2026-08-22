"""Sprite normalization pipeline v0 for BrawlerAGD character expansion.

Stages: ingest -> content-crop -> scale-normalize -> palette-quantize -> atlas-pack -> v2 JSON.
This version also drives the DCSS vs LPC coherence test.
"""
import json
import os
from pathlib import Path
from PIL import Image

RAW = Path("/home/claude/sprite-work/raw")
OUT = Path("/home/claude/sprite-work/out")
OUT.mkdir(exist_ok=True)

DCSS = RAW / "dcss-tiles/releases/Nov-2015"
LPC = RAW / "lpc-gen/spritesheets"

TARGET_BODY_HEIGHT = 28  # px a standard biped occupies inside a 32px cell (DCSS convention)


def content_bbox(im):
    return im.convert("RGBA").getchannel("A").getbbox()


def load_rgba(path):
    return Image.open(path).convert("RGBA")


def crop_to_content(im):
    box = content_bbox(im)
    return im.crop(box) if box else im


def scale_to_height(im, h):
    if im.height == 0:
        return im
    f = h / im.height
    return im.resize((max(1, round(im.width * f)), h), Image.NEAREST)


def build_master_palette(images, colors=64):
    """Adaptive palette from a pile of RGBA images (alpha>128 pixels only)."""
    strip_px = []
    for im in images:
        rgba = im.convert("RGBA")
        for r, g, b, a in rgba.getdata():
            if a > 128:
                strip_px.append((r, g, b))
    strip = Image.new("RGB", (len(strip_px), 1))
    strip.putdata(strip_px)
    pal_img = strip.quantize(colors=colors, method=Image.MEDIANCUT)
    return pal_img


def quantize_to(im, pal_img):
    rgba = im.convert("RGBA")
    alpha = rgba.getchannel("A")
    rgb = rgba.convert("RGB")
    q = rgb.quantize(palette=pal_img, dither=Image.NONE).convert("RGB")
    out = q.convert("RGBA")
    out.putalpha(alpha)
    return out


def lpc_compose(parts, frame=(0, 2)):
    """Alpha-composite full LPC sheets, then crop one 64x64 frame (col,row) from walk."""
    base = None
    for p in parts:
        layer = load_rgba(p)
        if base is None:
            base = Image.new("RGBA", layer.size, (0, 0, 0, 0))
        if layer.size != base.size:
            layer = layer.crop((0, 0, base.width, base.height))
        base.alpha_composite(layer)
    c, r = frame
    return base.crop((c * 64, r * 64, (c + 1) * 64, (r + 1) * 64))


def pack_atlas(sprites, cell=(48, 48), cols=16, gutter=1):
    """sprites: list of (id, RGBA). Bottom-center anchor inside cell. Returns atlas, entries."""
    cw, ch = cell
    rows = (len(sprites) + cols - 1) // cols
    atlas = Image.new("RGBA", (cols * (cw + gutter) + gutter, rows * (ch + gutter) + gutter), (0, 0, 0, 0))
    entries = []
    for i, (sid, im) in enumerate(sprites):
        col, row = i % cols, i // cols
        x0 = gutter + col * (cw + gutter)
        y0 = gutter + row * (ch + gutter)
        px = x0 + (cw - im.width) // 2
        py = y0 + (ch - im.height)
        atlas.alpha_composite(im, (px, py))
        entries.append({
            "id": sid,
            "rect": [px, py, im.width, im.height],
            "pivot": [px + im.width // 2, py + im.height],
            "tags": [],
        })
    return atlas, entries
