# Balance settings history

Each set of defaults that was played, with the sessions it was tested in, so later changes can be compared with the
data recorded under it (MPBalancer's hit log tags every hit with its settings number; MPStats' match records have
the times). Newest first. "Vanilla" is the value that leaves the game unchanged.

## Round 2: thrust model (from 14 October 2026, testing)

Melee pierce thrusts do a percentage of vanilla's strike for the same hit (speed bonus perks included) by the closing
speed along the thrust, from one curve for lances (weapons that can couch) and one for everything else; vanilla's
armor and body part apply after it (`docs/mod-damage.md`, "Melee thrusts"). Defaults live in
`src/MPBalancer/defaults.txt`.

- New `thrust_model = 1` (0 = vanilla).
- `thrust_curve` / `thrust_curve_horse` replaced by `thrust_curve_spear` / `thrust_curve_lance` (players and horses
  alike, both the same for now): shaped on the damage, within a couple of points of the standing hit up to 4 m/s,
  rising faster and faster to about 9 m/s, flattening after 13; never below the standing hit, never above vanilla.
  `100 88.7 79.2 71.1 64.2 62.5 62.8 64.3 66.2 67.9 69 69.1 68.5 67.2 65.3 63.1 60.6 58.1 55.5 52.9 50.4 48 45.7 43.6
  41.5 39.6`. Fitted on the Aserai Heavy Lance: head hit on armor 55 at 13.1 m/s = 72 (vanilla 107).
  Tried live first: vanilla up to 4 m/s then a steepening drop (the Aserai lance measured 96 there, wanted 72), and a
  version that followed vanilla to 4 m/s (low-speed stabs did too much).
- New `thrust_damage = mp_battania_long_spear:30` (29 vanilla; two-handed 35 instead of 34; 31 was too much).
- Lance buffs in `thrust_damage` (14 October): Khuzait Long Lance 29+7, Vlandian Heavy Lance 33+1, Sturgian Long
  Lance 31+6, Western Lance 31+7, Khuzait Lance 31+7, Bamboo Lance 30+5, Sturgian Lance 29+4, Vlandian Long Lance
  29+5. With one curve for spears and lances, Line Breaker spears had passed most lances; these put every lance
  above plain spears (12 m/s head hit on armor 55: lances 54-67, Line Breaker spears 56, plain spears 37-46).
- 15 October: `pierce_missile` 1 (was 0.85); new `missile_head = 1.5` (missile headshots on players, vanilla 2),
  `missile_head_horse = 1.2` (vanilla), `mount_damage_polearm = 1` (infantry anti-horse perks only with polearms);
  pierce soft cap off (`pierce_knee`/`pierce_cap` 1000, were 90/130: every class has 100 health); `horse_maneuver` 1
  (vanilla; was 0.95). New curve (steadier rise from 2 m/s, +5 at speed: Aserai Heavy Lance 77 on a head,
  armor 55, 13.1 m/s): `100 88.7 79.8 76.4 75.8 76.6 77.8 78.8 79.2 78.9 77.9 76.3 74.3 71.9 69.3 66.6 63.8 61
  58.3 55.7 53.1 50.7 48.4 46.2 44.1 42.1`; new `thrust_horse = 1.15` (thrusts on horses were 5-18% below the
  baseline matches with the player curves). New `thrust_curve_foot`: thrusts by attackers on foot use the baseline's
  `thrust_curve` (0:72 2:67 4:63 6:60 8:57 10:54 12:52 16:49 20:46) per m/s, without its armor curve (weaker than
  then); before, they used the mounted spear curve.
- New `small_shields = 2`: the Khuzait light cavalry's Small Shield perk gives two shields.
- Removed: `thrust_armor_curve`, `thrust_spread`, `thrust_ref`, `lance` (corrections for vanilla's speed² × weight
  term, which the curves now handle). `weapon_mult` default `none`.
- `horse_maneuver` 0.95 (was 0.9).

