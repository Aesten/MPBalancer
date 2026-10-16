# MPBalancer (Native+)

**The goal:** pierce damage (sword thrusts, spears, arrows, etc) and cavalry speed were dominating fights. This server-side mod tones those down and attempts to reach a better balance.

## Features (as of 16 October 2026)

**Mounted thrusts** (spears and lances from horseback)
- Damage is a % of vanilla that depends on speed. At low speed a thrust does about what it does standing still (vanilla already adds a lot there); from about 2 m/s it rises steadily up to about 13 m/s, then flattens. Speed still adds damage, just much less than in vanilla at high speed, so full-speed bombing gains the least.
- Example, a full thrust to the head at 12 m/s (around full-speed horse) against 25 armor: Western Cavalry Spear 68, Western Lance 90, Aserai Heavy Lance 99. Against 55 armor (Varyag-level): 50, 65 and 73.
- Weapons still differ the way they do in vanilla: heavier weapons and slower thrusts gain more from speed, and the Lancer and Line Breaker perks still add their speed bonus.

**Weapon buffs** (base thrust damage, applies to every grip, couches and braces included)
- Lances, so that every lance hits harder than a plain spear: Khuzait Long Lance 29 → 36, Western Lance and Khuzait Lance 31 → 38, Sturgian Long Lance 31 → 37, Bamboo Lance 30 → 35, Vlandian Long Lance 29 → 34, Sturgian Lance 29 → 33, Vlandian Heavy Lance 33 → 34.
- Battanian Long Spear 29 → 30.
- The loadout screen still shows the vanilla numbers.

**Thrusts on foot** (infantry, archers in melee, dismounted cavalry; every weapon, sword stabs included): 72% of vanilla standing, 63% at 4 m/s, 54% at 10 m/s, 46% from 20 m/s.

**Thrusts against horses:** 15% stronger than against players (still lower than vanilla), so killing a horse doesn't get much harder.

**Couched lances:** damage approaches 200 and never goes above it (vanilla couches could reach over 400), but it can still one-tap heavy infantry with enough speed.

**Missiles** (arrows, bolts, throwing weapons): base damage is at vanilla level, but **headshots on players do ×1.5 instead of ×2**. Headshots on horses are unchanged.

**Anti-horse infantry perks** (Cavalry Breaker, Spearman, Farmer): their bonus damage against horses now only counts with a spear, pike or other polearm, the weapon those perks come with. In vanilla it also applied to swords, axes, maces and even thrown weapons. Ranged classes' anti-horse perks are unchanged (only Sturgian Archer's poacher perk applies to melee weapons).

**Horses**
- Top speed is lower and closer together: 12.4 to 14.2 m/s instead of vanilla's 13 to 16.6. The fastest horses lose the most (Aserai).
- Maneuver is at 95% of vanilla.
- Bumping into someone needs a bit more speed (4.8 m/s instead of 4.3 m/s). This mostly has no impact as the horse needs to be in a gallop state to bump, which is usually reached around 5.9 m/s. This change therefore prevents some glitched instances where horses manage to enter gallop below the typical speed and still bump.

**Khuzait**
- Steppe Horse and Tarpan have 215 health instead of 185, the Steppe Charger 245 (your client mod might not register this HP change).
- Light cavalry's Small Shield perk gives two shields instead of one (the intended use case is to drop one to another cavalry that spawn with line-breaker, missing a shield - with this, it matches Battanian cavalry).
- The Rabble's Curved Round Shield weighs 1 kg instead of 5 (it made the class the slowest infantry).

**Unchanged:** swings (cut and blunt), kicks, shield bashes, the damage of horse bumps, horse acceleration, and the speed needed to couch.

**To check on the server:** type `!b debug` in chat. Every pierce hit you deal or take then shows what it did and what vanilla would have done with the vanilla weapon, e.g. `you -> Bob | pierce, neck | closing 9.4 m/s | armor 12 | vanilla 96 -> 67`.

## What changed since 12 October

- **Mounted thrusts rebuilt:** the old version cut every thrust to 72% of vanilla even when standing, and added armor and lance corrections on top. Now low-speed pokes stay close to a standing thrust, speed adds steadily up to about 13 m/s, and the extra corrections are gone. Typical full-speed hits are close to where they were, actually slightly higher (about +5 to +15%), with a smoother rise below.
- **Lances** got individual base damage buffs instead of a flat +15%, and the Aserai Heavy Lance's 15% nerf is gone (the new speed curve already keeps it in check).
- **Thrusts on foot** got their own curve.
- **Missiles** base damage is back to full damage, with weaker headshots on players instead.
- **Anti-horse perks** limited to polearms
- **Khuzait** horse health, Small Shield perk and Rabble shield changes.
