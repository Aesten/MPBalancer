# How the game turns a hit into damage (vanilla multiplayer, v1.4.8)

This describes the unmodified game: a melee or missile hit on an agent, from the engine's collision data to the health that gets removed, plus the native rules for horse speed and couching. It's based on the decompiled server code in `../BannerlordDecompiled/DedicatedServerSource/`, which runs identically on clients. For what MPBalancer changes, see [mod-damage.md](mod-damage.md).

## Overview

```
engine collision (hit point, bone, attack progress, velocities, blow direction)
  │
  ├─ 1. relative speed along the blow ............... MissionCombatMechanicsHelper.ComputeBlowMagnitudeMelee
  ├─ 2. magnitude (physics: weapon speed + extra speed, weight)
  │       melee: CalculateBaseMeleeBlowMagnitude → StrikeMagnitudeModel → CombatStatCalculator
  │       missile: StrikeMagnitudeModel.CalculateStrikeMagnitudeForMissile
  ├─ 3. hit-quality multipliers (momentum, arm hit 0.15, non-tip thrust 0.15, perk weapon multiplier)
  ├─ 4. × weapon damage factor → "specialMagnitude" (clamped to 500)
  ├─ 5. armor, by damage type ........................ StrikeMagnitudeModel.ComputeRawDamage
  ├─ 6. × body-part multiplier × difficulty, rounded up . MissionCombatMechanicsHelper.ComputeBlowDamage
  └─ 7. perk modifiers ................................ AgentApplyDamageModel.ApplyGeneralDamageModifiers
        → damage dealt (rounded)
```

Steps 2, 5 and 7 go through game models, which a server mod can replace. Steps 1, 3, 4 and 6 are static code; changing them needs Harmony, but their results can be compensated in a model. Shield blocks take a separate branch after step 4 (`ComputeBlowDamageOnShield`, then `AgentApplyDamageModel.CalculateShieldDamage`), but still pass through step 7.

What the engine decides before any of this runs: hit detection, the bone hit, where on the weapon it landed, the blow direction, attack progress, block/parry/chamber outcomes, agent and missile velocities, and animation timing.

## 1. Relative speed ("extra linear speed")

```
vrel   = attackerVelocity − victimVelocity          (2D, horizontal only; the mount's velocity when riding)
align  = dot(blowDirection, normalize(vrel))
if align > 0: align = min(align + 0.2, 1)           ← up to 0.2 of bonus alignment for free
extra  = |vrel| × align                             (negative when moving apart along the blow)
```

A target running into your thrust counts the same as you running into theirs. Moving away from the target gives a negative `extra`, which reduces damage.

## 2. Magnitude

### Melee: `CalculateBaseMeleeBlowMagnitude`

```
progressEffect = SpeedGraph(attackProgress)              (0..1, from managed_core_parameters.xml)
weaponSpeed    = weaponSpeedStat / k × √progressEffect   (k = 11.76 for thrusts, 4.55 for swings)
thrust: extra ×= 0.5   (hardcoded)
swing:  extra ×= 0.7   (hardcoded); the best of 5 impact points near the hit is used
```

**Thrust** (`CombatStatCalculator.CalculateStrikeMagnitudeForThrust`):

```
magnitude = 0.0625 × (weaponWeight + 2.5) × (weaponSpeed + extra)²
```

**Swing** (`CalculateStrikeMagnitudeForSwing`): a rigid-body impact model. `extra` is added to the linear speed of the weapon's center of mass (`swingSpeed × (0.5 + CoM) + extra`), and the magnitude is the kinetic energy transferred at the impact point. It's also roughly quadratic in speed, but speed has a smaller relative effect than on thrusts, because the rotation contributes energy too.

**Speed is added before squaring, so it matters a lot.** For a thrust with weapon speed stat 90 (7.65 m/s), damage relative to the standing value is about 128% at 2 m/s, 159% at 4 m/s, 252% at 9 m/s and 318% at 12 m/s. Heavy weapons with a slow thrust (lances) gain the most, because speed is a bigger share of their total.

**The "+X% speed bonus"** in the combat log is the same calculation re-run with `extra = 0`, compared with the real one. It's only shown when the attack is more than 70% charged.

**Attack progress curve** (`SpeedGraphFunction`; it multiplies the squared speed, so it scales damage directly when standing still):

| Thrust progress | 0.05 | 0.15 | 0.30–0.85 | 0.90 | 0.95 |
|---|---|---|---|---|---|
| Damage (no movement) | 50% | 70% | 100% | 67% | 33% |

Swings: 30% at progress 0, full from 0.15 to 0.54, then down to 1% at the end. Overhead swings: 75% → full from 0.33 to 0.70 → 75%.

### Couched lance / braced polearm (`CalculateBaseBlowMagnitudeForPassiveUsage`)

Damage comes only from speed: a thrust with zero weapon speed, an effective weight of `20 / extra^0.1 + weaponWeight`, and speed `extra × 0.83`. Anything below magnitude 10 is dropped to 0. Couching only deals damage if the attacker or the victim is mounted.

