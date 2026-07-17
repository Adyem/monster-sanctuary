# Monster Sanctuary modding notes

This file is a working map of the installed PC build. It is based on the
managed assembly inventory and reflection inspection performed on the local
`Assembly-CSharp.dll`; it is not a substitute for checking exact method
signatures in a decompiler before writing a patch.

## Repository and runtime layout

- `Monster Sanctuary.exe` is the Unity player executable.
- `Monster Sanctuary_Data/Managed/` contains the game's managed code and Unity
  API assemblies.
  - `Assembly-CSharp.dll` is the main game assembly. It contains roughly 1,450
    types in this build.
  - `Assembly-CSharp-firstpass.dll` contains additional Unity/game support code.
  - `UnityEngine*.dll` provides the Unity API used by plugins.
  - `Rewired_*.dll` provides input support.
- `Monster Sanctuary_Data/Resources/` only has Unity's default resources in
  the current install. Most game content is likely in serialized scenes,
  shared assets, and bundles under `Monster Sanctuary_Data`.
- `Monster Sanctuary_Data/Plugins/` contains native integrations such as
  Rewired and Steamworks.
- `BepInEx/` and the Doorstop files at the game root are local runtime tools.
  They are deliberately ignored by Git.
- `src/MonsterSanctuaryMod/` is the source project. Its references point at
  local game/BepInEx assemblies and must not be committed.

The game uses Unity Mono managed assemblies rather than an IL2CPP-only layout,
so normal C# BepInEx plugins and Harmony patches are a good starting point.

## Main code areas found

### Global lifecycle and scenes

- `GameController` appears to own global game state, scene changes, input,
  doors, transitions, and access to combat.
- `GameStateManager` exposes states such as menu, exploration, loading, and
  combat, and controls whether input is allowed.
- `PlayerController`, `PlayerStateManager`, `PlayerPhysics`,
  `GameModeManager`, and `ProgressManager` cover player movement, game mode,
  and progression.

These are useful for observing scene transitions or adding debug overlays, but
global patches can affect every scene and should be kept narrowly scoped.

### Monsters, teams, and progression

- `MonsterManager` owns active, inactive, farm, familiar, and permadead
  monsters. It also exposes add, remove, switch, load, and save operations.
- `Monster`, `MonsterStats`, `MonsterStateManager`, and `MonsterBehavior`
  represent runtime monster state, calculated stats, actions, and visuals.
- `MonsterTeamManager`, `MonsterTeam`, and `MonsterTeamData` handle saved team
  layouts and equipment/skill storage.
- `SkillManager`, `SkillTree`, `SkillTreeEntry`, `BaseSkill`, `PassiveSkill`,
  and the `Action*` classes implement skills, passives, and their effects.
- `EquipmentManager`, `Equipment`, and concrete equipment classes handle
  equipment and many combat-triggered equipment effects.

This is the most promising area for balance and quality-of-life mods because
the relevant logic is represented by named managed classes and callbacks.

### Combat and effects

- `CombatController` coordinates combat state, turns, targets, rewards,
  retreat, monster switching, and combat completion.
- `CombatUIController` and the combat/menu classes handle presentation and
  input around combat.
- `ActionDamage`, `ActionHeal`, `ActionBuff`, `ActionDebuff`, `ActionShield`,
  `ActionRevive`, and related `Action*` types are the effect execution layer.
- `BuffManager`, `BuffInfo`, `BuffView`, `UpkeepManager`, and the buff/debuff
  enums manage persistent combat effects.
- `SkillManager` has useful calculation and event methods, including damage,
  healing, mana, resistance/weakness, critical chance, and buff/debuff hooks.

Good initial patch points include calculation methods and pre/post action hooks.
Avoid changing queue ordering or turn-state transitions until the combat flow
is understood, because a small error can soft-lock a battle.

### Inventory, rewards, and saves

- `InventoryManager` owns item lists and exposes add/remove/quantity methods.
- `EquipmentManager` owns equipped items and save/load operations.
- `SaveGameManager`, `SaveGameData`, `PlayerSaveGameData`,
  `MonsterSaveGameData`, `MonsterTeamManager`, and `MonsterManager` participate
  in serialization and persistence.
- `RewardData`, `EncounterData`, `MonsterEncounter`, and `GameEvent` connect
  encounters and rewards to progression.

Inventory display or logging is relatively approachable. Directly changing
save data, item identity, monster identity, or serialization is high risk:
test with disposable backups and validate loading after every change.

### World, maps, UI, and input

- `MapData`, `MapArea`, `MapMenu`, `MinimapManager`, scene classes, and many
  trigger/action classes cover exploration and world state.
