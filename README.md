# mpbalancer

A **server-side multiplayer balancing mod** for Mount & Blade II: Bannerlord. It runs on the Dedicated Server only; players don't need to install anything.

What it changes (all tunable in-game, see [Tuning in-game](#tuning-in-game)):

- **Melee pierce thrusts** follow a curve of "% of vanilla damage" by relative speed: 72% standing, falling to 46% at 20 m/s on players, and 100% → 70% on horses. Vanilla speed physics is left alone, so weapons keep their vanilla relation to each other (lances gain more from speed than spears).
- **Armor**: on players, an armor curve on top of the speed curve makes unarmored body parts much more vulnerable and keeps heavy armor tanky (e.g. a Western lance head hit at 12 m/s: 105 unarmored, 74 at 20 armor, 50 at 53 armor).
- **Weapons**: thrust damage scales a bit with the weapon's listed thrust damage within its group, lances get +15%, and individual weapons can be adjusted (the Aserai heavy lance is toned down by 15%).
- **Missiles**: 85% of vanilla pierce damage. **Pierce on players** is soft-capped above 90 toward 130. **Swings** are vanilla (the target is pierce; cut damage was never the issue), with a `swing` setting kept for tests.
- **Couched lances / braced polearms**: a curve that approaches 200 and never exceeds it.
- **Horses**: top speeds compressed to about 12–14 m/s (vanilla about 13–16.6).

[docs/mod-damage.md](docs/mod-damage.md) explains how the mod processes a hit: which path each kind of hit takes, where each setting applies, how the curves work, and what damage to expect. [docs/native-damage.md](docs/native-damage.md) describes the vanilla calculation it builds on.

## Building

```sh
dotnet build src/MPBalancer -c Release
```

This compiles against the local Dedicated Server install's DLLs (override the path with `-p:DSDir="..."`) and writes a ready-to-copy module to `dist/Modules/MPBalancer/` (`SubModule.xml` and `bin/<platform>/MPBalancer.dll`). The server loads a module's DLL from `bin/<name of its working directory>/`, so the build ships it in both `Linux64_Shipping_Server` and `Win64_Shipping_Server`. The local install and the test server must be on the same game version (currently v1.4.8).

## Test server

A Linux Dedicated Server reachable as `ssh aesten@7900x`. Its install is at `~/Steam/steamapps/common/Mount & Blade II Dedicated Server/`; the launch script, `token.txt` and logs are in `~/bannerlord/`.

```sh
scripts/deploy.sh                              # build and install the module on the server
scripts/deploy.sh --restart                    # ...then restart the server
scripts/deploy.sh --restart -- --tickrate 60   # options after -- go to run-server.sh
```

`deploy.sh` replaces the module folder as a whole over `ssh` + `tar` (no stale files, no `scp` path-quoting issues). Set `MPB_HOST` to deploy elsewhere. A running server keeps the build it started with until it's restarted.

The server is started by `~/bannerlord/run-server.sh`, which lives in and is deployed by the sibling mpManager repo (see its README). It loads MPBalancer unless given `--vanilla`.

## Tuning in-game

Admins (players who joined with the server's `AdminPassword`) can change settings from chat, effective on the next hit. `!b debug` is open to everyone, so testers can see the mod's effect on their own hits. `!mpb` and its short form `!b` both work, and setting names can be shortened to any unique prefix (`!b horse_k 12.5`); unknown names get a "did you mean" hint.

```
!b                                  list settings and their values
!b thrust_curve                     show one setting's value, default, vanilla value and description
!b thrust_curve 0:70 4:61 20:45     set a curve (x:percent pairs; announced to everyone)
!b thrust_spread 0.3                set a number
!b weapon_mult mp_aserai_heavy_lance:0.85 mp_western_long_lance:1.1    set per-weapon multipliers (or none)
!b reset                            restore the mod's defaults
!b vanilla                          vanilla behaviour, for A/B comparisons
!b debug                            toggle a readout of every pierce hit you deal or take, and of bumps
!b speedo                           toggle a once-per-second readout of your horse's speed and top speed, as the server measures them
!b speedo stats                     print your current horse's item stats, server stats and bump threshold once
```

With MPManager on the server, these commands are routed through it (`src/MPBalancer/MPManagerLink.cs`): they're hidden from chat, `/b` works too, MPManager's aliases and `!help` include them, and they work from the server console (`mpm cmd /b ...`). Without MPManager, MPBalancer hooks chat itself, as before.

| Setting | Default | Vanilla | What it does |
|---|---|---|---|
| `swing` | 1 | 1 | scales the relative speed added to swings (vanilla by default) |
| `thrust_curve` | `0:72 2:67 4:63 6:60 8:57 10:54 12:52 16:49 20:46` | `0:100` | melee pierce thrusts on players: % of vanilla damage by relative speed (m/s), interpolated between points |
| `thrust_armor_curve` | `0:125 10:120 20:120 35:127 53:116` | `0:100` | melee pierce thrusts on players: extra % by the armor at the hit location, on top of `thrust_curve` |
| `thrust_curve_horse` | `0:100 2:94 4:89 6:85 8:81 10:78 12:76 16:73 20:70` | `0:100` | melee pierce thrusts on horses: % of vanilla damage by relative speed |
| `thrust_spread` | 0.5 | 0 | melee thrust damage × (weapon's listed thrust damage / `thrust_ref`) ^ this |
| `thrust_ref` | 0 (auto) | – | listed thrust damage left unchanged; 0 = median of the weapon's group (usage class, lances separately; logged at startup) |
| `lance` | 1.15 | 1 | extra multiplier on melee thrusts with lances (items that can couch) |
| `weapon_mult` | `mp_aserai_heavy_lance:0.85` | `none` | extra multiplier on melee thrusts per weapon item id |
| `pierce_missile` | 0.85 | 1 | multiplies pierce damage from arrows, bolts and thrown weapons |
| `pierce_knee` / `pierce_cap` | 90 / 130 | off (1000) | pierce damage to players: unchanged up to the knee, then smoothly toward the cap |
| `couch` | 1 | 1 | multiplies couch/brace damage, before the couch curve |
| `couch_knee` / `couch_cap` | 0 / 200 | off | couch/brace curve; with knee 0 it's `200 × tanh(damage / 200)` |
| `horse_speed` | 0.92 | 1 | multiplies horse top speed (applies to every horse immediately) |
| `horse_knee` / `horse_cap` | 12.4 / 14.2 | off (1000) | horse top speeds (m/s) above the knee are compressed smoothly toward the cap; keep well above 9 (the couch minimum) |
| `horse_maneuver` | 0.9 | 1 | multiplies horse maneuver (turning); applies to every horse immediately |
| `horse_accel_time` | 1 | 1 | multiplies the horse's `TopSpeedReachDuration` stat; by its name higher = slower acceleration, check the `[MPB-ACCEL]` log lines |
| `horse_turn_keep_speed` | 0.6 | 0.6 | native: how sharp a horse can turn before losing its speed |
| `couch_min_speed` | 9 | 9 | native: minimum horse speed (m/s) to couch |
| `bump_speed` | 4.8 | 4.3 | minimum relative speed (m/s) for a horse bump; the engine also needs a running gait (lowest seen 5.9 m/s), so this can only raise the bar. Applies to horses spawned after the change |

- "native" settings override engine parameters (`native_parameters.xml`) on the server only, through the engine's `override_native_parameter`; clients keep their own values, so test for snapping. They're only sent to the engine once changed from vanilla.
- The thrust, missile and couch settings act on the damage the victim actually takes (after armor, body part and perks). Shield hits aren't affected.
- Changes are lost on restart; to keep a value, change its default in `src/MPBalancer/BalanceSettings.cs`. Every change is announced in chat and logged.
- The command itself stays visible in chat: the hook (`ChatBox.OnMessageReceivedAtDedicatedServer`, the same one TaleWorlds' web panel uses) can see messages but not hide them. Commands work once the first mission has started (about 40 s after launch).

### Hit log and stats

`!b debug` shows every pierce hit you deal or take (the other damage types stay vanilla, so they're only logged) in lines like `you -> Bob | pierce, neck | 9.4 m/s (+134%) | armor 12 | vanilla 96 -> 67`: the attacker/victim relative speed, the game's speed bonus, the armor at the hit location, and what the same hit would have done in vanilla.

Every damaging hit is recorded as a CSV row, one file per server run (`HitLog.cs`): `hits-<start>.csv` (only hits that damage a player or bot: damage type, source, body part hit, speeds, armor, vanilla and dealt damage, weapon and its listed thrust damage, both horses). Each row carries a `settings` number; `settings-<start>.csv` lists the balance values behind each number (a new one after every `!mpb` change), so hits can be compared per configuration, and against the vanilla damage of the same hits. The files go to `MPB_DATA_DIR` (mpManager's `run-server.sh` sets it to `~/bannerlord/metrics`, where earlier runs are gzipped and kept 30 days; without it, `Modules/MPBalancer/data`). Rows are queued by the combat code and written by a background thread once a second, so recording costs the server next to nothing. `!b speedo` readings still go to the log as `[MPB-SPEED]` / `[MPB-ACCEL]`. To summarize hits:

```sh
ssh aesten@7900x 'cat $(ls -t ~/bannerlord/metrics/hits-*.csv | head -1)' | python scripts/hit-stats.py              # this run
ssh aesten@7900x 'cat $(ls -t ~/bannerlord/metrics/hits-*.csv | head -1)' | python scripts/hit-stats.py --settings 2 # this run, configuration 2
ssh aesten@7900x 'zcat ~/bannerlord/metrics/hits-*.csv.gz' | python scripts/hit-stats.py                            # earlier runs
```

Settings numbers start over at each server run, so use `--settings` on one run's file.

## Layout

- `src/MPBalancer/` — the mod (targets `netstandard2.0`, like the game assemblies; the server hosts them on .NET 6).
  - `MPBalancerSubModule.cs` — registers the replacement models for multiplayer games.
  - `Models/` — `BalancedApplyDamageModel` (thrust/missile/couch curves on the final damage), `BalancedStrikeMagnitudeModel` (swing speed bonus), `BalancedAgentStatModel` (horse top speed).
  - `BalanceSettings.cs` — all settings with defaults, vanilla values and ranges; number, curve and table setting types.
  - `WeaponFactors.cs` — weapon spread, lance and per-weapon multipliers. `Combat.cs` — shared helpers (speeds, soft cap, damage type).
  - `ChatCommands.cs`, `HitDebug.cs`, `Speedometer.cs` — admin commands, hit log/readout, horse speedometer. `ServerTickBehavior.cs` sends their chat output on the mission tick. `VanillaEstimate.cs` — the vanilla damage shown in the hit log.
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
