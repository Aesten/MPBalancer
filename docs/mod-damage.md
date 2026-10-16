# How MPBalancer processes a hit

This explains exactly what MPBalancer changes in the game's damage calculation: which path each kind of hit takes, where each setting applies, how the curves are evaluated, and what damage to expect. The vanilla steps referred to here (step 1–7) are described in [native-damage.md](native-damage.md). Every setting can be changed in-game with `!b <setting> <value>` (see the README).

## At a glance

The mod hooks into two places in the damage pipeline, plus horse stats:

```
engine collision
  │
  ├─ 1. relative speed
  ├─ 2. magnitude ............................ swings: vanilla                                   (BalancedStrikeMagnitudeModel)
  │                                            melee pierce thrusts: vanilla strike × thrust_curve_lance or
  │                                            thrust_curve_spear (closing speed) × weapon_mult, at most vanilla
  ├─ 3–6. hit quality, damage factor, armor, body part (vanilla)
  └─ 7. perks (vanilla), then MPBalancer's final-damage stage:                             (BalancedApplyDamageModel)
         shield hit?                    → unchanged
         couched lance / braced polearm → × couch, then couch_cap × tanh(damage / couch_cap)
         missile, pierce                → × pierce_missile
         anything else                  → unchanged
       → damage dealt

horse stats (any time they're computed): MountSpeed × horse_speed, then soft cap horse_knee → horse_cap
```

Melee thrusts are shaped at the strike (step 2): everything after it (armor, body part, perks) is vanilla and, in multiplayer, proportional, so the speed curves read directly as "% of vanilla damage". Missiles and couches are shaped on the final damage.

## Which path a hit takes

The checks are made in this order, on the damage after vanilla step 7:

| Hit | What the mod does |
|---|---|
| Blocked by a shield | Nothing (shield damage stays vanilla) |
| Couched lance or braced polearm | Couch curve, on players and horses |
| Arrow, bolt or thrown weapon (pierce) | × `pierce_missile` |
| Melee thrust that lands as pierce | % of vanilla by closing speed at the strike (`thrust_model` 1) |
| Swing (cut or blunt) | Nothing |
| Arm/hilt hits, non-tip thrusts that land blunt, kicks, shield bashes, horse charges, falls | Nothing |

"Pierce" is the effective damage type the game uses: arm and hilt hits, kicks and bashes, falls and horse charges count as blunt even with a pierce weapon.

## Melee thrusts

```
strike = vanilla's strike × curve(closing speed) × weapon_mult[weapon id], at most vanilla's strike
         curve: on foot thrust_curve_foot; mounted, thrust_curve_lance for lances (weapons that can couch),
                thrust_curve_spear for everything else
damage = strike → vanilla: armor, body part
```

- **Vanilla's strike** is `0.0625 × (weight + 2.5) × (thrust speed + closing speed / 2)² × thrust damage factor`, with the thrust speed scaled by how far the thrust had come. With no closing speed that is exactly the weapon's **listed thrust damage** (`Crafting.CalculateThrustBaseDamage`). Perks that strengthen the speed bonus lengthen the closing speed term, and they stay in: Lancer (heavy cavalry, comes with the heavy lance) ×1.153 for Aserai, Khuzait and Sturgia and ×1.072 for Vlandia; Line Breaker (Battanian and Khuzait light cavalry, comes with their cavalry spear) and Ride Like the Wind ×1.225.
- **Closing speed** is vanilla's: the attacker/victim relative velocity (the mount's when riding) projected on the thrust's direction, +0.2 for alignment, without the perks. A horse at full speed thrusting straight into a standing target is about 12–14 m/s; two horses meeting head-on, up to about 25. The `!b debug` readout and the hit log (`closing` column) show it.
- **Why a % of vanilla:** vanilla adds half the closing speed to the thrust speed before squaring, and multiplies by the weapon's weight, so damage runs away at speed (×4–6 at 12 m/s, ×9–11 at 25). The curves keep slow hits close to the standing hit and let speed add steadily up to about 13 m/s, then less and less. Vanilla's order between weapons stays: heavier weapons, slower thrusts (which gain more from speed) and speed bonus perks still hit harder.
- **Lances** (a couch usage: the heavy and long lances, not the Heavy Kontos, which only braces) have their own curve; it's the same as the spears' for now.
- **Armor** is vanilla's, applied to the strike: in multiplayer one value per class (class armor + perks), the same on every body part, and pierce damage × `0.25 × 100/(100+A) + 0.75 × 45/(45+A)`: × 0.84 at 10 armor, 0.68 at 25, 0.57 at 40, 0.50 at 55.
- **Horses** take the same curves as players, × `thrust_horse` (1.15), still capped at vanilla.
- **Never more than vanilla:** curves above 100% and `weapon_mult` above 1 are capped per hit at vanilla's strike.
- `thrust_model 0` puts vanilla back (`!b vanilla` does).

