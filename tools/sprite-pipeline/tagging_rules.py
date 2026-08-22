"""Heuristic tagging rules: DCSS filename/category -> namegen traits, bodyPlan, register, vibe.

Trait vocabulary = namegen/src/NameGen/Data/traits.json (character traits).
Affinities are 0..1; sparse. Pixel-derived tags (weightClass, paletteGroup, size traits)
are computed in the build script, then merged with these keyword rules.
"""

# keyword (substring of filename stem or category path) -> dict of tag payloads
# order matters: later rules override earlier scalar fields, trait affinities merge by max.
RULES = [
    # --- body plans by category/keyword ---
    (r"animals/", dict(bodyPlan="quadruped", register=["normal", "fantasy"])),
    (r"snake|adder|anaconda|mamba|python|worm|leech|slug|eel|naga", dict(bodyPlan="serpent", traits={"weaving": 0.7, "skulking": 0.5})),
    (r"bat|butterfly|moth|bee|wasp|shrike|raven|vulture|harpy|drake|wisp", dict(bodyPlan="winged", traits={"aerial": 0.8, "floaty": 0.5, "swift": 0.4})),
    (r"dragon", dict(bodyPlan="winged", traits={"aerial": 0.5, "brutal": 0.6, "giant": 0.6}, register=["fantasy"])),
    (r"eyes/|eyeball|orb_guardian|floating|\bstar\b|_star\b|vortex", dict(bodyPlan="floating", traits={"floaty": 0.9, "phantom": 0.4}, vibe="goofy")),
    (r"jelly|ooze|slime|pulsating|amorphous/|blob", dict(bodyPlan="blob", traits={"sluggish": 0.6, "gentle": 0.2}, vibe="goofy")),
    (r"skull|head\b|death_cob", dict(bodyPlan="headOnly", vibe="goofy")),
    (r"crab|scorpion|spider|beetle|roach|mantis|(^|_)ant\b|tarantella", dict(bodyPlan="quadruped", traits={"skulking": 0.6})),
    (r"elephant|yak|catoblepas|hog|swine|bear|hound|dog|wolf|jackal|frog|newt|turtle|basilisk|lizard", dict(bodyPlan="quadruped")),
    (r"aquatic/|shark|fish\b|eel\b|octopus|kraken", dict(bodyPlan="serpent", traits={"weaving": 0.6, "swift": 0.4})),
    (r"spectral_|dancing", dict(bodyPlan="floating", vibe="goofy", traits={"floaty": 0.8, "whirling": 0.6, "phantom": 0.5})),
    (r"deathcap|fungus|mushroom|toadstool", dict(bodyPlan="blob", vibe="goofy", traits={"sluggish": 0.7, "patient": 0.5})),

    # --- registers ---
    (r"undead|skelet|zombie|lich|ghost|ghoul|wight|wraith|vampire|phantasm|shadow|spectre|revenant|mummy|bog_body|macabre|flayed|drowned", dict(register=["horror", "fantasy"], traits={"phantom": 0.7, "elusive": 0.4})),
    (r"demons/|demon|hell_|abyss|panlord|torturer|executioner|fiend|imp\b", dict(register=["horror", "fantasy"], traits={"brutal": 0.6, "reckless": 0.3})),
    (r"golem|crystal_guardian|iron_|electric_|clockwork|orb_guardian|ushabti", dict(register=["scifi", "fantasy"], traits={"guardian": 0.5, "sluggish": 0.4, "patient": 0.5})),
    (r"angel|seraph|cherub|holy|pearl", dict(register=["fantasy"], vibe="regal", traits={"aerial": 0.6, "gentle": 0.4, "guardian": 0.4})),

    # --- archetype traits by keyword ---
    (r"knight|warrior|fighter|soldier|legion", dict(traits={"guardian": 0.5, "brutal": 0.4, "patient": 0.3})),
    (r"archer|bow|arbalest|crossbow", dict(traits={"artillery": 0.9, "piercing": 0.7, "patient": 0.3})),
    (r"mage|wizard|sorcer|conjur|warlock|annihilator|magus", dict(traits={"artillery": 0.7, "arcing": 0.5, "fragile": 0.4})),
    (r"priest|shaman|druid|death_mage|necro|summon|enchant", dict(traits={"artillery": 0.5, "patient": 0.4})),
    (r"rogue|assassin|stalker|sneak|thief", dict(traits={"elusive": 0.8, "skulking": 0.7, "swift": 0.6, "frantic": 0.3})),
    (r"ogre|troll|cyclops|giant|titan|juggernaut", dict(traits={"giant": 0.9, "heavy": 0.8, "brutal": 0.7, "sluggish": 0.4})),
    (r"spriggan|gnome|dwarf|kobold|halfling|felid|quokka", dict(traits={"tiny": 0.6, "swift": 0.5, "elusive": 0.4})),
    (r"blink|phase|shifter", dict(traits={"elusive": 0.9, "phantom": 0.5, "frantic": 0.4})),
    (r"fire|flame|burning|lava|efreet|salamander|vortex", dict(traits={"brutal": 0.5, "reckless": 0.4})),
    (r"ice|frost|simulacr|snow", dict(traits={"patient": 0.4, "stunner": 0.3})),
    (r"electric|lightning|spark|shock", dict(traits={"swift": 0.5, "stunner": 0.6, "frantic": 0.4})),
    (r"turtle|snail|shell|tortoise", dict(traits={"guardian": 0.7, "sluggish": 0.7, "patient": 0.6})),
    (r"hydra|tentacle|kraken", dict(bodyPlan="serpent", traits={"reaching": 0.8, "whirling": 0.4})),
    (r"centaur|yaktaur", dict(traits={"swift": 0.6, "artillery": 0.5}, bodyPlan="quadruped")),

    # --- vibe ---
    (r"frog|duck|quokka|slug|snail|mushroom|toadstool|cob|jiangshi|dancing|killer_bee|porcupine|elephant_slug|giant_cockroach", dict(vibe="goofy")),
    (r"butterfly|bunny|rabbit|bat\b|newt", dict(vibe="cute")),
    (r"executioner|torturer|reaper|bone|flayed|lich|abomination|horror", dict(vibe="menacing")),
    (r"maggot|worm|leech|necrophage|rot|slime|ooze|bile", dict(vibe="gross")),
    (r"angel|seraph|(^|_)king\b|queen|emperor|royal|crown|paladin", dict(vibe="regal")),
]