- `UIController`, `IngameMenuController`, `MenuList`, `PopupController`,
  `OverlayController`, and the various menu/view classes cover UI.
- `InputController` uses Rewired-backed input mappings.

Small runtime UI panels and debug displays are practical through Unity objects
and BepInEx. New map geometry, triggers, NPCs, or polished menus generally
require serialized Unity assets and are substantially harder.

## What is easiest to modify

1. **Logging, diagnostics, and debug toggles**
   - Add a BepInEx plugin, config entries, keybinds, and scene/state logging.
   - Use `GameController.Instance`, `GameStateManager`, and manager properties
     only after checking null and current scene/state.

2. **Simple balance changes**
   - Harmony-patch a calculation or pre/post callback in `SkillManager`,
     `MonsterStats`, `CombatController`, or an `Action*` class.
   - Examples: damage multipliers, healing, mana costs, crit/dodge behavior,
     resistance/weakness behavior, reward quantity, or equipment bonuses.

3. **Quality-of-life behavior**
   - Patch menu selection, inventory quantities, monster switching, input, or
     small UI behavior while preserving the original method's state flow.

4. **Runtime-only overlays**
   - Create a `Canvas`/`GameObject` from the plugin and destroy it on unload or
     scene changes. Keep this separate from game asset editing.

These changes can usually be shipped as a small plugin DLL and do not require
modifying the original game files.

## What is moderately difficult

- Adding new configurable rules that touch several systems, such as a new
  combat modifier that must affect preview, execution, tooltips, and AI.
- Changing skill-tree behavior while preserving saved skill data and UI state.
- Altering encounter selection, loot, or progression without breaking
  achievement, quest, or save bookkeeping.
- Replacing or extending existing UI screens; this requires understanding the
  scene hierarchy, prefabs, TextMeshPro/tk2d components, and lifecycle timing.
- Reading/writing Unity assets or bundles with an external asset tool and then
  loading the replacement at runtime.

## What is hardest or riskiest

- **New monsters, skills, items, or equipment with original art/data.** The
  code classes alone are not enough; references, prefabs, icons, localization,
  animations, and serialized data may all need to agree.
- **New maps, NPCs, quests, cutscenes, or world geometry.** These are mostly
  serialized Unity scene/prefab/asset work and need reliable packaging and
  scene-loading hooks.
- **Save-format changes.** New persistent fields or changed IDs can invalidate
  existing saves. Prefer derived/runtime state or a separate mod config first.
- **Multiplayer/online changes.** `Team17.Online`, matchmaking, and transport
  types are present, but changing authoritative gameplay or network data can
  cause desyncs, incompatibilities, or unwanted online behavior.
- **Patching very early initialization.** Constructors, static initialization,
  `Awake`, and scene bootstrap code run before many singletons exist and are
  easy to break.

## Suggested workflow for a new mod

1. Use a managed decompiler to inspect `Assembly-CSharp.dll`, including the
   declaring type, overload, access level, and field/property types.
2. Start with a BepInEx plugin that only logs `Awake`, scene changes, and the
   values relevant to the intended feature.
3. Add a BepInEx config entry or keybind so the behavior can be disabled.
4. Add one narrow Harmony prefix/postfix around the smallest suitable method.
   Prefer changing arguments or results over replacing whole methods.
5. Build with `.\scripts\build.ps1`; it installs the generated DLL into the
   local `BepInEx\plugins` directory.
6. Test in a disposable save and check `BepInEx\LogOutput.log`. Test menus,
   scene transitions, combat start/end, save/load, and disabling the mod.
7. Keep assets, decompiled game code, DLL references, loader files, logs,
   `bin`, and `obj` out of Git. Commit only original source, scripts, and
   documentation.

## Useful inspection targets

Start with these classes and methods when investigating a feature:

- Combat/damage: `CombatController`, `SkillManager`, `ActionDamage`,
  `ActionHeal`, `ActionBuff`, `ActionDebuff`, `ActionShield`.
- Stats: `Monster`, `MonsterStats`, `EquipmentManager`, `PassiveSkill`.
- Monsters/teams: `MonsterManager`, `MonsterTeamManager`, `Monster`.
- Items/rewards: `InventoryManager`, `RewardData`, `CombatController` reward
  methods.
- World/lifecycle: `GameController`, `GameStateManager`, `MapData`.
- UI/input: `UIController`, `IngameMenuController`, `MenuList`,
  `InputController`.
- Persistence: `SaveGameManager`, `SaveGameData`, `PlayerSaveGameData`,
  `MonsterSaveGameData`.

The current starter plugin only logs that it loaded. It is intentionally safe
to replace with a focused experiment once a target method has been confirmed.
