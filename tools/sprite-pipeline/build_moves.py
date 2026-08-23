"""Melee attack sprite library build (moves_v2). Same pipeline as characters.

Pools: item/weapon (melee only), effect bursts/clouds, misc/blood impacts,
goofy objects (food/potion/misc). Ranged weapons and projectiles are excluded
(projectile track comes later).
"""
import sys, re, json, random, colorsys, math
sys.path.insert(0, "/home/claude/sprite-work")
from pipeline import *

random.seed(2026)
W = DCSS / "item/weapon"
E = DCSS / "effect"
TRAITS30 = ["heavy","light","giant","tiny","swift","sluggish","aerial","floaty","plummeting",
    "grounded","brutal","gentle","stunner","launcher","spiker","reaching","patient","frantic",
    "elusive","phantom","guardian","mirror","artillery","weaving","arcing","piercing","fragile",
    "skulking","whirling","reckless"]

RANGED = re.compile(r"(^|_)bow|arbalest|crossbow|sling|blowgun|dart|arrow|bolt|bullet|needle|"
                    r"javelin|tomahawk|throwing|stone\b|rock\b|net\b|shot|sniper|"
                    r"krishna|punk|hellfire")  # spear is melee in DCSS; last three are artefact ranged weapons

# attackClass rules: (regex, class, traits, element, vibe)
CLASSES = [
    (r"whip",                        "whip",   {"reaching":0.8,"swift":0.5,"frantic":0.4}, None, None),
    (r"dagger|quick_blade|knife",    "blade",  {"piercing":0.8,"swift":0.7,"elusive":0.5,"frantic":0.4}, None, None),
    (r"rapier",                      "blade",  {"piercing":0.9,"swift":0.6}, None, None),
    (r"sword|falchion|scimitar|sabre|katana|cutlass|blade",
                                     "blade",  {"piercing":0.5,"swift":0.4,"brutal":0.4}, None, None),
    (r"axe|waraxe|broad_axe|battleaxe|executioner",
                                     "axe",    {"brutal":0.8,"heavy":0.5,"reckless":0.4}, None, None),
    (r"club|mace|hammer|morningstar|eveningstar|flail|giant_club|great_mace",
                                     "blunt",  {"brutal":0.6,"stunner":0.7,"heavy":0.5,"sluggish":0.3}, None, None),
    (r"glaive|halberd|scythe|trident|bardiche|lance|pike|lajatang|spear",
                                     "polearm",{"reaching":0.9,"piercing":0.6,"patient":0.4}, None, None),
    (r"staff|quarterstaff|sceptre|rod",
                                     "staff",  {"arcing":0.6,"patient":0.5,"guardian":0.3}, None, None),
    (r"tooth|fang",                  "natural",{"piercing":0.6,"brutal":0.5,"skulking":0.4}, None, None),
    (r"cloud_fire|flame|forest_fire|searing",
                                     "burst",  {"brutal":0.6,"reckless":0.5}, "fire", None),
    (r"cloud_cold|frost|icicle",     "burst",  {"stunner":0.5,"patient":0.4}, "ice", None),
    (r"cloud_poison|meph|miasma|cloud_acid",
                                     "burst",  {"skulking":0.5,"gentle":0.2}, "poison", "gross"),
    (r"cloud_storm|heataura|tloc",   "burst",  {"stunner":0.7,"swift":0.5,"frantic":0.4}, "electric", None),
    (r"cloud_chaos|mutagenic|irradiate|disjunct",
                                     "burst",  {"reckless":0.7,"whirling":0.5}, "chaos", "goofy"),
    (r"cloud_neg|drain|cloud_gloom|cloud_spectral|cloud_black",
                                     "burst",  {"phantom":0.7,"elusive":0.4}, "spectral", "menacing"),
    (r"cloud_grey_smoke|cloud_blue_smoke|cloud_yellow|calc_dust|sandblast|cloud_rain|cloud_magic",
                                     "burst",  {"elusive":0.5,"floaty":0.4}, None, None),
    (r"sting",                       "natural",{"piercing":0.7,"skulking":0.5}, "poison", None),
    (r"blood",                       "impact", {"brutal":0.7,"reckless":0.4}, None, "gross"),
]

def classify(stem):
    for pat, cls, traits, element, vibe in CLASSES:
        if re.search(pat, stem):
            return cls, dict(traits), element, vibe
    return None, {}, None, None

# wielder compatibility per attackClass: which character bodyPlans use it natively
COMPAT = {
    "blade":  ["biped"], "axe": ["biped"], "blunt": ["biped"], "polearm": ["biped"],
    "whip":   ["biped", "serpent"], "staff": ["biped", "floating"],
    "natural":["quadruped", "serpent", "winged", "headOnly", "biped"],
    "burst":  ["floating", "blob", "winged", "biped", "serpent", "quadruped", "headOnly"],
    "impact": ["quadruped", "serpent", "winged", "headOnly", "blob", "biped"],
    "object": ["biped", "blob", "floating", "quadruped", "serpent", "winged", "headOnly"],
}

records = []
seen = set()

def add(path, cls, traits, element, vibe, rel):
    im = crop_to_content(load_rgba(path))
    if im.width < 8 or im.height < 8:
        return
    records.append(dict(id=f"mv_{path.stem}", img=im, rel=rel, cls=cls,
                        traits=traits, element=element, vibe=vibe or "neutral"))

