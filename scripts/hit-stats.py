#!/usr/bin/env python3
"""Summarizes MPBalancer's hit data: the hits-<start>.csv files it writes per server run (see HitLog.cs), or the
[MPB-HIT] lines of older server logs.

Usage:
  ssh bannerlord@7900x 'cat $(ls -t ~/bannerlord/instances/0/metrics/hits-*.csv | head -1)' | python scripts/hit-stats.py   (this run)
  ssh bannerlord@7900x 'zcat ~/bannerlord/instances/0/metrics/hits-*.csv.gz' | python scripts/hit-stats.py                  (earlier runs)
  python scripts/hit-stats.py hits.csv [--settings N]
  ssh bannerlord@7900x 'zgrep -h "\\[MPB-HIT\\]" ~/bannerlord/instances/0/logs/server-*.log.gz' | python scripts/hit-stats.py   (old logs)

--settings N keeps only hits made with balance configuration N; settings-<start>.csv says which values each
number stands for (a new number after every !mpb change). Without it, all configurations are mixed.

The first number is what vanilla would have dealt (logs before vanilla tracking: the damage before
the final-stage settings); "after" is the damage dealt. The reduction columns compare the two.
"""
import csv
import re
import sys
from collections import defaultdict

FIELD = re.compile(r"(\w+)=(\S+)")
SPEED_BUCKETS = [(0, 4), (4, 8), (8, 12), (12, 16), (16, 999)]
# Body parts as logged in part= (the engine's hit body part). Logs before part= was added only had a
# head/body split (tgt=) that lumped every non-head part together, so they're left out of the per-part tables.
PARTS = ["head", "neck", "chest", "abdomen", "shoulder", "arm", "legs"]


def parse(lines):
    lines = iter(lines)
    first = next(lines, "")
    if first.startswith("time,"):
        # CSV (HitLog): a header, then one row per hit. zcat of several runs repeats the header; skip those.
        header = first.strip().split(",")
        for row in csv.reader(lines):
            if not row or row[0] == "time":
                continue
            hit = dict(zip(header, row))
            for key in ("speed", "bonus", "armor", "vanilla", "before", "after"):
                hit[key] = float(hit[key])
            hit["base"] = hit["vanilla"]
            yield hit
        return
    for line in [first] + list(lines):
        if "[MPB-HIT]" not in line:
            continue
        hit = dict(FIELD.findall(line.split("[MPB-HIT]", 1)[1]))
        for key in ("speed", "bonus", "armor", "before", "after"):
            hit[key] = float(hit[key])
        # Newer logs include the true vanilla damage; older ones only the damage before the final-stage settings.
        # Early part= logs had left_/right_ variants.
        if "part" in hit:
            hit["part"] = hit["part"].replace("left_", "").replace("right_", "")
        hit["base"] = float(hit["vanilla"]) if "vanilla" in hit else hit["before"]
        yield hit


def summarize(title, hits):
    if not hits:
        return
    before = sum(h["base"] for h in hits)
    after = sum(h["after"] for h in hits)
    print("  %-34s %4d hits | avg %6.1f -> %6.1f | reduction %4.0f%% | max after %5.1f" % (
        title, len(hits), before / len(hits), after / len(hits),
        100 * (1 - after / before) if before else 0, max(h["after"] for h in hits)))


def main():
    args = sys.argv[1:]
    settings = None
    if "--settings" in args:
        i = args.index("--settings")
        settings = args[i + 1]
        del args[i:i + 2]
    source = open(args[0], encoding="utf-8", errors="replace") if args else sys.stdin
    hits = list(parse(source))
    if settings is not None:
        hits = [h for h in hits if h.get("settings") == settings]
    if not hits:
        print("No [MPB-HIT] lines found.")
        return

    print("All hits by damage type / source / victim:")
    groups = defaultdict(list)
    for h in hits:
        groups[(h["type"], h["src"], h["victim"])].append(h)
    for key in sorted(groups):
        summarize(" ".join(key), groups[key])

    pierce = [h for h in hits if h["type"] == "pierce" and h.get("tgt") != "shield" and h["src"] != "couch"]
    if pierce:
        print("\nPierce thrusts and missiles on players by relative speed (m/s):")
        for low, high in SPEED_BUCKETS:
            bucket = [h for h in pierce if h["victim"] == "human" and low <= h["speed"] < high]
            summarize("%2d-%-3s" % (low, high if high < 999 else "+"), bucket)

        print("\nPierce thrusts and missiles on players by body part and relative speed (m/s):")
        for part in PARTS:
            for low, high in SPEED_BUCKETS:
                bucket = [h for h in pierce if h["victim"] == "human" and h.get("part") == part and low <= h["speed"] < high]
                summarize("%-14s %2d-%-3s" % (part, low, high if high < 999 else "+"), bucket)

        print("\nPierce thrusts and missiles on players by weapon:")
        weapons = defaultdict(list)
        for h in pierce:
            if h["victim"] == "human":
                weapons[h["weapon"]].append(h)
        for weapon in sorted(weapons, key=lambda w: -len(weapons[w])):
            summarize(weapon, weapons[weapon])

    ridden = [h for h in hits if h.get("ahorse", "none") != "none" and "aspeed" in h]
    if ridden:
        print("\nAttacker's own speed by horse (m/s; logged since horse tracking was added):")
        horses = defaultdict(list)
        for h in ridden:
            horses[h["ahorse"]].append(float(h["aspeed"]))
        for horse in sorted(horses, key=lambda x: -max(horses[x])):
            v = sorted(horses[horse])
            print("  %-32s %4d hits | median %5.1f | 90th pct %5.1f | max %5.1f" % (
                horse, len(v), v[len(v) // 2], v[int(len(v) * 0.9)], v[-1]))

    couches = [h for h in hits if h["src"] == "couch" and h.get("tgt") != "shield"]
    if couches:
        print("\nCouches and braces:")
        for h in sorted(couches, key=lambda h: h["speed"]):
            print("  %-6s %5.1f m/s | armor %3.0f | %6.1f -> %6.1f | %s" % (
                h["victim"], h["speed"], h["armor"], h["before"], h["after"], h["weapon"]))


if __name__ == "__main__":
    main()