### Worked example

Aserai Heavy Lance (listed one-handed thrust 34, Lancer perk), full thrust to the head at 13.1 m/s closing speed, 55 armor:

| Step | Factor | Damage |
|---|---|---|
| Vanilla strike | 34 × ((6.04 + 1.153 × 6.55) / 6.04)² | 172.4 |
| `thrust_curve_lance` at 13.1 m/s | 71.6% | 123.5 |
| Armor 55 | × 0.499 | 61.6 |
| Head | × 1.25 | **77** |

Vanilla would deal 107.

## How curves work

`thrust_curve_spear` and `thrust_curve_lance` are % of vanilla by closing speed: 26 values, one per m/s from 0 to 25 (`!b thrust_curve_spear 100 100 100 ...`), held at the last value above 25; `x:percent` pairs work too, interpolated in straight lines between points. `scripts/curve-editor.py` edits them, and they're stored in `src/MPBalancer/defaults.txt`.

The defaults (the same for spears and lances for now) are shaped on the damage rather than on vanilla: a thrust stays close to the standing hit up to about 2 m/s (vanilla already gains a lot at low speed), gains about 30% by 4 m/s, rises steadily up to about 13 m/s and flattens after. That's why the percentage dips at low speed and rises again up to 8: vanilla jumps at low speed. Damage never goes below the standing hit and still rises with speed for every weapon.

| Closing speed (m/s) | 0 | 2 | 4 | 6 | 8 | 10 | 12 | 14 | 16 | 18 | 20 | 25 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `thrust_curve_spear`, `thrust_curve_lance` | 100% | 80% | 76% | 78% | 79% | 78% | 74% | 69% | 64% | 58% | 53% | 42% |

They're fitted on the Aserai Heavy Lance: a head hit on armor 55 at 13.1 m/s does 77 (vanilla 107). With one curve for everything, spears and lances differ as vanilla makes them (weight, thrust speed, perks) and by `thrust_damage`. Thrusts on horses get `thrust_horse` (1.15) on top, capped at vanilla: the curves were set on players, and without it horses took 5–18% less than in the 12–13 October matches.

**On foot** (infantry, archers in melee, dismounted cavalry; every weapon, sword stabs included), thrusts use `thrust_curve_foot`: the 12-13 October baseline's player curve, interpolated per m/s (72% of vanilla standing, 63% at 4 m/s, 57% at 8, 52% at 12, 46% from 20). Infantry closing speeds are low (median 2.8 m/s in those matches, 90% under 6.4), so it mostly acts between 60% and 72%. In the baseline the armor curve added 16-27% on top, so infantry stabs are weaker than then (about 84% of vanilla at 0-2 m/s then, 72-67% now). Before this curve, infantry used the mounted spear curve (about 79% of vanilla for the median stab).

Compared with vanilla (unarmored body hits, full one-handed thrusts; vanilla in brackets):

| Weapon (listed thrust, perk) | 0 m/s | 4 m/s | 8 m/s | 12 m/s | 16 m/s | 20 m/s | 25 m/s |
|---|---|---|---|---|---|---|---|
| Western Long Spear (29) | 29 (29) | 36 (47) | 55 (69) | 71 (96) | 81 (127) | 86 (163) | 90 (213) |
| Battanian Long Spear (29+1, `thrust_damage`) | 30 (29) | 36 (46) | 56 (68) | 72 (93) | 81 (123) | 86 (156) | 89 (204) |
| Western Cavalry Spear (33) | 33 (33) | 40 (53) | 62 (78) | 80 (107) | 90 (141) | 96 (180) | 99 (235) |
| Battanian Cavalry Spear (33, Line Breaker) | 33 (33) | 44 (58) | 72 (91) | 97 (131) | 114 (178) | 124 (233) | 131 (311) |
| Western Lance (31+7) | 38 (31) | 49 (53) | 79 (81) | 105 (115) | 121 (155) | 131 (201) | 138 (267) |
| Vlandian Heavy Lance (33+1, Lancer) | 34 (33) | 47 (61) | 79 (97) | 108 (141) | 127 (194) | 139 (254) | 149 (342) |
| Aserai Heavy Lance (34, Lancer) | 34 (34) | 49 (65) | 84 (106) | 116 (157) | 139 (217) | 153 (288) | 164 (390) |