# --- melee weapons (variant-collapsed) ---
for p in sorted(W.glob("*.png")) + sorted((W / "artefact").glob("*.png")):
    stem = p.stem.lower()
    if RANGED.search(stem) and not re.search(r"trident|halberd|glaive", stem):
        continue
    base = re.sub(r"\d+$", "", stem)
    if base in seen:
        continue
    cls, traits, element, vibe = classify(stem)
    if "artefact" in p.parent.name:
        if cls is None:
            OVERRIDES = {"axe_of_woe": "axe", "skullcrusher": "blunt", "shillelagh": "blunt",
                         "octopus_king": "blunt", "crystal_spear": "polearm", "morg": "blade",
                         "wrath_of_trog": "axe"}
            cls = next((v for k, v in OVERRIDES.items() if k in stem), "blade")
            traits = {"piercing": 0.4, "swift": 0.3}
        traits = {**traits, "brutal": max(traits.get("brutal", 0), 0.6)}
        vibe = vibe or "menacing"
    if cls is None:
        continue
    seen.add(base)
    add(p, cls, traits, element, vibe, p.relative_to(DCSS).as_posix())

# --- effect bursts (one frame per family) ---
for p in sorted(E.glob("*.png")):
    stem = p.stem.lower()
    base = re.sub(r"\d+$", "", stem)
    if base in seen:
        continue
    cls, traits, element, vibe = classify(stem)
    if cls is None:
        continue
    seen.add(base)
    add(p, cls, traits, element, vibe, p.relative_to(DCSS).as_posix())

# --- blood impacts (a few) ---
for name in ["blood_red.png", "blood_red1.png", "blood_green.png"]:
    p = DCSS / "misc/blood" / name
    if p.exists() and p.stem not in seen:
        seen.add(p.stem)
        add(p, "impact", {"brutal": 0.7, "reckless": 0.4}, None, "gross", f"misc/blood/{name}")

# --- goofy objects (charm quota; continues the current sheet's hearts-and-dice spirit) ---
GOOFY = [
    ("item/food/banana.png",       {"swift": 0.4}),
    ("item/food/cheese.png",       {"gentle": 0.4}),
    ("item/food/meat_ration.png",  {"heavy": 0.4}),
    ("item/food/lemon.png",        {"gentle": 0.3}),
    ("item/potion/magenta.png",    {"reckless": 0.5}),
    ("item/potion/bubbly.png",     {"reckless": 0.4}),
    ("item/misc/misc_crystal.png", {"arcing": 0.5}),
    ("item/misc/misc_box.png",     {"patient": 0.4}),
    ("item/gold/gold_pile.png",    {"heavy": 0.3}),
    ("item/misc/misc_fan.png",     {"floaty": 0.5}),
    ("item/misc/misc_lamp.png",    {"arcing": 0.4}),
    ("item/misc/misc_stones.png",  {"sluggish": 0.4}),
]
for rel, traits in GOOFY:
    p = DCSS / rel
    if p.exists():
        add(p, "object", traits, None, "goofy", rel)

print("melee library collected:", len(records))
from collections import Counter
print(Counter(r["cls"] for r in records))

# --- palette groups + quantize with the SAME master palette derivation as characters ---
def palette_group(im):
    rgba = im.convert("RGBA")
    px = [(r, g, b) for r, g, b, a in rgba.getdata() if a > 128]
    hs = [colorsys.rgb_to_hsv(r/255, g/255, b/255) for r, g, b in px]
    sat = [x for x in hs if x[1] > 0.25 and x[2] > 0.2]
    if len(sat) < len(px) * 0.15:
        v = sum(v for _, _, v in hs) / max(1, len(hs))
        return "bone" if v > 0.7 else ("grey" if v > 0.3 else "black")
    x = sum(math.cos(2*math.pi*h) for h, _, _ in sat)
    y = sum(math.sin(2*math.pi*h) for h, _, _ in sat)
    deg = ((math.atan2(y, x) / (2*math.pi)) % 1.0) * 360
    return ("red" if deg < 20 or deg >= 345 else "orange" if deg < 45 else
            "gold" if deg < 70 else "green" if deg < 170 else "blue" if deg < 260 else
            "purple" if deg < 315 else "red")

allmon = list((DCSS / "mon").rglob("*.png"))
pal = build_master_palette([load_rgba(p) for p in random.sample(allmon, 150)], colors=64)

for r in records:
    r["paletteGroup"] = palette_group(r["img"])
    r["q"] = quantize_to(r["img"], pal)

records.sort(key=lambda r: r["id"])
atlas, entries = pack_atlas([(r["id"], r["q"]) for r in records], cell=(40, 40), cols=16)
for e, r in zip(entries, records):
    traits = {k: round(v, 2) for k, v in r["traits"].items() if k in TRAITS30 and v >= 0.2}
    e.update(source="dcss-tiles", sourceFile=r["rel"], license="CC0", interim=False,
             attackClass=r["cls"], element=r["element"], vibe=r["vibe"],
             tags=sorted(traits.keys()), traitAffinity=traits,
             compatiblePlans=COMPAT[r["cls"]], paletteGroup=r["paletteGroup"])
atlas.save(OUT / "moves_v2.png")
doc = dict(texture="moves_v2.png", textureSize=list(atlas.size), cell=[40, 40],
           styleFamily="dcss", kind="melee", traitVocabulary=TRAITS30, sprites=entries)
(OUT / "moves_v2_slices.json").write_text(json.dumps(doc, indent=1))
print("atlas:", atlas.size)
print("vibe:", Counter(r["vibe"] for r in records))

# overview render
bg = Image.new("RGBA", atlas.size, (44, 47, 66, 255))
bg.alpha_composite(atlas)
bg.resize((atlas.width * 3, atlas.height * 3), Image.NEAREST).convert("RGB").save(OUT / "moves_overview.png")
