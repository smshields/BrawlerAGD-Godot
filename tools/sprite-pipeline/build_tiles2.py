"""Stage tile library build, pass 2 (tiles_v2.1).

Fixes from review: (1) per-cell VARIANT fill instead of one repeated block,
(2) real floor-tile surface caps + sprite-style dark outline instead of thin
programmatic lips, (3) ~2x theme breadth, (4) per-theme decorative PROPS
(trees, statues, plants, columns, altars) that sit on platform tops.

Contract v2.1: every piece key maps to a LIST of rects (renderer picks
hash(cx, cy, platformIndex) % n). New per-theme "props" list.
"""
import sys, json, random, colorsys, math
sys.path.insert(0, "/home/claude/sprite-work")
from pipeline import *

random.seed(2026)
DN = DCSS / "dngn"
STAGE_TRAITS = ["vast","cramped","towering","sprawling","deadly","forgiving",
                "shattered","barren","twin","shifting","sanctuary"]
MAX_VARIANTS = 4

# prop pools (paths under DCSS root); themes reference by key
PROPS = {
    "tree_red":    ["dngn/trees/tree1_red.png", "dngn/trees/tree2_red.png"],
    "tree_yellow": ["dngn/trees/tree1_yellow.png", "dngn/trees/tree2_yellow.png"],
    "mangrove":    ["dngn/trees/mangrove1.png", "dngn/trees/mangrove2.png"],
    "bush":        ["mon/fungi_plants/bush2.png", "mon/fungi_plants/bush3.png"],
    "mushroom":    ["mon/fungi_plants/wandering_mushroom.png", "mon/fungi_plants/deathcap.png"],
    "briar":       ["mon/fungi_plants/briar_patch.png"],
    "crypt_plant": ["mon/fungi_plants/plant_crypt.png"],
    "demon_plant": ["mon/fungi_plants/plant_demonic.png"],
    "column":      ["dngn/statues/crumbled_column_1.png", "dngn/statues/crumbled_column_2.png",
                    "dngn/statues/crumbled_column_3.png"],
    "statue_hero": ["dngn/statues/statue_ancient_hero.png", "dngn/statues/statue_sword.png"],
    "statue_evil": ["dngn/statues/statue_ancient_evil.png", "dngn/statues/statue_wraith.png",
                    "dngn/statues/statue_demonic_bust.png"],
    "statue_beast":["dngn/statues/statue_dragon.png", "dngn/statues/statue_hydra.png",
                    "dngn/statues/statue_elephant.png"],
    "statue_odd":  ["dngn/statues/statue_snail.png", "dngn/statues/statue_cat.png",
                    "dngn/statues/statue_princess.png", "dngn/statues/statue_twins.png"],
    "statue_tech": ["dngn/statues/statue_orb_guardian.png", "dngn/statues/statue_iron.png",
                    "dngn/statues/statue_orb.png"],
    "statue_holy": ["dngn/statues/statue_angel.png", "dngn/altars/elyvilon.png"],
    "idol":        ["dngn/statues/orcish_idol.png"],
    "altar_dark":  ["dngn/altars/kiku.png", "dngn/altars/yredelemnul.png"],
    "altar_wild":  ["dngn/altars/fedhas.png", "dngn/altars/cheibriados.png"],
}

