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
- Utility modifiers are Physical Defense, Magical Defense, Health, Critical
  Damage, Critical Chance, Mana, and Mana Regeneration.
- Attack and Magic are weapon-only modifiers. Accessories cannot roll either
  offensive stat.
- Every weapon has exactly one offensive profile: Attack, Magic, or hybrid.
  Hybrid weapons receive both Attack and Magic at 70% of the corresponding
  single-stat magnitude, making them flexible but individually weaker in each
  damage type. Increasing rarity never adds a second offensive profile.
- Physical and Magical Defense are separate mode-specific ratings. They
  reduce only the matching incoming damage type and are capped so they remain
  complementary to the base game's Defense stat.
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
current pool can shield a healed ally, cleanse or buff a healed ally, cleanse
or buff a shielded ally, remove an enemy buff on a damaging hit, or grant a
shield when the wearer receives a buff. Per-action guards prevent a multi-hit
or repeated callback from firing the same power repeatedly on one target.

## Balance basis

The level-42 affix endpoints were calibrated around the original +5 equipment
ranges rather than copied directly. The base game reaches level 42, ordinary
equipment normally upgrades five times, high-end single-stat accessories reach
roughly 100 Defense, 1000 Health, 120 Mana, or 60 Mana Regeneration, and basic
+5 weapons reach roughly 250 Attack or Magic. Generated items divide their
power budget among one to five independently rolled modifiers; Legendary
powers sit above that budget. A hybrid weapon applies 70% of the normal
weapon-offense curve to both Attack and Magic.

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
