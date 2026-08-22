"""Full library build: collect, dedupe, composite humanoids, compute pixel tags,
apply keyword rules, quantize, pack, emit v2 JSON + QA sheets."""
import sys, re, json, random, colorsys
sys.path.insert(0, "/home/claude/sprite-work")
from pipeline import *
from tagging_rules import RULES, KITS, SPECIES

random.seed(2026)
P = DCSS / "player"
EXCLUDE_DIRS = ("tentacles", "statues", "sprint", "zombies", "simulacra", "spectrals", "vault", "aberr", "draco", "panlord")
EXCLUDE_STEMS = re.compile(r"^(demon_body_|demon_head_|demon_wings|demon_legs|demon_arm|draco-)")
TRAITS30 = ["heavy","light","giant","tiny","swift","sluggish","aerial","floaty","plummeting",
    "grounded","brutal","gentle","stunner","launcher","spiker","reaching","patient","frantic",
    "elusive","phantom","guardian","mirror","artillery","weaving","arcing","piercing","fragile",
    "skulking","whirling","reckless"]

# ---------- collect monsters ----------
seen_base = set()
records = []  # dict: id, img, relpath
for p in sorted((DCSS / "mon").rglob("*.png")):
    rel = p.relative_to(DCSS).as_posix()
    if any(f"/{d}/" in f"/{rel}" for d in EXCLUDE_DIRS):
        continue
    if EXCLUDE_STEMS.match(p.stem):
        continue
    base = re.sub(r"\d+$", "", p.stem)
    if base in seen_base:
        continue
    im = crop_to_content(load_rgba(p))
    if im.width < 10 or im.height < 12:
        continue
    seen_base.add(base)
    records.append(dict(id=f"dcss_{p.stem}", img=im, rel=rel))
print("monsters collected:", len(records))

# ---------- humanoid composites ----------
def comp(layers):
    base = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    ok = 0
    for relp in layers:
        fp = P / relp
        if fp.exists():
            base.alpha_composite(load_rgba(fp)); ok += 1
    return crop_to_content(base), ok

hum_count = 0
for kit_name, gear, kit_tags in KITS:
    for sp_name, sp_tags in SPECIES:
        if not (P / "base" / f"{sp_name}.png").exists():
            continue
        # skip odd pairings: centaurs/mummies only in some kits to keep variety sane
        if sp_name.startswith("centaur") and kit_name not in ("archer", "knight", "duelist"):
            continue
        if sp_name.startswith("mummy") and kit_name not in ("warlock", "paladin", "monk"):
            continue
        im, ok = comp([f"base/{sp_name}.png"] + gear)
        if ok < 1 + len(gear) - 1 or im.width < 10 or im.height < 20:
            continue
        records.append(dict(id=f"hum_{kit_name}_{sp_name}", img=im,
                            rel="player-composite", kit=kit_tags, species=sp_tags))
        hum_count += 1
print("humanoid composites:", hum_count)

# ---------- pixel stats ----------
def pixel_stats(im):
    rgba = im.convert("RGBA")
    px = [(r, g, b) for r, g, b, a in rgba.getdata() if a > 128]
    area = len(px)
    hs = []
    for r, g, b in px:
        h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        hs.append((h, s, v))
    sat = [x for x in hs if x[1] > 0.25 and x[2] > 0.2]
    if len(sat) < area * 0.15:
        v_avg = sum(v for _, _, v in hs) / max(1, len(hs))
        group = "bone" if v_avg > 0.7 else ("grey" if v_avg > 0.3 else "black")
    else:
        import math
        x = sum(math.cos(2 * math.pi * h) for h, _, _ in sat)
        y = sum(math.sin(2 * math.pi * h) for h, _, _ in sat)
        hue = (math.atan2(y, x) / (2 * math.pi)) % 1.0
        deg = hue * 360
        group = ("red" if deg < 20 or deg >= 345 else "orange" if deg < 45 else
                 "gold" if deg < 70 else "green" if deg < 170 else "blue" if deg < 260 else
                 "purple" if deg < 315 else "red")
    return area, group

for r in records:
    r["area"], r["paletteGroup"] = pixel_stats(r["img"])

