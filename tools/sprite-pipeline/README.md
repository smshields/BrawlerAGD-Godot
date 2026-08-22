# Sprite pipeline (characters track)

Regenerates `godot/assets/players_v2.png` and `players_v2_slices.json` from source assets. Only needed when adding sources, fixing tags in bulk, or swapping in commissioned art; the game never runs this.

## Requirements

- Python 3.10+, Pillow (`pip install pillow`)
- Source clone: `git clone --depth 1 https://github.com/crawl/tiles.git <raw>/dcss-tiles`

## Layout expected by the scripts

Edit the `RAW`/`OUT` paths at the top of `pipeline.py` (they default to the Cowork session's paths). `RAW` must contain `dcss-tiles/releases/Nov-2015`.

## Run

```
python3 build_library.py
```

Outputs to `OUT`: `players_v2.png`, `players_v2_slices.json`, `master_palette.png`, `qa_sheet.png` + `qa_tags.txt` (random 48-sprite QA sample; re-run for fresh samples). Copy the two players_v2 files into `godot/assets/`.

## Tuning tags

- Keyword/trait rules: `tagging_rules.py` (RULES). Trait names must stay within the namegen 30-trait vocabulary; the build drops unknown names.
- Humanoid composites: KITS x SPECIES in the same file.
- One-off fixes: hand-edit `players_v2_slices.json`; it survives until the next full rebuild, so prefer rule fixes for anything systematic.

## Adding commissioned art later

Author to `docs/art/style-spec.md`, deliver sheets per its Delivery format section, add an ingest block in `build_library.py` mirroring the monster collection loop, set `interim: false`, source and license fields accordingly. The selector needs no changes.