# (name, kind, family_prefix, cap_floor_prefix or None, registers, traits, vibe, prop keys)
# kind: "wall" or "floor" (floor themes use the floor tile as the block itself)
THEMES = [
    # fantasy
    ("brick",      "wall", "brick_brown",   "pebble_brown",  ["fantasy"],          {"towering":0.5}, "neutral", ["column", "statue_hero"]),
    ("brick_gray", "wall", "brick_gray",    "rect_gray",     ["fantasy"],          {"towering":0.5,"cramped":0.3}, "neutral", ["column"]),
    ("vines",      "wall", "brick_brown-vines", "moss",      ["fantasy","normal"], {"sprawling":0.6,"shifting":0.3}, "neutral", ["bush", "tree_yellow"]),
    ("stone",      "wall", "stone_gray",    "pebble_brown",  ["fantasy","normal"], {"towering":0.4}, "neutral", ["column", "statue_hero"]),
    ("stone_dark", "wall", "stone2_dark",   "black_cobalt",  ["fantasy","horror"], {"towering":0.5,"deadly":0.3}, "menacing", ["statue_evil"]),
    ("stone_brown","wall", "stone2_brown",  "grey_dirt",     ["fantasy"],          {"sprawling":0.4}, "neutral", ["bush"]),
    ("runed",      "wall", "stone_black_marked", "etched",   ["fantasy"],          {"twin":0.5,"vast":0.3}, "regal", ["statue_hero"]),
    ("relief",     "wall", "relief",        "limestone",     ["fantasy"],          {"sanctuary":0.4,"towering":0.4}, "regal", ["statue_hero", "column"]),
    ("sandstone",  "wall", "sandstone_wall", "sandstone_floor", ["fantasy"],       {"barren":0.7,"sprawling":0.4}, "neutral", ["column"]),
    ("marble",     "wall", "marble_wall",   "marble_floor",  ["fantasy"],          {"sanctuary":0.7,"forgiving":0.5}, "regal", ["statue_holy", "column"]),
    ("church",     "wall", "church",        "limestone",     ["fantasy"],          {"sanctuary":0.9,"forgiving":0.5}, "regal", ["statue_holy"]),
    ("lair",       "wall", "lair",          "lair",          ["fantasy","normal"], {"sprawling":0.5}, "neutral", ["bush", "mushroom"]),
    ("emerald",    "wall", "emerald",       "self", ["fantasy"],          {"twin":0.4,"vast":0.4}, "regal", ["statue_beast"]),
    ("shoals",     "wall", "shoals_wall",   "sand",          ["fantasy","normal"], {"sprawling":0.5,"shifting":0.4}, "neutral", ["mangrove"]),
    ("orc",        "wall", "orc",           "orc",           ["fantasy"],          {"cramped":0.5}, "menacing", ["idol"]),
    ("snake",      "wall", "snake",         "snake-a",       ["fantasy"],          {"shifting":0.5,"cramped":0.3}, "menacing", ["statue_beast", "altar_wild"]),
    ("vault",      "wall", "vault",         "rect_gray",     ["fantasy","scifi"],  {"twin":0.4,"cramped":0.4}, "neutral", ["statue_tech"]),
    ("pebble",     "wall", "pebble_red",    "grey_dirt",     ["fantasy"],          {"barren":0.4}, "neutral", ["column"]),
    # horror
    ("catacombs",  "wall", "catacombs",     "crypt",         ["horror"],           {"shattered":0.7,"deadly":0.4}, "menacing", ["statue_evil", "crypt_plant"]),
    ("tomb",       "wall", "tomb",          "tomb",          ["horror"],           {"barren":0.5,"cramped":0.5}, "menacing", ["statue_evil"]),
    ("undead",     "wall", "undead",        "crypt",         ["horror"],           {"deadly":0.5,"shattered":0.5}, "menacing", ["crypt_plant", "altar_dark"]),
    ("bone",       "wall", "undead_brown",  "grey_dirt",     ["horror"],           {"shattered":0.6,"barren":0.4}, "menacing", ["crypt_plant"]),
    ("hell",       "wall", "hell0",         "infernal",      ["horror"],           {"deadly":0.9}, "menacing", ["demon_plant", "altar_dark"]),
    ("volcanic",   "wall", "volcanic_wall", "volcanic_floor",["horror","fantasy"], {"deadly":0.8,"shattered":0.4}, "menacing", ["demon_plant"]),
    ("flesh",      "wall", "wall_flesh",    "floor_nerves",  ["horror"],           {"shifting":0.6,"deadly":0.4}, "gross", ["demon_plant"]),
    ("slime",      "wall", "slime0",        "bog_green",     ["horror","fantasy"], {"shifting":0.8,"forgiving":0.3}, "gross", ["mushroom"]),
    ("slime_stone","wall", "slime_stone",   "bog_green",     ["horror"],           {"shifting":0.6}, "gross", ["mushroom", "briar"]),
    ("abyss",      "wall", "abyss/abyss",   "black_cobalt",  ["horror"],           {"vast":0.6,"shifting":0.7}, "menacing", ["statue_evil"]),
    ("bloodcobble","floor", "cobble_blood", None,            ["horror"],           {"deadly":0.6,"shattered":0.4}, "gross", ["statue_evil"]),
    ("demonic",    "floor", "demonic_red",  None,            ["horror"],           {"deadly":0.7}, "menacing", ["demon_plant", "altar_dark"]),
    # scifi
    ("metal",      "wall", "metal_wall",    "mesh",          ["scifi"],            {"cramped":0.4,"towering":0.4}, "neutral", ["statue_tech"]),
    ("metal_white","wall", "metal_wall_white", "mesh",       ["scifi"],            {"twin":0.4,"sanctuary":0.3}, "neutral", ["statue_tech"]),
    ("lab",        "wall", "lab-metal",     "mesh",          ["scifi"],            {"twin":0.5,"cramped":0.4}, "neutral", ["statue_tech"]),
    ("lab_rock",   "wall", "lab-rock",      "grey_dirt",     ["scifi"],            {"barren":0.4,"cramped":0.4}, "neutral", ["column"]),
    ("lab_stone",  "wall", "lab-stone",     "rect_gray",     ["scifi"],            {"twin":0.4}, "neutral", ["statue_tech"]),
    ("silver",     "wall", "silver_wall",   "mesh",          ["scifi","fantasy"],  {"sanctuary":0.4,"twin":0.4}, "regal", ["statue_tech"]),
    ("mirror",     "wall", "mirrored_wall", "self", ["scifi"],            {"twin":0.9}, "regal", ["statue_odd"]),
    ("crystal_blue","wall","crystal_wall_blue", "self", ["scifi","fantasy"], {"vast":0.5,"twin":0.5}, "regal", ["statue_tech"]),
    ("crystal_red","wall", "crystal_wall_red", "self", ["scifi","horror"], {"deadly":0.5,"vast":0.4}, "menacing", ["statue_evil"]),
    ("crystal_green","wall","crystal_wall_green","self",["scifi","fantasy"],{"forgiving":0.4,"vast":0.4}, "regal", ["statue_beast"]),
    ("crystal_white","wall","crystal_wall_white","self",["scifi"],        {"sanctuary":0.5,"twin":0.4}, "regal", ["statue_holy"]),
    ("cobalt",     "wall", "cobalt_rock",   "black_cobalt",  ["scifi","fantasy"],  {"vast":0.6}, "neutral", ["column"]),
    ("cobalt_stone","wall","cobalt_stone",  "black_cobalt",  ["scifi"],            {"vast":0.5,"towering":0.4}, "neutral", ["statue_tech"]),
    ("zot",        "wall", "zot_blue",      "black_cobalt",  ["scifi","fantasy"],  {"vast":0.7,"towering":0.5}, "regal", ["statue_odd"]),
    ("permarock",  "wall", "permarock_red", "demonic_red",   ["scifi","horror"],   {"deadly":0.4,"vast":0.4}, "menacing", ["statue_evil"]),
    # normal / nature / goofy
    ("beehive",    "wall", "beehives",      "grey_dirt",     ["normal","fantasy"], {"cramped":0.6,"sprawling":0.3}, "goofy", ["bush", "mushroom"]),
    ("grass",      "floor", "grass/grass0", None,            ["normal","fantasy"], {"forgiving":0.7,"sprawling":0.5}, "cute", ["tree_yellow", "bush"]),
    ("flowers",    "floor", "grass/grass_flowers_blue", None, ["normal","fantasy"],{"forgiving":0.8,"sanctuary":0.3}, "cute", ["tree_red", "bush"]),
    ("dirt",       "floor", "dirt",         None,            ["normal"],           {"barren":0.6}, "neutral", ["bush", "column"]),
    ("sand",       "floor", "sand",         None,            ["normal"],           {"barren":0.8,"vast":0.4}, "neutral", ["column"]),
    ("ice",        "floor", "ice",          None,            ["normal","fantasy"], {"forgiving":0.3,"shifting":0.4}, "neutral", ["statue_odd"]),
    ("frozen",     "floor", "frozen",       None,            ["normal","horror"],  {"barren":0.5,"deadly":0.3}, "neutral", ["statue_odd"]),
    ("swamp",      "floor", "swamp",        None,            ["normal","horror"],  {"shifting":0.6,"forgiving":0.3}, "gross", ["mangrove", "mushroom"]),
    ("mud",        "floor", "mud",          None,            ["normal"],           {"shifting":0.5,"barren":0.3}, "gross", ["mangrove", "briar"]),
    ("moss",       "floor", "moss",         None,            ["normal","fantasy"], {"forgiving":0.5,"sprawling":0.4}, "cute", ["bush", "mushroom"]),
    ("mosaic",     "floor", "mosaic",       None,            ["fantasy"],          {"sanctuary":0.5,"twin":0.4}, "regal", ["statue_holy", "column"]),
    ("labyrinth",  "floor", "labyrinth",    None,            ["fantasy","scifi"],  {"cramped":0.7,"twin":0.4}, "neutral", ["statue_odd"]),
    ("white_marble","floor","white_marble", None,            ["fantasy"],          {"sanctuary":0.6}, "regal", ["statue_holy"]),
    ("limestone",  "floor", "limestone",    None,            ["normal","fantasy"], {"barren":0.4,"sprawling":0.3}, "neutral", ["column"]),
]