Vanilla in brackets is the vanilla weapon; weapons raised by `thrust_damage` can beat it at very low speed.

## Per-weapon multiplier

- **`weapon_mult`** (default none): extra multiplier on melee thrusts per item id, for exceptions. Capped at vanilla, so above 1 it only helps where the curve is below 100%.

## The hit log's vanilla column

Each hit's `vanilla` is what vanilla would have dealt for the same hit (same weapon, speeds, timing, body part, armor and perks): the mod's damage with every per-hit change undone. They all scale the damage proportionally, so this is exact up to the game's rounding to whole points.

| Undone | How |
|---|---|
| Thrust curves (`thrust_model`, `thrust_curve_*`, `thrust_horse`, `weapon_mult`) | vanilla's strike over ours, recorded when the strike is computed (`VanillaEstimate`) |
| `thrust_damage` | the weapon's vanilla thrust damage factor over the current one |
| `missile_head`, `missile_head_horse` | vanilla's body part multiplier over ours |
| `mount_damage_polearm` | it acts after the column is taken (the column keeps the perk's bonus) |
| `pierce_missile`, couch curve | they act after the column is taken |

Not undone, because they change the hit itself rather than its damage: horse speed and handling (a slower horse hits at a lower speed), item weights, horse health.

## Soft caps

The couch curve (`couch_cap`, knee 0) and the horse speed cap (`horse_knee` / `horse_cap`) use the same formula:

```
value ≤ knee:  unchanged
value > knee:  knee + (cap − knee) × tanh((value − knee) / (cap − knee))
```

It's smooth at the knee, gets flatter as the value rises, and never exceeds the cap. A cap of 1000 turns it off.

The couch curve has knee 0, so it compresses from the start: `200 × tanh(damage / 200)`. The pierce cap on players (`pierce_knee`/`pierce_cap`, 90 → 130 until 15 October) was removed: every class has 100 health, so it hardly changed kills.

## Other attacks

- **Swings:** vanilla. The balancing targets pierce; cut damage was never the issue.
- **Missiles:** `× pierce_missile` (1, vanilla; 0.85 until 15 October) on the final damage. Missile speed physics is vanilla. Headshots (head and neck) are set where the game picks the body part multiplier (`GetDamageMultiplierForBodyPart`, before armor): `missile_head` 1.5 on players (vanilla 2), `missile_head_horse` 1.2 on horses (vanilla, the same as for any pierce hit to a horse's head). The hit log's `vanilla` column uses vanilla's multiplier.
- **Anti-horse infantry perks:** Cavalry Breaker (heavy infantry, Empire shock infantry: +100% / +75% against horses), Spearman and Farmer (light infantry: +50–75%) are `MountDamage` perk effects without a weapon filter: vanilla applies them to any melee weapon (Cavalry Breaker) or any attack, thrown and ranged included (Spearman, Farmer). With `mount_damage_polearm` 1, an infantry class's bonus only counts with a polearm in hand (one- or two-handed, by grip), the spear or pike those perks come with; otherwise the game's damage multiplier (1 + damage bonuses + damage taken) is recomputed without it. Ranged classes' perks are untouched.
- **Couched lances and braced polearms:** a weapon's `thrust_damage` applies to its couched and braced grips too; then `× couch` (1.0), then `200 × tanh(damage / 200)` (`couch_cap`), on players and horses. Couch damage is almost entirely speed:

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

## Horse handling and bumps

```
maneuver   = vanilla MountManeuver × horse_maneuver (0.95)
accel time = vanilla TopSpeedReachDuration × horse_accel_time (1)
```

Both apply to every horse immediately. `horse_turn_keep_speed` (how sharp a horse can turn before losing speed) and `couch_min_speed` (9 m/s) are engine parameters, overridden on the server only; clients keep their own values. All of these except `horse_maneuver` are vanilla by default.

**Bumps.** A bump happens when a horse runs into someone faster than `bump_speed` (4.8 m/s; vanilla 4.3, the horse Monster's `relative_speed_limit_for_charge`; camels keep their 5.0/4.3 ratio). The engine reads it when a horse spawns, so changes apply from the next spawn. The speed is the closing speed: the horse's velocity minus the victim's velocity along the horse's direction.

The engine also only bumps while the horse is in a running gait: a walking or trotting horse doesn't bump, even above the threshold (the same state also changes its animation). In tests the lowest bump was 5.9 m/s on an Aserai agile horse with the vanilla 4.3. This gate is inside the engine and can't be changed from the server, so `bump_speed` only matters above it: it can raise the requirement but can't make slower horses bump.

Bump damage and knockdowns stay vanilla: damage = (closing speed × angle)² × angle × charge_damage/100, blunt; any damaging, roughly head-on bump knocks back; knockdown needs about 10 damage on foot and 20 on a rider. Because damage grows with speed², the horse speed cap already reduces knockdowns.

## Settings reference

| Setting | Default | Applies to | Where |
|---|---|---|---|
| `thrust_model` | 1 (0 = vanilla) | melee pierce thrusts | step 2 |
| `thrust_curve_spear` | see above | melee pierce thrusts with weapons that can't couch | step 2 |
| `thrust_curve_lance` | see above | melee pierce thrusts with lances | step 2 |
| `thrust_curve_foot` | see above | melee pierce thrusts by attackers on foot | step 2 |
| `thrust_horse` | 1.15 | melee pierce thrusts on horses, on top of the curves | step 2 |
| `weapon_mult` | none | melee pierce thrusts with listed weapons | step 2 |
| `pierce_missile` | 1 | arrows, bolts, thrown weapons | final damage |
| `missile_head`, `missile_head_horse` | 1.5, 1.2 | pierce missiles to the head and neck, players / horses | body part multiplier |
| `mount_damage_polearm` | 1 | infantry perks' bonus against horses | perk multiplier (final damage) |
| `couch`, `couch_cap` | 1, 200 | couched lances, braced polearms (all victims) | final damage |
| `horse_speed`, `horse_knee`, `horse_cap` | 0.92, 12.4, 14.2 | horse top speed | horse stats |
| `horse_maneuver`, `horse_accel_time` | 0.95, 1 | horse turning and acceleration | horse stats |
| `horse_turn_keep_speed`, `couch_min_speed` | 0.6, 9 (vanilla) | horse speed loss on turns, couch minimum speed | engine parameters |
| `bump_speed` | 4.8 | minimum speed for a bump | horse Monster, at spawn |

## Expected damage

Full thrusts to the head on players with the defaults, at 6 / 12 / 18 m/s closing speed (body hits 20% lower):

| Armor | Western Cavalry Spear (33) | Battanian Cavalry Spear (33, Line Breaker) | Western Lance (31+7) | Aserai Heavy Lance (34, Lancer) |
|---|---|---|---|---|
| 0 | 63 / 100 / 117 | 72 / 122 / 149 | 79 / 131 / 159 | 82 / 146 / 183 |
| 10 | 53 / 84 / 98 | 60 / 102 / 125 | 67 / 110 / 133 | 69 / 122 / 154 |
| 25 | 43 / 68 / 80 | 49 / 83 / 102 | 54 / 90 / 108 | 56 / 99 / 125 |
| 40 | 36 / 57 / 67 | 41 / 70 / 86 | 46 / 76 / 91 | 47 / 84 / 106 |
| 55 | 31 / 50 / 58 | 36 / 61 / 74 | 40 / 65 / 79 | 41 / 73 / 91 |

Thrusts that don't land with the tip, or hit with the arm, are multiplied by 0.15 by the game, so occasional 3–6 damage hits are expected.

## Why it's built this way

- **Thrusts are a % of vanilla, falling with speed.** An earlier version scaled the speed fed into vanilla's formula, which took proportionally more from lances and removed the small walking bonus. Another bent the final damage with "% of vanilla" curves plus armor and per-weapon corrections, which pulled against each other. The one before this took the weapon's standing strength (its listed thrust damage) times a speed curve of its own: clean, but it flattened every weapon toward its listed damage, so strong lances didn't feel strong, and under the vanilla cap lances only left vanilla around 10 m/s, with a sharp bend. Taking vanilla's strike for the hit keeps vanilla's spread between weapons, and the curves control how much speed adds, shaped on the damage itself.
- **Horse speed uses a soft cap rather than a flat cut**, so the fastest horses are slowed while slow horses stay comfortably above the 9 m/s couch minimum.

## Possible next levers

- **Ranged damage vs. the cavalry nerf:** cavalry thrusts were cut to roughly 50–90% of vanilla to players and horses are slower, while arrows, bolts and javelins only lost 15% (`pierce_missile`). Ranged may need its own balancing to stay fair against the weakened cavalry, for example a different `pierce_missile`, separate values per missile type, or the missile speed term.