# humanoid composite kits: (kit name, gear layers, kit tags)
KITS = [
    ("knight",   ["body/armour_blue_gold.png", "hand1/long_sword_slant.png"],
     dict(traits={"guardian": 0.6, "brutal": 0.4, "patient": 0.4}, vibe="regal")),
    ("paladin",  ["body/armour_mummy.png", "hand1/mace.png"],
     dict(traits={"guardian": 0.7, "gentle": 0.3, "patient": 0.5}, vibe="regal")),
    ("wizard",   ["body/robe_blue.png", "head/wizard_bluegreen.png", "hand1/staff_mage.png"],
     dict(traits={"artillery": 0.8, "arcing": 0.5, "fragile": 0.5})),
    ("warlock",  ["body/robe_black_gold.png", "head/hood_red.png", "hand1/staff_evil.png"],
     dict(traits={"artillery": 0.7, "brutal": 0.4}, vibe="menacing", register=["horror", "fantasy"])),
    ("archer",   ["body/leather_green.png", "hand1/bow.png"],
     dict(traits={"artillery": 0.9, "piercing": 0.7})),
    ("rogue",    ["body/coat_black.png", "head/hood_black2.png", "hand1/dagger_slant.png"],
     dict(traits={"elusive": 0.8, "skulking": 0.7, "swift": 0.6})),
    ("monk",     ["body/karate.png"],
     dict(traits={"swift": 0.6, "frantic": 0.5, "gentle": 0.3})),
    ("barbarian",["legs/loincloth_red.png", "hand1/axe.png"],
     dict(traits={"brutal": 0.8, "reckless": 0.6})),
    ("pirate",   ["body/coat_black.png", "head/bandana_ybrown.png", "hand1/cutlass.png"],
     dict(traits={"reckless": 0.5, "swift": 0.4}, vibe="goofy")),
    ("duelist",  ["body/jessica.png", "hand1/rapier.png"],
     dict(traits={"swift": 0.7, "piercing": 0.6, "frantic": 0.4})),
]

SPECIES = [
    ("human_m", {}), ("human_f", {}),
    ("deep_elf_m", dict(traits={"fragile": 0.3, "swift": 0.3})),
    ("deep_dwarf_m", dict(traits={"heavy": 0.3, "patient": 0.4})),
    ("hill_orc_m", dict(traits={"brutal": 0.4})),
    ("draconian_gold_m", dict(traits={"guardian": 0.3}, register=["fantasy"])),
    ("mummy_m", dict(register=["horror", "fantasy"], traits={"phantom": 0.4, "sluggish": 0.3})),
    ("centaur_brown_m", dict(bodyPlan="quadruped", traits={"swift": 0.6})),
]