areas = sorted(r["area"] for r in records)
t1, t2 = areas[len(areas) // 3], areas[2 * len(areas) // 3]

# ---------- tagging ----------
def apply_rules(r):
    traits = {}
    body, vibe, regs = None, None, None
    key = (r["rel"] + "/" + r["id"]).lower()
    for pat, payload in RULES:
        if re.search(pat, key):
            for k, v in payload.get("traits", {}).items():
                traits[k] = max(traits.get(k, 0), v)
            body = payload.get("bodyPlan", body)
            vibe = payload.get("vibe", vibe)
            regs = payload.get("register", regs)
    for src in (r.get("kit"), r.get("species")):
        if src:
            for k, v in src.get("traits", {}).items():
                traits[k] = max(traits.get(k, 0), v)
            body = src.get("bodyPlan", body)
            vibe = src.get("vibe", vibe)
            regs = src.get("register", regs)
    # size traits from pixel area (composites are all near-equal size; force medium)
    if r["rel"] == "player-composite":
        wc = "medium"
    elif r["area"] <= t1:
        traits["light"] = max(traits.get("light", 0), 0.6)
        if r["area"] <= areas[len(areas) // 6]:
            traits["tiny"] = max(traits.get("tiny", 0), 0.6)
        wc = "light"
    elif r["area"] >= t2:
        traits["heavy"] = max(traits.get("heavy", 0), 0.6)
        if r["area"] >= areas[-len(areas) // 8]:
            traits["giant"] = max(traits.get("giant", 0), 0.6)
        wc = "heavy"
    else:
        wc = "medium"
    # pixel-derived size wins conflicts with keyword-derived size traits
    if wc == "light":
        traits.pop("giant", None); traits.pop("heavy", None)
    elif wc == "heavy":
        traits.pop("tiny", None); traits.pop("light", None)
    else:
        for t in ("giant", "tiny"): traits.pop(t, None)
    if body in ("floating", "winged"):
        traits.setdefault("aerial", 0.5)
    if body is None:
        body = "biped"
    if vibe is None:
        vibe = "menacing" if any(traits.get(t, 0) > 0.5 for t in ("brutal", "phantom")) else "neutral"
    if regs is None:
        regs = ["fantasy"]
    traits = {k: round(v, 2) for k, v in traits.items() if k in TRAITS30 and v >= 0.2}
    return traits, body, wc, vibe, regs

for r in records:
    r["traits"], r["bodyPlan"], r["weightClass"], r["vibe"], r["register"] = apply_rules(r)

# ---------- quantize + pack ----------
allmon = list((DCSS / "mon").rglob("*.png"))
pal = build_master_palette([load_rgba(p) for p in random.sample(allmon, 150)], colors=64)
for r in records:
    r["q"] = quantize_to(r["img"], pal)

records.sort(key=lambda r: r["id"])
atlas, entries = pack_atlas([(r["id"], r["q"]) for r in records], cell=(40, 40), cols=24)
for e, r in zip(entries, records):
    e.update(source="dcss-tiles" if r["rel"] != "player-composite" else "dcss-player-composite",
             sourceFile=r["rel"], license="CC0", interim=False,
             tags=sorted(r["traits"].keys()), traitAffinity=r["traits"],
             bodyPlan=r["bodyPlan"], weightClass=r["weightClass"], vibe=r["vibe"],
             register=r["register"], paletteGroup=r["paletteGroup"])
atlas.save(OUT / "players_v2.png")
doc = dict(texture="players_v2.png", textureSize=list(atlas.size), cell=[40, 40],
           styleFamily="dcss", traitVocabulary=TRAITS30, sprites=entries)
(OUT / "players_v2_slices.json").write_text(json.dumps(doc, indent=1))
print("library:", len(entries), "atlas:", atlas.size)

# ---------- stats + QA sheets ----------
from collections import Counter
print("bodyPlan:", Counter(r["bodyPlan"] for r in records))
print("vibe:", Counter(r["vibe"] for r in records))
print("weight:", Counter(r["weightClass"] for r in records))
print("palette:", Counter(r["paletteGroup"] for r in records))

qa = random.sample(records, 48)
cell, scale = 44, 4
cols = 8
rows_n = 6
bg = Image.new("RGBA", (cols * cell, rows_n * cell), (44, 47, 66, 255))
for i, r in enumerate(qa):
    c, rw = i % cols, i // cols
    im = r["q"]
    bg.alpha_composite(im, (c * cell + (cell - im.width) // 2, rw * cell + (cell - 6 - im.height)))
bg.resize((bg.width * scale, bg.height * scale), Image.NEAREST).convert("RGB").save(OUT / "qa_sheet.png")
lines = []
for i, r in enumerate(qa):
    lines.append(f"{i:2d} {r['id']}: plan={r['bodyPlan']} wc={r['weightClass']} vibe={r['vibe']} "
                 f"reg={','.join(r['register'])} pal={r['paletteGroup']} traits={r['traits']}")
(OUT / "qa_tags.txt").write_text("\n".join(lines))
print("QA sheet + tags written")