### Missiles (`CalculateStrikeMagnitudeForMissile`)

```
magnitude = (missileSpeedRelativeToVictim / missileBaseSpeed)² × missileTotalDamage
missileTotalDamage = ammo damage + bow/crossbow thrust_damage  (stats from the item XML)
```

The shooter's movement is part of the missile's velocity, and the victim's movement is part of the relative speed.

### Horse charge (`ComputeBlowMagnitudeFromHorseCharge`, static)

`magnitude = (speedTowardVictim × hitAngle)² × hitAngle × horseChargeDamage`. It's always blunt.

## 3. Hit quality

`magnitude × momentumRemaining` (lower after the weapon has already passed through someone), then:

- **Hit with the arm or the hilt** instead of the blade: × 0.15 (`SwingHitWithArmDamageMultiplier`, `ThrustHitWithArmDamageMultiplier`). The damage type also becomes blunt.
- **Thrust that didn't land with the tip**: × 0.15 (`NonTipThrustHitDamageMultiplier`).
- **Perk weapon damage multiplier**: `AgentStatCalculateModel.GetWeaponDamageMultiplier`.

## 4. Weapon damage factor

`specialMagnitude = magnitude × damageFactor`

- **Crafted melee weapons** (all MP melee weapons are `CraftedItem`s in `mpitems.xml`): the blade piece's `damage_factor` in `mp_crafting_pieces.xml` (e.g. 3.2, 2.8).
- **Bows, crossbows, ammo and throwing weapons**: 1.0.

**This is what a melee weapon's displayed damage stat means.** Crafting computes it as `magnitude × damageFactor` at full attack speed, with **zero movement speed**, at the best impact point, before armor (`Crafting.CalculateSwingBaseDamage` / `CalculateThrustBaseDamage`). The stat is never read during melee combat; it's the physics evaluated in a reference case. Real hits are rarely that ideal (timing, impact point), and in vanilla the movement bonus usually more than makes up for it, even at walking pace.

## 5. Armor (`MultiplayerStrikeMagnitudeModel.ComputeRawDamage`)

```
blunt part  = magnitude × 100 / (100 + armor)
cut    = 0.10 × blunt part + 0.90 × magnitude × max(0, 1 − 0.6·armor / (20 + 0.4·armor))
pierce = 0.25 × blunt part + 0.75 × magnitude × 45 / (45 + armor)
blunt  = blunt part
```

The armor value is the armor of the body part that was hit (head armor for head hits, and so on). Share of the magnitude that becomes damage:

| Armor | 0 | 10 | 20 | 30 | 40 | 50 | 60 |
|---|---|---|---|---|---|---|---|
| Cut | 100% | 77% | 60% | 47% | 37% | 29% | 23% |
| Pierce | 100% | 84% | 73% | 64% | 58% | 52% | 48% |
| Blunt | 100% | 91% | 83% | 77% | 71% | 67% | 62% |

Pierce loses much less to armor than cut does: against 40+ armor it does about 1.6× the damage cut does from the same magnitude.

## 6. Body part and difficulty (`GetDamageMultiplierForBodyPart`)

| Body part | Cut | Pierce, melee | Pierce, missile | Blunt |
|---|---|---|---|---|
| Head / neck (human) | 1.2 | 1.25 | **2.0** | 1.2 |
| Head / neck (horse) | 1.2 | 1.2 | 1.2 | 1.2 |
| Torso / arms (human) | 1.0 | 1.0 | 1.0 | 1.0 |
| Torso / arms (horse) | 0.8 | 0.8 | 0.8 | 0.8 |
| Legs | 0.8 | 0.8 | 0.8 | 0.8 |

The result is then multiplied by the difficulty multiplier (1.0 in MP) and rounded up.

## 7. Perks (`MultiplayerAgentApplyDamageModel.ApplyGeneralDamageModifiers`)

The damage is multiplied by `1 + attacker perk damage bonus + victim perk damage-taken modifier` for the effective damage type, plus ranged headshot perk bonuses. The combat log's "modified damage" is the difference this step makes. Players have 100 HP (`hitpoints` in `mpclassdivisions.xml`), apart from perk bonuses.

## Horses: top speed and couching

**Top speed** (`MultiplayerAgentStatCalculateModel.UpdateMountAgentStats`):

```
MountSpeed = (horse item speed + harness speed bonus + 1) × 0.22 × (1 + perk mount speed) × (1 + riding skill × 0.0032)
```

MP horse items have speed 38–50 (Aserai horses are the fastest), and no MP harness changes speed. With the rider bonus of the classes we tested (about 1.48), that's about 12.7–16.6 m/s.

**Who computes it:** every client runs the same stat model with its own data files. The server sends the horse and harness items at spawn, but not item modifiers (`ModuleNetworkData.ReadItemReferenceFromPacket` rebuilds items without them). Movement itself is replicated natively; testing showed the server is in charge, so a server-side `MountSpeed` is what horses really reach.

**Couching** needs at least 9 m/s: `mounted_passive_action_minimum_speed` in `Native/ModuleData/native_parameters.xml`, a native parameter each client reads from its own files.
