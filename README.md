# Randomized Gear Mode for Monster Sanctuary

This repository contains a BepInEx plugin that adds an opt-in, loot-driven
game mode to Monster Sanctuary. It is designed as an MMO-style equipment
progression layer: ordinary weapons and accessories are replaced by generated
items with an item level, rarity, random modifiers, and possible Legendary
power.

The repository is rooted in a local Steam installation. Game files, BepInEx,
managed references, logs, generated save data, and compiled DLLs are ignored
by Git.

## Starting the mode

Build and install the plugin, then open Monster Sanctuary's new-game mode
selection screen. `Randomized Gear` has its own labeled row and On/Off button;
it does not reuse or cover the `Relics of Chaos` control. The plugin changes
gameplay only when Randomized Gear was selected for the new save or New Game
Plus run.

The mode marker is written into the game save. Generated item definitions are
stored separately under:

```text
BepInEx/config/RandomizedGearMode/slot-<number>.gear
```

Copying or deleting a save through the game also copies or removes its gear
registry.

## Implemented rules

- Ordinary equipment acquired from combat, chests, reward items, scripts, or
  shops is replaced with generated equipment. Relics are preserved for
  compatibility with Relics of Chaos.
- Merchant equipment is displayed as unidentified. Its rarity and modifiers
  are generated only after payment, using the party's current level.
- Combat equipment uses the encounter or enemy level. Chests and scripted
  world rewards use the current map encounter level when available, with the
  party level as a fallback. Merchant purchases always use the party level.
  Item levels are clamped to 1-42.
- Common items have one modifier. Magic has two, Rare has three, Epic has
  four, and Legendary has five plus one triggered power.
- Utility modifiers include Physical Defense, Magical Defense, flat and
  percentage Maximum Health, Critical Damage, Critical Chance, Mana, Mana
  Regeneration, Damage Reduction, Buff Effect, and Shield Effect.
- Attack, Magic, hybrid offense, and Poison/Burn/Congeal Damage are
  weapon-only modifiers. Accessories cannot roll damage-oriented modifiers.
- Every weapon has exactly one offensive profile: Attack, Magic, or hybrid.
  Hybrid weapons receive both Attack and Magic at 70% of the corresponding
  single-stat magnitude, making them flexible but individually weaker in each
  damage type. Increasing rarity never adds a second offensive profile.
- Physical and Magical Defense are separate mode-specific ratings. They
  reduce only the matching incoming damage type and are capped so they remain
  complementary to the base game's Defense stat.
- Each Damage Reduction modifier is exactly 5% at every item level and roll
  quality. While the mode is active, the combined reduction from Defense,
  percentage reduction, defensive buffs, and the mode's typed defenses is
  limited to 50% for player monsters. This cap does not turn shields into
  damage reduction; shields remain a separate resource.
- The blacksmith increases rarity instead of item level. Each rarity increase
  retains every existing modifier and roll, then adds one new utility
  modifier. Item level remains the level at which the item was found or
  purchased.
- The blacksmith can reroll all modifiers and roll strengths for crafting
  materials plus a modest gold fee. Rerolling preserves item level, rarity,
  and any Legendary power. Weapons always receive a valid offensive profile
  after a reroll; accessories still exclude Attack and Magic.
- Upgrade and reroll materials are selected deterministically from the
  crafting-material tier appropriate to item level. The precise material is
  varied using the source equipment, rarity, item level, reroll count, and
  operation, so different base items do not all have identical recipes.
- Raising Epic gear to Legendary also consumes one `Legendary Upgrade Token`.
  This encounter reward reuses the appearance and icon of a monster Level
  Badge but cannot be consumed as one. Its drop chance rises from 0.5% near
  level 1 to 6% at level 42, with a 2.5 percentage-point champion bonus and a
  10% hard cap.
- Online Arena entry and online battle startup are blocked while this mode is
  active. Generated item IDs are local and are intentionally never sent into
  PvP.
- Enabling the mode for New Game Plus converts carried ordinary inventory and
  equipped items. Existing Relics are not converted.

## Relics of Chaos compatibility

When both game modes are enabled, each generated Legendary item also receives
one `Relic Echo` selected from a Relics of Chaos item in the same equipment
category. Weapons use weapon donors and accessories use accessory donors.
Randomized Gear and Relics of Chaos remain independent save settings.

The installed build provides 34 effect-bearing donors used by this system: 9
weapons and 25 accessories. Relics whose identity consists only of flat stats
are not donors. A Relic Echo inherits the donor's special percentage fields,
drawbacks, runtime equipment callbacks, and equip restrictions. Examples
include extra attack hits, start-of-turn buffs or shields, elemental damage
and debuff procs, improved debuffs, additional buff stacks, lifesteal, and
type-limited effects.

Flat relic Attack, Magic, Defense, Health, Mana, critical, and regeneration
stats are not copied. The generated item's normal modifier budget remains in
control, so an accessory cannot gain Attack or Magic through a Relic Echo.
Restrictions such as `MonsterTypeRestriction`, `OnlyFamiliars`, and
`OnlyUnshifted` are retained so an effect intended for Dragons, Constructs,
Nature monsters, Spectral Familiars, or unshifted monsters keeps that identity.
The selected donor is shown in the Legendary item's tooltip and remains fixed
when modifiers are rerolled.

## Rarity progression

