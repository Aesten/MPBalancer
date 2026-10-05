# How MPBalancer processes a hit

This explains exactly what MPBalancer changes in the game's damage calculation: which path each kind of hit takes, where each setting applies, how the curves are evaluated, and what damage to expect. The vanilla steps referred to here (step 1–7) are described in [native-damage.md](native-damage.md). Every setting can be changed in-game with `!b <setting> <value>` (see the README).

## At a glance

The mod hooks into two places in the damage pipeline, plus horse stats:

```
engine collision
  │
  ├─ 1. relative speed
  ├─ 2. magnitude ............................ swings only: speed × swing (default 1 = vanilla) (BalancedStrikeMagnitudeModel)
  ├─ 3–6. hit quality, damage factor, armor, body part (vanilla)
  └─ 7. perks (vanilla), then MPBalancer's final-damage stage:                             (BalancedApplyDamageModel)
         shield hit?                    → unchanged
         couched lance / braced polearm → × couch, then soft cap toward couch_cap
         missile, pierce                → × pierce_missile
         melee thrust, pierce           → × thrust_curve(speed) × thrust_armor_curve(armor)   on players
                                          × thrust_curve_horse(speed)                         on horses
                                          × weapon factors (thrust_spread, lance, weapon_mult)
         anything else                  → unchanged
         then, pierce on players        → soft cap: pierce_knee → pierce_cap
       → damage dealt

horse stats (any time they're computed): MountSpeed × horse_speed, then soft cap horse_knee → horse_cap
```

Because thrusts are shaped at the very end, every number in that branch is a plain "% of what vanilla would have dealt for this exact hit": vanilla still decides the physics, timing, impact point, armor and body part, and the mod scales the result.

## Which path a hit takes

The checks are made in this order, on the damage after vanilla step 7:

| Hit | What the mod does |
|---|---|
| Blocked by a shield | Nothing (shield damage stays vanilla; only a non-default `swing` would change swings blocked by shields) |
| Couched lance or braced polearm | Couch curve, on players and horses |
| Arrow, bolt or thrown weapon (pierce) | × `pierce_missile`, then the pierce soft cap on players |
| Melee thrust that lands as pierce | Thrust curves and weapon factors, then the pierce soft cap on players |
| Swing (cut or blunt) | Nothing by default (`swing` is 1) |
| Arm/hilt hits, non-tip thrusts that land blunt, kicks, shield bashes, horse charges, falls | Nothing |

"Pierce" is the effective damage type the game uses: arm and hilt hits, kicks and bashes, falls and horse charges count as blunt even with a pierce weapon.

## Melee thrusts

```
damage dealt = vanilla damage
             × thrust_curve(relative speed)          players only
             × thrust_armor_curve(armor at hit)       players only
             × thrust_curve_horse(relative speed)     horses only
             × (listed thrust damage / reference) ^ thrust_spread
             × lance                                  if the weapon can couch
             × weapon_mult[weapon id]                 if listed
then, on players: soft cap (pierce_knee 90 → pierce_cap 130)
```

