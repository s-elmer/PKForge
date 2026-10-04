#!/usr/bin/env python3
"""Builds PKForge's Pokémon Showdown sprite set: the Black/White-style static front
sprites (normal and shiny) and the box icon sheet, with a table that maps every PKHeX
(species, form) to them.

Why: PKHeX numbers forms per species; Showdown names them ("charizard-megax"). Showdown's
pokedex lists each species' forms in the games' internal order (formeOrder), so PKHeX form
n is formeOrder[n], except for the forms below that Showdown folds, merges or orders
differently. Every exception is explicit; any other mismatch is reported.

Sprites: Pokémon Showdown / Smogon (https://play.pokemonshowdown.com/sprites). Gen 6-9
Black/White-style sprites are drawn by the Smogon community; used in PKForge with their
permission for free, open-source software, credited in src/PKForge.App/Resources/Showdown/ATTRIBUTION.md.

Usage:
    dotnet run --project tools/SpriteForms/FormDump -c Release -- forms.tsv
    python3 tools/ShowdownSprites/build.py forms.tsv

Writes:
    src/PKForge.Domain/Resources/showdownsprites.tsv
        key "<species>-<form>"  stem  flags(1 front, 2 shiny front)  icon index
    src/PKForge.App/Resources/Showdown/front/<stem>.png, front-shiny/<stem>.png, icons.png
"""
import concurrent.futures
import csv
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BASE = "https://play.pokemonshowdown.com"
OUT_TABLE = os.path.join(ROOT, "src/PKForge.Domain/Resources/showdownsprites.tsv")
OUT_ART = os.path.join(ROOT, "src/PKForge.App/Resources/Showdown")
UA = {"User-Agent": "PKForge-sprite-build (https://github.com/sofianeelhor/PKForge)"}

toid = lambda s: re.sub(r"[^a-z0-9]", "", s.lower())

# PKHeX (species, form) whose look Showdown draws under another name, or not at all.
OVERRIDES = {
    (59, 2): "arcanine-hisui", (101, 2): "electrode-hisui", (549, 2): "lilligant-hisui",
    (713, 2): "avalugg-hisui", (900, 1): "kleavor",          # Legends: Arceus nobles
    (493, 18): "arceus",                                      # Legend Arceus event look
    (658, 1): "greninja", (658, 2): "greninja-ash",           # Battle Bond looks normal
    (774, 7): "minior",                                       # red core
    # Totem forms look like the regular form; Showdown has no front sprite for them.
    (20, 2): "raticate-alola", (105, 2): "marowak-alola", (735, 1): "gumshoos", (738, 1): "vikavolt",
    (743, 1): "ribombee", (752, 1): "araquanid", (754, 1): "lurantis", (758, 1): "salazzle",
    (777, 1): "togedemaru", (778, 2): "mimikyu", (778, 3): "mimikyu-busted", (784, 1): "kommoo",
    (744, 1): "rockruff",                                     # Own Tempo Rockruff looks normal
}
for f in range(1, 5):
    OVERRIDES[(1007, f)] = "koraidon"                         # riding modes share the base look
    OVERRIDES[(1008, f)] = "miraidon"
for f in range(7):
    OVERRIDES[(774, f)] = "minior-meteor"                     # every meteor shell is one sprite
for f, colour in enumerate(["orange", "yellow", "green", "blue", "indigo", "violet"], start=8):
    OVERRIDES[(774, f)] = f"minior-{colour}"
for sp in (414, 664, 665):                                    # Mothim / Scatterbug / Spewpa forms look alike
    for f in range(1, 20):
        OVERRIDES[(sp, f)] = {414: "mothim", 664: "scatterbug", 665: "spewpa"}[sp]

def fetch(url, binary=True):
    for attempt in range(4):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=30) as r:
                data = r.read()
                return data if binary else data.decode("utf-8")
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            time.sleep(1 + attempt)
        except Exception:
            time.sleep(1 + attempt)
    raise RuntimeError(f"could not fetch {url}")