Rarity probabilities interpolate between the beginning and end of the item
level range:

| Rarity | Item level 1 | Item level 42 |
| --- | ---: | ---: |
| Legendary | 0.1% | 2% |
| Epic | 0.9% | 10% |
| Rare | 5% | 25% |
| Magic | 24% | 38% |
| Common | 70% | 25% |

Legendary powers are event-driven effects rather than another flat stat. The
pool contains seventeen powers:

| Power | Effect and limit |
| --- | --- |
| Barrier Bloom | Healing also shields for 25% of the heal |
| Purifying Touch | The first heal on each target per action removes a debuff |
| Rallying Light | The first heal on each target per action grants a random buff |
| Cleansing Ward | The first shield on each target per action removes a debuff |
| Empowering Ward | The first shield on each target per action grants a random buff |
| Spellbreaker | The first damaging hit on each target per action removes a buff |
| Inspiring Aegis | Receiving a buff grants shield equal to 8% of maximum health |
| Hexing Edge | The first damaging hit on each target per action applies a random debuff |
| Vampiric Pulse | The first damaging hit on each target per action heals for 10% of actual damage |
| Critical Momentum | The first critical damage or healing result per action grants a random buff |
| Mana Battery | The first completed action each turn restores 8% maximum mana |
| Mending Ward | The first shield on each target per action heals for 15% of its value |
| Arcane Shelter | The first heal on each target per action restores 5% maximum mana |
| Retaliatory Ward | The first hit taken from an action grants shield equal to 25% of that hit |
| Last Bastion | Once per combat, reaching 30% health or less grants 20% maximum-health shield |
| Phoenix Oath | Once per combat, fatal damage is survived with 10% health |
| Opening Gambit | Enter combat with two random buffs |

Action, turn, and combat guards prevent multi-hit attacks, repeated callbacks,
or extra actions from multiplying effects beyond their stated limits. Powers
can still interact intentionally—for example, Opening Gambit can activate
Inspiring Aegis when both appear on a monster's equipped Legendary items.

## Balance basis

The level-42 affix endpoints were calibrated around the original +5 equipment
ranges rather than copied directly. The base game reaches level 42, ordinary
equipment normally upgrades five times, high-end single-stat accessories reach
roughly 100 Defense, 1000 Health, 120 Mana, or 60 Mana Regeneration, and basic
+5 weapons reach roughly 250 Attack or Magic. Generated items divide their
power budget among one to five independently rolled modifiers; Legendary
powers sit above that budget. A hybrid weapon applies 70% of the normal
weapon-offense curve to both Attack and Magic.

The percentage-effect modifiers use these unrolled level endpoints. Their
normal 85%-115% roll quality is then applied, except Damage Reduction, which
always remains exactly 5%:

| Modifier | Item level 1 | Item level 42 | Application |
| --- | ---: | ---: | --- |
| Maximum Health | 3% | 8% | Multiplies the wearer's final health calculation |
| Damage Reduction | 5% | 5% | Native equipment reduction; subject to the 50% combined cap |
| Poison, Burn & Congeal Damage | 5% | 12% | Weapon-only; raises enemies' incoming DOT multipliers through the game's enemy-stat pass |
| Buff Effect | 3% | 8% | Multiplies the strength of buffs affecting the wearer |
| Shield Effect | 5% | 15% | Uses the game's native outgoing shield multiplier |

DOT power deliberately excludes Shock because Shock is a debuff/resource
interaction rather than a periodic damage trigger in this game. Applying DOT
power through enemy-stat calculation also keeps Poison, Burn, and Congeal on
the same calculation path used by the base game's comparable passive effects.

Balance references:

- [Monster Sanctuary equipment overview](https://monster-sanctuary.fandom.com/wiki/Equipment)
- [Monster Sanctuary weapons](https://monster-sanctuary.fandom.com/wiki/Weapons)
- [Monster Sanctuary accessories](https://monster-sanctuary.fandom.com/wiki/Accessories)
- [Monster Sanctuary level cap](https://monster-sanctuary.fandom.com/wiki/Level)
- [Official Relics of Chaos update notes](https://steamcommunity.com/ogg/814370/announcements/detail/3692432394722364289)

These references were used for factual ranges and mode behavior. The plugin's
generation formulas, rarity curve, persistence design, code, and wording are
original to this repository.

## Build and install

Run:

```powershell
.\scripts\build.ps1
```

The script builds `src/MonsterSanctuaryMod/MonsterSanctuaryMod.csproj` in
Release mode for the game's Mono-compatible .NET Framework runtime and copies
`MonsterSanctuaryMod.dll` to `BepInEx/plugins`.

If local PowerShell policy prevents scripts from running, build directly:

```powershell
dotnet build .\src\MonsterSanctuaryMod\MonsterSanctuaryMod.csproj --configuration Release
```

After launching the game, inspect `BepInEx/LogOutput.log`. Set
`VerboseLogging = true` in the generated BepInEx config only while diagnosing
item generation or save-load behavior.

## Testing warning

Use a disposable save until the mode has completed repeated runtime testing.
The `RGM3` sidecar format avoids modifying Monster Sanctuary's binary save
schema and can migrate existing `RGM1` and `RGM2` registries. Generated
equipment still participates in inventory, equipment, upgrade, and save-load
flows that need in-game validation on the installed build.
