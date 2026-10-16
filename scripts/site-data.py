#!/usr/bin/env python3
"""Builds the data for the changelog page (site/): site/data.json and site/changelog.html.

  python scripts/site-data.py

Reads:
  src/MPBalancer/defaults.txt              the curves, thrust_horse, thrust_damage and horse settings
  site/changelog.md                        the changelog shown on the page
  reports/data/weapons-v1.4.8.csv          each weapon usage's listed thrust damage and thrust speed (local export)
  ../_BannerlordDecompiled/ModuleData/...  the game's classes, perks, characters and items (GAME_DATA overrides)

For every cavalry class it enumerates the perk choices players can spawn with (one perk per perk list) and keeps the
distinct (weapon, speed bonus perks, horse) setups for its spears and lances; the page computes the damage from them
with the same model as the mod (docs/mod-damage.md). Run it again after changing defaults.txt or the changelog.
"""
import csv
import itertools
import json
import math
import os
import re
import xml.etree.ElementTree as ET

import markdown

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SITE = os.path.join(ROOT, "site")
GAME_DATA = os.environ.get("GAME_DATA", os.path.join(
    ROOT, "..", "_BannerlordDecompiled", "ModuleData", "DedicatedServer", "Native", "ModuleData"))
RIDING = 150  # every player cavalry class
POLEARMS = ("OneHandedPolearm", "TwoHandedPolearm")
FACTION = {"vlandia": "Vlandia", "empire": "Empire", "khuzait": "Khuzait", "aserai": "Aserai", "sturgia": "Sturgia",
           "battania": "Battania"}


def defaults():
    out = {}
    with open(os.path.join(ROOT, "src", "MPBalancer", "defaults.txt"), encoding="utf-8") as f:
        for line in f:
            if "=" in line and not line.lstrip().startswith("#"):
                k, v = line.split("=", 1)
                out[k.strip()] = v.strip()
    return out


def table(text):
    return {} if text == "none" else {k: float(v) for k, v in (t.split(":") for t in text.split())}


def plain_name(node):
    return re.sub(r"\{=[^}]*\}", "", node.get("name", ""))