def stem_of(base_name, forme_name):
    if forme_name == base_name:
        return toid(base_name)
    return toid(base_name) + "-" + toid(forme_name.split("-", 1)[1])

def main():
    forms_path = sys.argv[1]
    dex = json.loads(fetch(f"{BASE}/data/pokedex.json", binary=False))
    js = fetch(f"{BASE}/js/battle-dex-data.js", binary=False)
    block = re.search(r"BattlePokemonIconIndexes\s*=\s*\{(.*?)\};", js, re.S).group(1)
    icon_extra = {k: int(a) + int(b) for k, a, b in re.findall(r"(\w+):\s*(\d+)\s*\+\s*(\d+)", block)}
    icon_extra.update({k: int(v) for k, v in re.findall(r"(\w+):\s*(\d+)\s*,", block)})

    species = {v["num"]: v for v in dex.values() if 0 < v.get("num", 0) <= 1025 and not v.get("forme")}
    forms = {}
    for row in csv.reader(open(forms_path, encoding="utf-8"), delimiter="\t"):
        sp, fm, ctx = int(row[0]), int(row[1]), row[2]
        if 1 <= sp <= 1025 and (ctx == "Gen9" or (sp, fm) not in forms):
            forms[(sp, fm)] = row[4] if len(row) > 4 else ""

    table, unresolved = {}, []
    for (sp, fm) in sorted(forms):
        base = species[sp]
        order = base.get("formeOrder") or [base["name"]]
        if (sp, fm) in OVERRIDES:
            stem = OVERRIDES[(sp, fm)]
        elif fm < len(order):
            stem = stem_of(base["name"], order[fm])
        else:
            unresolved.append((sp, fm, forms[(sp, fm)]))
            stem = toid(base["name"])
        forme_id = stem.replace("-", "")
        icon = icon_extra[forme_id] if forme_id in icon_extra and fm > 0 else sp
        table[(sp, fm)] = [stem, icon]

    stems = sorted({s for s, _ in table.values()})
    os.makedirs(os.path.join(OUT_ART, "front"), exist_ok=True)
    os.makedirs(os.path.join(OUT_ART, "front-shiny"), exist_ok=True)
    have = {}
    def grab(job):
        folder, stem = job
        target = os.path.join(OUT_ART, "front" if folder == "gen5" else "front-shiny", stem + ".png")
        if os.path.exists(target):
            return job, True
        data = fetch(f"{BASE}/sprites/{folder}/{stem}.png")
        if data is None:
            return job, False
        open(target, "wb").write(data)
        return job, True
    jobs = [(f, s) for s in stems for f in ("gen5", "gen5-shiny")]
    with concurrent.futures.ThreadPoolExecutor(6) as pool:
        for job, ok in pool.map(grab, jobs):
            have[job] = ok
    open(os.path.join(OUT_ART, "icons.png"), "wb").write(fetch(f"{BASE}/sprites/pokemonicons-sheet.png"))

    lines = ["# PKHeX form -> Pokémon Showdown sprite stem and icon. Generated; do not edit.",
             "# Sprites: Pokémon Showdown / Smogon, used with permission for free, open-source software.",
             "# key\tstem\tflags(1 front, 2 shiny front)\ticon"]
    missing = []
    for (sp, fm), (stem, icon) in sorted(table.items()):
        flags = (1 if have.get(("gen5", stem)) else 0) | (2 if have.get(("gen5-shiny", stem)) else 0)
        if flags != 3:
            missing.append((sp, fm, stem, flags))
        lines.append(f"{sp}-{fm}\t{stem}\t{flags}\t{icon}")
    open(OUT_TABLE, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print(f"{len(table)} forms, {len(stems)} stems, {len(missing)} without a full front pair, {len(unresolved)} unresolved")
    for m in missing: print("  missing", m)
    for u in unresolved: print("  unresolved", u)

if __name__ == "__main__":
    main()
