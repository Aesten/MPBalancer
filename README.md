# mpbalancer

A **server-side multiplayer balancing mod** for Mount & Blade II: Bannerlord. It runs on the Dedicated Server only; players don't need to install anything.

What it changes (all tunable in-game, see [Tuning in-game](#tuning-in-game)):

- **Melee pierce thrusts** do a percentage of vanilla's damage for the same hit, by the closing speed along the thrust: close to the standing hit up to 4 m/s, then speed adds steadily up to about 13 m/s and less and less after (one curve for lances, one for spears and other weapons; the same for now). Vanilla's speed term grows with the square of the speed times the weapon's weight, so fast hits ran away; now they gain less, while vanilla's order between weapons (weight, thrust speed, speed bonus perks) stays. Never more than vanilla. Armor and body part apply as in vanilla (in multiplayer, armor is one value per class, the same on every body part).
- **Missiles**: vanilla damage, but headshots (head and neck) on players do ×1.5 instead of ×2. **Pierce on players** is soft-capped above 90 toward 130. **Swings** are vanilla (the target is pierce; cut damage was never the issue), with a `swing` setting kept for tests.
- **Anti-horse infantry perks** (Cavalry Breaker, Spearman, Farmer): their bonus damage against horses only counts with a polearm (the spear or pike they come with); vanilla applies it to any weapon, even thrown ones.
- **Couched lances / braced polearms**: a curve that approaches 200 and never exceeds it.
- **Horses**: top speeds compressed to about 12–14 m/s (vanilla about 13–16.6).
- **Item weights**: the Khuzait Rabble's Curved Round Shield weighs 1 kg instead of 5 (it made the class the slowest infantry; item weights set movement speed in multiplayer).
- **Horse health**: the Khuzait Steppe Horse and Tarpan have 215 health instead of 185, the Steppe Charger 245.
- **Thrust damage** (active thrusts only; couches and braces stay vanilla): Battanian Long Spear 29+1; lances raised so each beats the plain spears: Khuzait Long Lance 29+7, Vlandian Heavy Lance 33+1, Sturgian Long Lance 31+6, Western Lance and Khuzait Lance 31+7, Bamboo Lance 30+5, Sturgian Lance 29+4, Vlandian Long Lance 29+5.
- **Small Shield perk** (Khuzait light cavalry): gives two Steppe Guardian Shields instead of one, the second in the slot the class leaves free with this perk.

[docs/mod-damage.md](docs/mod-damage.md) explains how the mod processes a hit: which path each kind of hit takes, where each setting applies, how the curves work, and what damage to expect. [docs/native-damage.md](docs/native-damage.md) describes the vanilla calculation it builds on. `scripts/curve-editor.py` is a small window for editing the speed curves (`thrust_curve_spear`, `thrust_curve_lance`) point by point, 0–25 m/s: it saves them to `defaults.txt` or sends them to the running server. [docs/settings-history.md](docs/settings-history.md) records each set of defaults that was played, with the sessions and data behind it.

## Building

```sh
dotnet build src/MPBalancer -c Release
```

This compiles against the local Dedicated Server install's DLLs (override the path with `-p:DSDir="..."`) and writes a ready-to-copy module to `dist/Modules/MPBalancer/` (`SubModule.xml` and `bin/<platform>/MPBalancer.dll`). The server loads a module's DLL from `bin/<name of its working directory>/`, so the build ships it in both `Linux64_Shipping_Server` and `Win64_Shipping_Server`. The local install and the test server must be on the same game version (currently v1.4.8).

## Test server

A Linux Dedicated Server reachable as `ssh bannerlord@7900x`. Its install is at `~/Steam/steamapps/common/Mount & Blade II Dedicated Server/`; the launch script, `token.txt` and logs are in `~/bannerlord/`.

```sh
scripts/deploy.sh                              # build and install the module on the server
scripts/deploy.sh --restart                    # ...then restart the server
scripts/deploy.sh --restart -- --tickrate 60   # options after -- go to run-server.sh
```

`deploy.sh` replaces the module folder as a whole over `ssh` + `tar` (no stale files, no `scp` path-quoting issues). Set `MPB_HOST` to deploy elsewhere. A running server keeps the build it started with until it's restarted.

The server is started by `~/bannerlord/run-server.sh`, which lives in and is deployed by the sibling mpServerConfig repo. It loads MPBalancer unless given `--no-balancer`, `--manager-only` or `--vanilla` (no mods at all).

## Tuning in-game

Admins (players who joined with the server's `AdminPassword`) can change settings from chat, effective on the next hit. `!b debug` is open to everyone, so testers can see the mod's effect on their own hits. `!mpb` and its short form `!b` both work, and setting names can be shortened to any unique prefix (`!b horse_k 12.5`); unknown names get a "did you mean" hint.

```
!b                                  list settings and their values
!b thrust_curve_spear               show one setting's value, default, vanilla value and description
!b thrust_curve_spear 0:100 4:100 12:80 20:57   set a curve (x:percent pairs; announced to everyone)
!b thrust_curve_spear 100 100 ... 46   or a plain list of percentages, one per 1 m/s from 0 (scripts/curve-editor.py writes these)
!b pierce_missile 0.8               set a number
!b weapon_mult mp_aserai_heavy_lance:0.85 mp_western_long_lance:1.1    set per-weapon multipliers (or none)
!b reset                            restore the mod's defaults
!b vanilla                          vanilla behaviour, for A/B comparisons
!b debug                            toggle a readout of every pierce hit you deal or take
!b speedo                           toggle a once-per-second readout of your speed and top speed (your horse's, or yours on foot), as the server measures them
!b speedo stats                     mounted: your horse's item stats, server stats and bump threshold; on foot: the weapons you carry with their weights, the server's speed multipliers (normal and in combat), the weapon weight it counts, and the time to top speed
```

With MPManager on the server, these commands are routed through it (mpManager's shared `link/MPManagerLink.cs`, compiled in; the build expects the mpManager checkout next to this one, or `-p:MPManagerDir=...`): they're hidden from chat, MPManager's aliases and `!help` include them, they work from the server console (`mpm cmd /b ...`), and the server's permissions file decides who may run what. MPBalancer's defaults: what only shows something (`!b`, `!b <setting>`, `debug`, `speedo`) is open to everyone, changing settings (`!b <setting> <value>`, `reset`, `vanilla`) is for admins. Without MPManager, MPBalancer hooks chat itself, and only `!b debug` is open to everyone.

Default values live in [src/MPBalancer/defaults.txt](src/MPBalancer/defaults.txt), built into the mod (change a value there, rebuild and deploy; `scripts/curve-editor.py` edits the speed curves there). The table shows them for reference.

| Setting | Default | Vanilla | What it does |
|---|---|---|---|
| `swing` | 1 | 1 | scales the relative speed added to swings (vanilla by default) |
| `thrust_model` | 1 | 0 | 1 = melee pierce thrusts do a % of vanilla by closing speed (the curves below); 0 = vanilla |
| `thrust_curve_spear` | 26 values, % of vanilla per m/s of closing speed: 100, 79, 64 at 0, 2, 4 m/s (close to the standing hit), 69% at 10, 67% at 13, 50% at 20, 40% at 25 | – | melee pierce thrusts with weapons that can't couch; also accepts speed:percent pairs |
| `thrust_curve_lance` | the same as `thrust_curve_spear` for now | – | melee pierce thrusts with lances (weapons that can couch) |
| `weapon_mult` | none | none | extra multiplier on melee pierce thrusts per weapon item id (item_id:multiplier pairs); capped at vanilla |
| `pierce_missile` | 1 | 1 | multiplies pierce damage from arrows, bolts and thrown weapons |
| `missile_head` | 1.5 | 2 | pierce missile damage multiplier on players' head and neck; applies to missiles fired after the change |
| `missile_head_horse` | 1.2 | 1.2 | the same on horses (vanilla 1.2, as for any pierce hit to a horse's head) |
| `mount_damage_polearm` | 1 | 0 | 1 = infantry perks' bonus against horses (Cavalry Breaker +100%, Spearman and Farmer +50-75%) only with polearms; 0 = any weapon. Ranged classes' perks (Hunter, Big Game Hunter, Poacher) are untouched |
| `pierce_knee` / `pierce_cap` | 90 / 130 | off (1000) | pierce damage to players: unchanged up to the knee, then smoothly toward the cap |
| `couch` | 1 | 1 | multiplies couch/brace damage, before the couch curve |
| `couch_knee` / `couch_cap` | 0 / 200 | off | couch/brace curve; with knee 0 it's `200 × tanh(damage / 200)` |
| `horse_speed` | 0.92 | 1 | multiplies horse top speed (applies to every horse immediately) |
| `horse_knee` / `horse_cap` | 12.4 / 14.2 | off (1000) | horse top speeds (m/s) above the knee are compressed smoothly toward the cap; keep well above 9 (the couch minimum) |
| `horse_maneuver` | 0.95 | 1 | multiplies horse maneuver (turning); applies to every horse immediately |
| `horse_accel_time` | 1 | 1 | multiplies the horse's `TopSpeedReachDuration` stat; by its name higher = slower acceleration, check the `[MPB-ACCEL]` log lines |
| `horse_turn_keep_speed` | 0.6 | 0.6 | native: how sharp a horse can turn before losing its speed |
| `couch_min_speed` | 9 | 9 | native: minimum horse speed (m/s) to couch |
| `bump_speed` | 4.8 | 4.3 | minimum relative speed (m/s) for a horse bump; the engine also needs a running gait (lowest seen 5.9 m/s), so this can only raise the bar. Applies to horses spawned after the change |
| `item_weight` | `mp_curved_round_shield:1` | none | item weight in kg per item id. In multiplayer, weapons and shields carried (more so held) slow their carrier; armor weight doesn't count. The Khuzait Rabble's Curved Round Shield (5 kg) made it the slowest infantry class. Applies to characters spawned after the change; clients predict their own movement with the vanilla weight |
| `thrust_damage` | Battanian Long Spear 30 and the lances above (see `defaults.txt`) | none | listed thrust damage per weapon item id (its first thrusting usage; the others scale by the same ratio; couched and braced usages stay vanilla). Scales the weapon's thrust damage factor, so damage at every speed; applies to the next hit. Clients still list the vanilla number |
| `horse_hp` | `mp_khuzait_horse_agile:215 mp_khuzait_horse:215 mp_khuzait_horse_charger:245` | none | total health per horse item id (vanilla: 200 plus the horse's extra health; these three Khuzait horses have 185). Applies to horses spawned after the change; a player's own horse health bar can show more than full until the horse is below its vanilla maximum |
| `small_shields` | 2 | 1 | shields given by the Khuzait light cavalry's Small Shield perk; 2 adds a second one in Item3. Applies from the next spawn, in every mode, to players and their troops. Spawned characters and the pre-spawn preview show both; the class selection screen (built from each player's own files) still shows one |

- "native" settings override engine parameters (`native_parameters.xml`) on the server only, through the engine's `override_native_parameter`; clients keep their own values, so test for snapping. They're only sent to the engine once changed from vanilla.
- The thrust, missile and couch settings act on the damage the victim actually takes (after armor, body part and perks). Shield hits aren't affected.
- Changes are lost on restart; to keep a value, change its default in `src/MPBalancer/BalanceSettings.cs`. Every change is announced in chat and logged.
- The command itself stays visible in chat: the hook (`ChatBox.OnMessageReceivedAtDedicatedServer`, the same one TaleWorlds' web panel uses) can see messages but not hide them. Commands work once the first mission has started (about 40 s after launch).

### Hit log and stats

`!b debug` shows every pierce hit you deal or take (the other damage types stay vanilla, so they're only logged) in lines like `you -> Bob | pierce, neck | 9.4 m/s | armor 12 | vanilla 96 -> 67`: the attacker/victim relative speed, the armor at the hit location, and what the same hit would have done in vanilla.

Every damaging hit is recorded as a CSV row, one file per server run (`HitLog.cs`): `hits-<start>.csv` (only hits that damage a player or bot: damage type, source, body part hit, speeds, armor, vanilla and dealt damage, weapon and its listed thrust damage, both horses). Each row carries a `settings` number; `settings-<start>.csv` lists the balance values behind each number (a new one after every `!mpb` change), so hits can be compared per configuration, and against the vanilla damage of the same hits. The files go to `MPB_DATA_DIR` (mpManager's `run-server.sh` sets it to `~/bannerlord/metrics`, where earlier runs are gzipped and kept 30 days; without it, `Modules/MPBalancer/data`). Rows are queued by the combat code and written by a background thread once a second, so recording costs the server next to nothing. `!b speedo` readings still go to the log as `[MPB-SPEED]` / `[MPB-ACCEL]`. To summarize hits:

```sh
ssh bannerlord@7900x 'cat $(ls -t ~/bannerlord/instances/0/metrics/hits-*.csv | head -1)' | python scripts/hit-stats.py              # this run
ssh bannerlord@7900x 'cat $(ls -t ~/bannerlord/instances/0/metrics/hits-*.csv | head -1)' | python scripts/hit-stats.py --settings 2 # this run, configuration 2
ssh bannerlord@7900x 'zcat ~/bannerlord/instances/0/metrics/hits-*.csv.gz' | python scripts/hit-stats.py                            # earlier runs
```

Settings numbers start over at each server run, so use `--settings` on one run's file.

## Layout

- `src/MPBalancer/` — the mod (targets `netstandard2.0`, like the game assemblies; the server hosts them on .NET 6).
  - `MPBalancerSubModule.cs` — registers the replacement models for multiplayer games.
  - `Models/` — `BalancedApplyDamageModel` (thrust/missile/couch curves on the final damage), `BalancedStrikeMagnitudeModel` (swing speed bonus), `BalancedAgentStatModel` (horse top speed).
  - `BalanceSettings.cs` — all settings with defaults, vanilla values and ranges; number, curve and table setting types.
  - `WeaponFactors.cs` — weapon spread, lance and per-weapon multipliers. `Combat.cs` — shared helpers (speeds, soft cap, damage type).
  - `ChatCommands.cs`, `HitDebug.cs`, `Speedometer.cs` — admin commands, hit log/readout, speedometer (mounted and on foot). `ServerTickBehavior.cs` sends their chat output on the mission tick. `VanillaEstimate.cs` — the vanilla damage shown in the hit log.
- `module/SubModule.xml` — module definition, copied into `dist/` on build. `ModuleCategory="Server"` keeps the module from being advertised to clients; `DedicatedServerType="all"` loads it on any dedicated server type but never on a game client.
- `docs/mod-damage.md` — how the mod processes a hit, where each setting applies, curves, expected damage. `docs/native-damage.md` — the vanilla damage calculation, horse speed and couching rules.
- `scripts/` — `deploy.sh`, `hit-stats.py`.
- `../BannerlordDecompiled/` — the shared decompiled-source repo, a sibling of this one, with its own scripts to regenerate it after a game update (see its README).
  - `DedicatedServerSource/` — decompiled Dedicated Server assemblies, including server-only ones. Prefer this tree for reading multiplayer code (the decompiler resolved its dependencies better).
  - `GameSource/` — decompiled client assemblies (multiplayer and singleplayer).
  - `ModuleData/{Client,DedicatedServer}/{Native,Multiplayer}/` — XML/JSON/TXT data from both installs.

## Findings

- **Client and server builds match.** The server ships its own build of every shared DLL, but the combat code is identical (`MissionCombatMechanicsHelper.cs`, `CombatStatCalculator.cs` match line for line), and so is the Multiplayer module's item and class data.
- **Damage is decided by the server.** Hits go through `Mission.MeleeHitCallback` / `MissileHitCallback` → `GetAttackCollisionResults` → the game models → `RegisterBlow`, all managed C#, and the server sends results and the combat log to clients. Server-side damage changes apply to everyone, including the combat log's "+X% speed bonus".
- **Game models can be replaced without Harmony.** `MultiplayerGame.OnInitialize` registers its models before calling `InitializeGameStarter`, and `GetGameModel<T>()` returns the newest one, so models added from the SubModule's `InitializeGameStarter` win. Subclass the Multiplayer models so the perk logic still applies.
- **Speed bonus.** The relative velocity is projected onto the blow direction (with up to +0.2 free alignment), multiplied by 0.7 for swings / 0.5 for thrusts, and added to the weapon's own speed before squaring, so even walking speed adds a lot. Scaling that speed in the strike model hits speed-heavy weapons (lances) hardest, so thrusts are shaped on the final damage instead (see `docs/mod-damage.md`).
- **The engine decides** hit detection, the bone hit, where on the weapon it landed, attack progress, block/parry outcomes, velocities and animation timing. Managed code only decides what to do with them.
- **Horse speed works server-side (tested).** Clients compute horse stats themselves, and nothing the server could send changes that (item modifiers aren't networked, no MP harness has a speed penalty, server-only XML edits stay on the server). But the server is in charge of movement: with `!b speedo`, the measured top speed equalled the server's `MountSpeed` at `horse_speed` 1, 0.5 and 0.8 (16.6, 8.3 and 13.3 m/s), instantly and without rubber-banding. Top speed ≈ (item speed + 1) × 0.22 × rider bonus (riding skill and perks, about 1.48 in the test).
- **Couching needs at least 9 m/s** (`mounted_passive_action_minimum_speed` in `Native/ModuleData/native_parameters.xml`, read by each client from its own files). A global speed cut makes slow horses hard to couch with; the soft cap only touches the fastest horses.
- **Server-only item XML edits are risky.** Clients use their own item data for the loadout screen and animations, so balance through model overrides instead.
- **Where module DLLs go.** `Module.LoadSubModules` looks in `Modules/<Mod>/bin/<working directory name>/` and falls back to the server's own `bin`. The `Couldn't find .dll ... Win64_Shipping_Client` log line is harmless.
- **The chat hook runs inside the network packet handler.** Broadcasting from there crashed the server (native SIGSEGV), so commands and readouts are queued and sent per player on the mission tick.