def main():
    d = defaults()
    items = {i.get("id"): i for i in ET.parse(os.path.join(GAME_DATA, "mpitems.xml")).getroot().iter("Item")}
    chars = {c.get("id"): c for c in ET.parse(os.path.join(GAME_DATA, "mpcharacters.xml")).getroot().iter("NPCCharacter")}

    # Weapon usages: the mounted grip (one-handed polearm if the weapon has one) of every pierce-thrusting polearm.
    usages = {}
    with open(os.path.join(ROOT, "reports", "data", "weapons-v1.4.8.csv"), encoding="utf-8") as f:
        for r in csv.DictReader(f):
            if r["thrust_type"] != "pierce" or r["weapon_class"] not in POLEARMS or "couch" in r["item_usage"] \
                    or "bracing" in r["item_usage"]:
                continue
            prev = usages.get(r["item"])
            if prev is None or (r["weapon_class"] == "OneHandedPolearm" and prev["weapon_class"] != "OneHandedPolearm"):
                usages[r["item"]] = r
    rows = list(csv.DictReader(open(os.path.join(ROOT, "reports", "data", "weapons-v1.4.8.csv"), encoding="utf-8")))
    lances = {r["item"] for r in rows if "couch" in r["item_usage"]}
    # Thrown weapons (javelins) also have a polearm melee grip; they're not cavalry spears.
    for item in {r["item"] for r in rows if r["weapon_class"] not in POLEARMS}:
        usages.pop(item, None)

    horse_speed, knee, cap = float(d["horse_speed"]), float(d["horse_knee"]), float(d["horse_cap"])

    def top_speed(horse_id):
        h = items[horse_id].find(".//Horse")
        v = (int(h.get("speed")) + 1) * 0.22 * (1 + RIDING * 0.0032) * horse_speed
        return round(knee + (cap - knee) * math.tanh((v - knee) / (cap - knee)) if v > knee else v, 2)

    weapons, horses, setups = {}, {}, []
    for c in ET.parse(os.path.join(GAME_DATA, "mpclassdivisions.xml")).getroot().iter("MPClassDivision"):
        cid = c.get("id")
        if "cavalry" not in cid:
            continue
        parts = cid.split("_")
        cls = FACTION.get(parts[-1], parts[-1]) + " " + ("heavy" if parts[1] == "heavy" else "light") + " cavalry"
        base = {e.get("slot"): e.get("id", "").replace("Item.", "") for e in chars[c.get("hero")].iter("equipment")}
        lists = {}
        for p in c.iter("Perk"):
            mode = p.get("game_mode", "all")
            if mode != "all" and "Skirmish" not in mode and "Battle" not in mode:
                continue
            perk = {"name": plain_name(p), "equip": {}, "speed": 0.0}
            for e in p.iter("OnSpawnEffect"):
                if e.get("type") == "AlternativeEquipmentOnSpawn" and e.get("item") and e.get("target") in (None, "Any", "Player"):
                    perk["equip"][e.get("slot")] = e.get("item")
            for e in p.iter("Effect"):
                # Speed bonus perks that apply to mounted melee pierce thrusts (Lancer, Line Breaker, Ride Like the Wind).
                if e.get("type") == "SpeedBonus" and e.get("hit_type") in (None, "Melee") \
                        and e.get("damage_type") in (None, "Pierce"):
                    perk["speed"] += float(e.get("value"))
            lists.setdefault(p.get("perk_list"), []).append(perk)
        seen = set()
        for combo in itertools.product(*[lists[k] for k in sorted(lists)]):
            equip = dict(base)
            for perk in combo:
                equip.update(perk["equip"])
            speed = sum(p["speed"] for p in combo)
            horse = equip.get("Horse")
            for slot in ("Item0", "Item1", "Item2", "Item3"):
                w = equip.get(slot)
                if w not in usages or horse not in items:
                    continue
                key = (w, speed, horse)
                if key in seen:
                    continue
                seen.add(key)
                # Name the perks that matter for this setup: the one giving the weapon, speed perks, the horse's.
                relevant = [p["name"] for p in combo
                            if p["speed"] > 0 or p["equip"].get(slot) == w or p["equip"].get("Horse") == horse]
                setups.append({"class": cls, "weapon": w, "speed_bonus": speed, "horse": horse,
                               "perks": relevant})
                horses[horse] = {"name": plain_name(items[horse]), "top_speed": top_speed(horse)}
                r = usages[w]
                weapons[w] = {"name": r["name"], "lance": w in lances, "grip": r["weapon_class"],
                              "listed": int(r["thrust_damage"]), "thrust_speed": int(r["thrust_speed"])}

    buffs = table(d.get("thrust_damage", "none"))
    for w, info in weapons.items():
        info["buffed"] = buffs.get(w, info["listed"])

    def curve(name):
        return [float(v) for v in d[name].split()]

    data = {
        "curves": {"spear": curve("thrust_curve_spear"), "lance": curve("thrust_curve_lance"),
                   "foot": curve("thrust_curve_foot")},
        "thrust_horse": float(d["thrust_horse"]),
        "weapon_mult": table(d.get("weapon_mult", "none")),
        "weapons": dict(sorted(weapons.items(), key=lambda kv: (not kv[1]["lance"], kv[1]["name"]))),
        "horses": horses,
        "setups": sorted(setups, key=lambda s: (weapons[s["weapon"]]["name"], s["class"], horses[s["horse"]]["top_speed"])),
    }
    os.makedirs(SITE, exist_ok=True)
    with open(os.path.join(SITE, "data.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
    with open(os.path.join(SITE, "changelog.md"), encoding="utf-8") as f:
        lines = f.read().split("\n")
    # Python-Markdown needs a blank line before a list that follows a paragraph line ("**Horses**" then "- ...").
    fixed = []
    for line in lines:
        if line.startswith(("- ", "* ")) and fixed and fixed[-1].strip() and not fixed[-1].startswith(("- ", "* ", "  ")):
            fixed.append("")
        fixed.append(line)
    html = markdown.markdown("\n".join(fixed), extensions=["tables"])
    with open(os.path.join(SITE, "changelog.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(html + "\n")
    print("Wrote site/data.json (%d weapons, %d setups, %d horses) and site/changelog.html"
          % (len(weapons), len(setups), len(horses)))


if __name__ == "__main__":
    main()