def find_variants(kind, prefix, cap=MAX_VARIANTS):
    base = DN / ("wall" if kind == "wall" else "floor")
    if "/" in prefix:
        sub, pre = prefix.rsplit("/", 1)
        base = base / sub
        prefix = pre
    out = []
    for p in sorted(base.glob(f"{prefix}*.png")):
        stem_rest = p.stem[len(prefix):]
        if stem_rest == "" or stem_rest.isdigit() or (stem_rest[0] in "0123456789" and stem_rest.isdigit()):
            out.append(p)
    return out[:cap]

def lum(c): return 0.299*c[0] + 0.587*c[1] + 0.114*c[2]

def theme_colors(im):
    px = [(r, g, b) for r, g, b, a in im.convert("RGBA").getdata() if a > 128]
    px.sort(key=lum)
    dark = px[max(0, len(px)//12)]
    shade = tuple(int(c*0.55) for c in dark)
    outline = tuple(min(60, int(c*0.35)) for c in dark)  # near-black, sprite-style
    return shade, outline

def make_piece(block, cap_strip, exp_l, exp_r, exp_t, exp_b, shade, outline):
    t = block.copy().convert("RGBA")
    w, h = t.size
    put = t.putpixel
    if exp_t and cap_strip is not None:
        t.alpha_composite(cap_strip, (0, 0))
    if exp_t:
        for x in range(w): put((x, 0), outline + (255,))
    if exp_b:
        for x in range(w):
            put((x, h-1), outline + (255,))
            put((x, h-2), shade + (255,))
    for side, exp in ((0, exp_l), (w-1, exp_r)):
        if exp:
            inner = 1 if side == 0 else w-2
            for y in range(h):
                put((side, y), outline + (255,))
                if y > (10 if exp_t else 2) and y < h-2:
                    put((inner, y), shade + (255,))
    return t

def mean_lum(im):
    px = [(r, g, b) for r, g, b, a in im.convert("RGBA").getdata() if a > 128]
    return sum(lum(c) for c in px) / max(1, len(px))

def make_cap_strip(floor_img, block_lum, height=10):
    """Surface strip from a floor tile, brightness-normalized to read as a LIT
    walking surface: scaled so its mean luminance is ~1.2x the wall block's,
    never darker than the wall. Bottom row shadowed to seat it."""
    strip = floor_img.resize((32, 32), Image.NEAREST).crop((0, 0, 32, height)).convert("RGBA")
    sl = mean_lum(strip)
    target = max(block_lum * 1.2, sl)
    f = min(2.2, target / max(1.0, sl))
    px = strip.load()
    for y in range(height):
        for x in range(32):
            r, g, b, a = px[x, y]
            if y == height - 1:
                px[x, y] = (int(r*f*0.55), int(g*f*0.55), int(b*f*0.55), 255)
            else:
                px[x, y] = (min(255, int(r*f)), min(255, int(g*f)), min(255, int(b*f)), 255)
    return strip

def make_drop(block, cap_strip, exp_l, exp_r, shade, outline, height=12):
    w = block.width
    slab = block.crop((0, 0, w, height)).convert("RGBA")
    if cap_strip is not None:
        slab.alpha_composite(cap_strip.crop((0, 0, w, min(height-2, cap_strip.height))), (0, 0))
    put = slab.putpixel
    for x in range(w):
        put((x, 0), outline + (255,))
        put((x, height-1), (0, 0, 0, 0))
        put((x, height-2), outline + (255,) if x % 8 < 5 else (0, 0, 0, 0))
    for y in range(height-1):
        if exp_l: put((0, y), outline + (255,))
        if exp_r: put((w-1, y), outline + (255,))
    return slab

H_STATES = [("L", True, False), ("M", False, False), ("R", False, True), ("S", True, True)]
V_STATES = [("T", True, False), ("M", False, False), ("B", False, True), ("S", True, True)]
VARIED_KEYS = {"MM", "TM", "TL", "TR", "TS", "BM"}   # keys that get per-variant versions

allmon = list((DCSS / "mon").rglob("*.png"))
pal = build_master_palette([load_rgba(p) for p in random.sample(allmon, 150)], colors=64)

themes_out, missing = [], []
for name, kind, prefix, cappre, regs, traits, vibe, propkeys in THEMES:
    variants = find_variants(kind, prefix)
    if not variants:
        missing.append((name, prefix)); continue
    blocks = [quantize_to(load_rgba(p).resize((32, 32), Image.NEAREST), pal) for p in variants]
    cap_strip = None
    if cappre == "self":
        cap_strip = make_cap_strip(blocks[0], mean_lum(blocks[0]))
    elif cappre:
        caps = find_variants("floor", cappre, cap=1)
        if caps:
            cap_strip = make_cap_strip(quantize_to(load_rgba(caps[0]), pal), mean_lum(blocks[0]))
    shade, outline = theme_colors(blocks[0])
    pieces = {}
    for hk, el, er in H_STATES:
        for vk, et, eb in V_STATES:
            key = vk + hk
            source_blocks = blocks if key in VARIED_KEYS else blocks[:1]
            pieces[key] = [make_piece(b, cap_strip, el, er, et, eb, shade, outline)
                           for b in source_blocks]
    drops = {}
    for hk, el, er in H_STATES:
        drops[hk] = [make_drop(blocks[0], cap_strip, el, er, shade, outline)]
    props = []
    for pk in propkeys:
        for rel in PROPS.get(pk, []):
            p = DCSS / rel
            if p.exists():
                props.append((pk, crop_to_content(quantize_to(load_rgba(p), pal))))
    themes_out.append(dict(name=name, register=regs, traitAffinity=traits, vibe=vibe,
                           source=f"dngn/{kind}/{prefix}*", license="CC0",
                           pieces=pieces, drops=drops, props=props))
if missing: print("MISSING:", missing)
print("themes:", len(themes_out))

# ---- pack ----
CW = 34
rows_px, cols_max = 0, 0
layout = []
for th in themes_out:
    cells = []
    for vk, _, _ in V_STATES:
        for hk, _, _ in H_STATES:
            for i, im in enumerate(th["pieces"][vk + hk]):
                cells.append(("t", vk + hk, i, im))
    for hk, _, _ in H_STATES:
        cells.append(("d", hk, 0, th["drops"][hk][0]))
    for i, (pk, im) in enumerate(th["props"]):
        cells.append(("p", pk, i, im))
    layout.append(cells)
    cols_max = max(cols_max, len(cells))
atlas = Image.new("RGBA", (cols_max * CW + 1, len(themes_out) * CW + 1), (0, 0, 0, 0))
out_themes = []
total_tiles = 0
for ti, (th, cells) in enumerate(zip(themes_out, layout)):
    entry = dict(name=th["name"], register=th["register"], traitAffinity=th["traitAffinity"],
                 vibe=th["vibe"], source=th["source"], license=th["license"],
                 tiles={}, dropTiles={}, props=[])
    for ci, (typ, key, i, im) in enumerate(cells):
        x, y = 1 + ci * CW, 1 + ti * CW
        atlas.alpha_composite(im, (x, y + (CW - 2 - im.height)))
        rect = [x, y + (CW - 2 - im.height), im.width, im.height]
        if typ == "t":
            entry["tiles"].setdefault(key, []).append(rect)
        elif typ == "d":
            entry["dropTiles"].setdefault(key, []).append(rect)
        else:
            entry["props"].append(dict(kind=key, rect=rect))
        total_tiles += 1
    px = [(r, g2, b) for r, g2, b, a in th["pieces"]["MM"][0].convert("RGBA").getdata() if a > 128]
    hs = [colorsys.rgb_to_hsv(r/255, g2/255, b/255) for r, g2, b in px]
    sat = [x for x in hs if x[1] > 0.25 and x[2] > 0.2]
    if len(sat) < len(px) * 0.15:
        v = sum(v for _, _, v in hs) / max(1, len(hs))
        entry["paletteGroup"] = "bone" if v > 0.7 else ("grey" if v > 0.3 else "black")
    else:
        xx = sum(math.cos(2*math.pi*h) for h, _, _ in sat)
        yy = sum(math.sin(2*math.pi*h) for h, _, _ in sat)
        deg = ((math.atan2(yy, xx) / (2*math.pi)) % 1.0) * 360
        entry["paletteGroup"] = ("red" if deg < 20 or deg >= 345 else "orange" if deg < 45 else
            "gold" if deg < 70 else "green" if deg < 170 else "blue" if deg < 260 else
            "purple" if deg < 315 else "red")
    out_themes.append(entry)

atlas.save(OUT / "tiles_v2.png")
doc = dict(texture="tiles_v2.png", textureSize=list(atlas.size), tileSize=32, dropHeight=12,
           contract="v2.1-variants-props", styleFamily="dcss", kind="stage",
           stageTraitVocabulary=STAGE_TRAITS, themes=out_themes)
(OUT / "tiles_v2_slices.json").write_text(json.dumps(doc, indent=1))
print("atlas:", atlas.size, "cells:", total_tiles)
from collections import Counter
print("registers:", Counter(r for t in out_themes for r in t["register"]))