An intermediate version (the weapon's listed thrust damage × a rising speed curve, capped at vanilla) was deployed
on 14 October but not played: it flattened weapons toward their listed damage.

To fill in after testing: sessions, data, feedback.

## Baseline: 12–13 October 2026 (commit 9500411)

The defaults in code as of commit `9500411` ("Added Khuzait shield + horse tweaks"). Played in 8 matches over two
sessions, all with these exact values (settings number 1 in each run's `settings-<start>.csv`):

- 12 October: Echerion ×2 (Vlandia vs Empire), Fort of Honor ×2 (Sturgia vs Battania); 12–15 players. The Khuzait
  `item_weight` and `horse_hp` values weren't in yet (no Khuzait played).
- 13 October: Town Outskirts ×2 (Khuzait vs Aserai), Echerion ×2 (Battania vs Vlandia); 10–15 players. All values
  below.

Reports: `mpStats/reports/2026-10-12*.pdf`, `2026-10-13*.pdf` (local), changelog
`reports/2026-10-12-changelog.md`.

| Setting | Value | Vanilla | What it does |
|---|---|---|---|
| `swing` | 1 | 1 | swing speed bonus multiplier (swings unchanged) |
| `thrust_curve` | `0:72 2:67 4:63 6:60 8:57 10:54 12:52 16:49 20:46` | `0:100` | melee pierce thrusts on players: % of vanilla damage by relative speed (m/s) |
| `thrust_armor_curve` | `0:125 10:120 20:120 35:127 53:116` | `0:100` | extra % on top, by armor at the hit location |
| `thrust_curve_horse` | `0:100 2:94 4:89 6:85 8:81 10:78 12:76 16:73 20:70` | `0:100` | melee pierce thrusts on horses, by relative speed |
| `thrust_spread` | 0.5 | 0 | thrust damage × (listed thrust damage / reference) ^ this |
| `thrust_ref` | 0 (auto: median per weapon group) | 0 | reference listed thrust damage for `thrust_spread` |
| `lance` | 1.15 | 1 | extra multiplier on thrusts with lances (couchable) |
| `weapon_mult` | `mp_aserai_heavy_lance:0.85` | none | extra multiplier on thrusts per weapon |
| `pierce_missile` | 0.85 | 1 | arrows, bolts, throwing weapons |
| `pierce_knee` / `pierce_cap` | 90 / 130 | off | pierce damage to players above 90 compressed toward 130 |
| `couch` | 1 | 1 | couch/brace multiplier before the couch curve |
| `couch_knee` / `couch_cap` | 0 / 200 | off | couch/brace curve, approaches 200 |
| `horse_speed` | 0.92 | 1 | horse top speed multiplier |
| `horse_knee` / `horse_cap` | 12.4 / 14.2 | off | horse top speeds above 12.4 m/s compressed toward 14.2 |
| `horse_maneuver` | 0.9 | 1 | horse turning multiplier |
| `horse_accel_time` | 1 | 1 | horse acceleration |
| `horse_turn_keep_speed` | 0.6 | 0.6 | engine: how sharp a horse turns before losing speed |
| `couch_min_speed` | 9 | 9 | engine: minimum horse speed to couch |
| `bump_speed` | 4.8 | 4.3 | minimum relative speed for a horse bump |
| `item_weight` | `mp_curved_round_shield:1` | none | Khuzait Rabble's shield 1 kg instead of 5 (13 October) |
| `horse_hp` | `mp_khuzait_horse_agile:215 mp_khuzait_horse:215 mp_khuzait_horse_charger:245` | none | Khuzait horses' health, from 185 (13 October) |

**What the data showed under these values** (pierce damage on players, rounds only):

| | 12 October (4 matches) | 13 October (4 matches) |
|---|---|---|
| Mounted thrusts | −27% vs vanilla (40.1 → 29.4) | −28% (43.0 → 31.0) |
| Thrusts on foot | −25% | −22% |
| Missiles | −15% | −15% |
| Couches | −18% (5 hits) | −30% (3 hits) |
| Thrusts by speed (mounted) | −15% at 0–4 m/s to −39% above 16 | −16% to −42% |
| Damage per life: cavalry / ranged / infantry | 108 / 100 / 66 | 104 / 118 / 67 |
| One-shot pierce hits (100+ on a player) | 12 in vanilla → 4 | – |

Feedback: cavalry and archer players noticed the lower damage; nobody noticed the horse speed or turning changes.

**Changed live after the baseline** (13 October, from the server console, after the matches): `horse_maneuver`
0.9 → 1 (19:41) → 0.95 (19:42); settings numbers 2 and 3 of that run. 0.95 became the new code default (next
section, once the rest of the round is settled).