- **Relative speed** is the length of the attacker/victim relative velocity in m/s (the mount's velocity when riding), the same "speed" the hit log shows. It isn't projected onto the blow direction. A horse at full speed hitting a standing target is about 12–14 m/s; 20+ m/s means both sides are moving into each other.
- **Armor** is the armor of the body part that was hit, the same value vanilla uses: a head without a helmet counts as 0 even in heavy body armor.
- **Vanilla physics is untouched.** Weapons keep their vanilla relation to each other at every speed: heavy lances gain more from speed than spears, exactly as in vanilla.

### Worked example

Western lance (listed thrust 31, a lance), head hit at 12 m/s on 53 armor; vanilla would deal 72:

| Step | Factor | Damage |
|---|---|---|
| Vanilla | | 72 |
| `thrust_curve` at 12 m/s | 52% | 37.4 |
| `thrust_armor_curve` at 53 | 116% | 43.4 |
| `thrust_spread` (31 / lance median 31) ^ 0.5 | 1.00 | 43.4 |
| `lance` | 1.15 | 49.9 |
| `weapon_mult` | not listed | 49.9 |
| Soft cap | below 90, unchanged | **50** |

## How curves work

A curve is a list of `x:percent` points, for example `thrust_curve 0:72 2:67 4:63 6:60 8:57 10:54 12:52 16:49 20:46`:

- **x** is the relative speed in m/s for `thrust_curve` and `thrust_curve_horse`, and the armor at the hit location for `thrust_armor_curve`.
- **Between two points** the value is interpolated in a straight line: at 3 m/s, `thrust_curve` gives 65%.
- **Below the first point and above the last** the end value is held: above 20 m/s, `thrust_curve` stays at 46%; above 53 armor, the armor curve stays at 116%.
- **Points can be added or removed freely** and are sorted automatically: `!b thrust_curve 0:70 4:61 20:45` is a valid three-point curve. `0:100` (a single point at 100%) means vanilla.

The default curves:

| Speed (m/s) | 0 | 2 | 4 | 6 | 8 | 10 | 12 | 16 | 20+ |
|---|---|---|---|---|---|---|---|---|---|
| `thrust_curve` (players) | 72% | 67% | 63% | 60% | 57% | 54% | 52% | 49% | 46% |
| `thrust_curve_horse` | 100% | 94% | 89% | 85% | 81% | 78% | 76% | 73% | 70% |

| Armor | 0 | 10 | 20 | 35 | 53+ |
|---|---|---|---|---|---|
| `thrust_armor_curve` (players) | 125% | 120% | 120% | 127% | 116% |

The armor curve is a correction on top of vanilla's armor formula, not a replacement: vanilla already reduces pierce damage by 27% at 20 armor and 49% at 53. Values above 100% at high armor give back part of that, so armor still matters but heavy armor isn't immune to thrusts. Combined with the speed curve, a thrust on players ends up between about 90% (unarmored, standing) and 53% (53 armor, 20 m/s) of vanilla.

## Weapon factors (melee thrusts only)

- **`thrust_spread`** (default 0.5): `× (listed thrust damage / reference) ^ thrust_spread`. The listed thrust damage is the number in the loadout screen (for the usage being used, one- or two-handed). The reference is `thrust_ref`, or when that's 0, the median listed thrust damage of the weapon's group, logged at startup: one-handed polearms 29, one-handed lances 31, two-handed polearms 36, two-handed lances 36. Lances (items that can couch) are grouped separately because they list lower thrust damage than spears. Example: a spear listed at 33 among one-handed polearms gets (33 / 29) ^ 0.5 = × 1.07. `0` turns it off.
- **`lance`** (default 1.15): extra multiplier for weapons that can couch.
- **`weapon_mult`** (default `mp_aserai_heavy_lance:0.85`): extra multiplier per item id. The Aserai heavy lance is the strongest weapon in vanilla physics (heaviest, slowest thrust), so speed adds the most to it.

## Soft caps

`pierce_knee` / `pierce_cap`, `couch_knee` / `couch_cap` and `horse_knee` / `horse_cap` all use the same formula:

```
value ≤ knee:  unchanged
value > knee:  knee + (cap − knee) × tanh((value − knee) / (cap − knee))
```

It's smooth at the knee, gets flatter as the value rises, and never exceeds the cap. Setting both to 1000 turns it off.

| Input | 90 | 100 | 120 | 150 | 200+ |
|---|---|---|---|---|---|
| Pierce cap (knee 90, cap 130) | 90 | 100 | 115 | 126 | 130 |

With knee 0 (the couch default) it compresses from the start: `200 × tanh(damage / 200)`.

## Other attacks

- **Swings:** vanilla by default. The balancing targets pierce; cut damage was never the issue. `swing` scales the movement speed added to a swing at step 2 (standing swings are never affected); it was 0.75 in earlier versions, a leftover from the first general speed-bonus nerf, and trimmed mounted swings by about 5–10%. It's the only change inside the physics, so it would also affect swings blocked by shields.
- **Missiles:** `× pierce_missile` (0.85) on the final damage, then the pierce soft cap on players. Missile speed physics is vanilla.
- **Couched lances and braced polearms:** `× couch` (1.0), then the soft cap with knee 0 and cap 200, on players and horses. Couch damage is almost entirely speed:

| Speed (m/s) | 4 | 6 | 8 | 9 | 10 | 11 | 13 | 16 | 20 |
|---|---|---|---|---|---|---|---|---|---|
| Vanilla (estimated from logged hits) | 37 | 80 | 139 | 175 | 214 | 257 | 355 | 529 | 813 |
| With the default curve | 36 | 76 | 121 | 141 | 158 | 172 | 189 | 198 | 200 |

A couch still kills an unarmored player (100 HP) from about 8 m/s; lower `couch` to move that point.

## Horse speed

```
top speed = vanilla MountSpeed × horse_speed (0.92), then soft cap (horse_knee 12.4 → horse_cap 14.2 m/s)
```

Changes apply to every horse on the field immediately. Measured and expected top speeds:

| Horse (vanilla top speed) | With the defaults |
|---|---|
| Khuzait tougher, Empire war (13.0) | about 12.0 |
| Khuzait agile, Battanian pony (14.0) | about 12.9 |
| Aserai war agile (15.3) | about 13.7 |
| Aserai agile (16.6) | about 14.1 |

Keep top speeds well above 9 m/s, the minimum to hold a couched lance (a native rule the server can't change). Slower horses also mean lower relative speeds, so they lower thrust and couch damage on top of the curves.

## Settings reference

| Setting | Default | Applies to | Where |
|---|---|---|---|
| `swing` | 1 (vanilla) | swings (all victims, shields too) | step 2 |
| `thrust_curve` | see above | melee pierce thrusts on players | final damage |
| `thrust_armor_curve` | see above | melee pierce thrusts on players | final damage |
| `thrust_curve_horse` | see above | melee pierce thrusts on horses | final damage |
| `thrust_spread`, `thrust_ref` | 0.5, 0 (auto) | melee pierce thrusts | final damage |
| `lance` | 1.15 | melee pierce thrusts with weapons that can couch | final damage |
| `weapon_mult` | `mp_aserai_heavy_lance:0.85` | melee pierce thrusts with listed weapons | final damage |
| `pierce_missile` | 0.85 | arrows, bolts, thrown weapons | final damage |
| `pierce_knee`, `pierce_cap` | 90, 130 | all pierce damage on players, last | final damage |
| `couch`, `couch_knee`, `couch_cap` | 1, 0, 200 | couched lances, braced polearms (all victims) | final damage |
| `horse_speed`, `horse_knee`, `horse_cap` | 0.92, 12.4, 14.2 | horse top speed | horse stats |

## Expected damage

Head hits on players with the defaults, at 12 / 14 / 16 m/s (body hits are 20% lower):

| Armor | Western lance | Aserai heavy lance | Battanian cavalry spear |
|---|---|---|---|
| 0 | 105 / 115 / 122 | 120 / 126 / 128 | 109 / 119 / 125 |
| 10 | 85 / 97 / 107 | 103 / 115 / 123 | 90 / 102 / 112 |
| 20 | 74 / 84 / 94 | 90 / 103 / 114 | 78 / 88 / 99 |
| 35 | 65 / 74 / 83 | 79 / 91 / 103 | 69 / 78 / 88 |
| 53 | 50 / 57 / 63 | 61 / 70 / 79 | 52 / 60 / 67 |

These come from vanilla physics fitted on logged hits (unarmored targets; `body ≈ base × (1 + coef × speed)²`, head = body × 1.25), combined with the formulas above:

| Weapon | Listed thrust | Base (body, 0 m/s) | Coef | Vanilla body at 8 / 12 m/s |
|---|---|---|---|---|
| Aserai heavy lance | 34 | 33.5 | 0.096 | 104 / 155 |
| Battanian cavalry spear | 33 | 32.3 | 0.083 | 89 / 128 |
| Khuzait long lance | 29 | 28.6 | 0.090 | 85 / 124 |
| Sturgian long lance | 31 | 30.4 | 0.079 | 81 / 117 |
| Western long lance | 29 | 28.5 | 0.077 | 74 / 107 |
| Aserai bamboo spear | 32 | 32.2 | 0.068 | 77 / 106 |
| Khuzait long spear | 31 | 31.1 | 0.069 | 75 / 104 |
| Western long spear | 29 | 28.8 | 0.068 | 69 / 96 |

Checks against play: the speed curves were measured within 1–3 points of their values (e.g. players 70 / 63 / 57 / 51% at 0–2 / 4–6 / 8–10 / 12–16 m/s), and a session at 45+ armor measured 47 / 56 to the head at 8–12 / 12+ m/s against the modeled 50 / 57. Head-on cavalry clashes (22–25 m/s) can exceed the table.

Thrusts that don't land with the tip, or hit with the arm, are multiplied by 0.15 by the game, so occasional 3–6 damage hits are expected.

## Why it's built this way

- **Thrusts are shaped on the final damage, not in the physics.** Speed is added to the weapon's own speed before squaring, so scaling it takes proportionally more from weapons whose damage comes mostly from speed. Lances are exactly those weapons, so a physics speed factor made them weaker than spears, and a low factor also removed the walking bonus that hides imperfect hits. A "% of vanilla by speed" curve keeps every weapon's vanilla relation and can take any shape.
- **Horses get their own curve** because the player curve made proper thrusts on horses far too weak, especially at low speed. Thrusts on horses stay at vanilla when standing and lose less at speed than thrusts on players.
- **The armor curve** exists because the same speed cut at every armor level made unarmored players barely more vulnerable than armored ones, while heavily armored players took 5–6 thrusts to kill.
- **Horse speed uses a soft cap rather than a flat cut**, so the fastest horses are slowed while slow horses stay comfortably above the 9 m/s couch minimum.

## Possible next levers

- **Ranged damage vs. the cavalry nerf:** cavalry thrusts now deal roughly 50–90% of vanilla to players and horses are slower, while arrows, bolts and javelins only lost 15% (`pierce_missile`). Ranged may need its own balancing to stay fair against the weakened cavalry, for example a different `pierce_missile`, separate values per missile type, or the missile speed term.
- **Missile headshots:** the missile pierce head/neck multiplier of 2.0 (`GetDamageMultiplierForBodyPart`).
