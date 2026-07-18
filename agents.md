# AGENTS.md -- Monster Sanctuary modding workspace

This repository is a source workspace for BepInEx plugins running against the
local Windows Mono build of Monster Sanctuary. The game installation is also
the repository root, so treat the installed game as a local SDK and runtime,
not as source code that belongs in Git.

This document is written for an AI coding agent. Identifiers that refer to the
game assembly are deliberately kept in their exact spelling and casing. Do
not rename them when searching, reflecting, writing Harmony patches, or
describing a change.

## Scope and authority

The installed files are authoritative for this checkout. The game may have a
different assembly revision from another player's installation. Before using
any method below, verify its declaring type, overload, visibility, parameter
types, and return type in the local `Assembly-CSharp.dll`.

The public `garfieldbanks/MonsterSanctuaryMods` repository was consulted as an
external research source. This document does not reproduce its source code,
comments, or substantial documentation passages. Technical facts and game API
identifiers were independently summarized and, where stated, checked against
the local `Assembly-CSharp.dll`. The links used for that research are listed
in `Sources`.

## Repository layout

- `src/MonsterSanctuaryMod/` contains original C# source.
- `src/MonsterSanctuaryMod/MonsterSanctuaryMod.csproj` targets
  `net472` and references assemblies from this installation. This target was
  verified against the local Mono runtime; a `netstandard2.0` build failed at
  plugin load because this installation does not provide the required facade.
- `src/MonsterSanctuaryMod/Plugin.cs` is the `BaseUnityPlugin` entry point for
  the opt-in Randomized Gear game mode. Its supporting generation, registry,
  balance, and Harmony patch code lives beside it in the same directory.
- `scripts/build.ps1` builds Release and copies the plugin DLL into
  `BepInEx/plugins`.
- `Monster Sanctuary_Data/Managed/Assembly-CSharp.dll` is the main managed
  game assembly used for reflection and decompiler inspection.
- `Monster Sanctuary_Data/Managed/Assembly-CSharp-firstpass.dll` contains
  additional game/support types.
- `Monster Sanctuary_Data/Managed/UnityEngine*.dll` contains Unity APIs.
- `BepInEx/core/` contains `BepInEx.dll`, `BepInEx.Harmony.dll`, `0Harmony.dll`,
  and the other local loader libraries.
- `BepInEx/LogOutput.log` is the first place to check after launching the
  game.

The `.gitignore` intentionally excludes the installed game, loader files,
managed references, logs, plugins, `bin`, and `obj`. Do not weaken it to commit
copyrighted game data or generated binaries.

## How to implement a mod

### 1. State the behavior and its boundary

Write down whether the change affects exploration, combat, menus, rewards,
progression, or save/load. Identify whether it should affect player monsters,
enemy monsters, or both. Give each change a single responsibility and a clear
enable/disable boundary. This keeps unrelated patches from sharing hidden
state and makes failures easier to isolate.

Do not begin by replacing a whole controller method. First locate the smallest
calculation or callback that already represents the behavior.

### 2. Inspect the local assembly

Use a managed .NET decompiler or reflection tool against:

```text
Monster Sanctuary_Data/Managed/Assembly-CSharp.dll
```

For every candidate patch, record:

- the exact declaring type, including namespace if present;
- the exact overload and parameter order;
- whether arguments or the return value are passed by reference;
- whether the method is static, instance, public, or non-public;
- whether the method is called during preview, execution, AI evaluation, or
  save/load;
- whether it can run before the relevant singleton exists.

A useful reflection probe should print `MethodInfo.Name`,
`DeclaringType.FullName`, `ReturnType`, and each parameter's type and
`ParameterInfo.ParameterType.IsByRef`. Do not infer signatures from a method
name alone.

### 3. Start with an observable plugin

The entry point should derive from `BaseUnityPlugin` and use a stable
`[BepInPlugin(...)]` GUID. Log one startup message and, for experiments, log
only the relevant event or value. Avoid logging every frame or every damage
hit once the target is understood.

Use null checks around objects obtained from `GameController.Instance`,
`GameStateManager.Instance`, `PlayerController.Instance`,
`MonsterTeamManager.Instance`, and other singleton properties. A plugin can
load while a scene-specific manager is not yet available.

### 4. Add a setting before adding a permanent tweak

Use BepInEx `ConfigEntry<T>` for settings that belong in the configuration
file. Give settings clear descriptions, safe defaults, and sensible ranges.
For a toggle, default to `false` unless the feature is a diagnostic or a bug
fix that is safe for every save. For larger groups of optional tweaks, a mod
menu integration can expose the same setting at runtime.

Do not silently change a setting while the game is in combat or during a scene
transition if the target system caches derived state. Settings that alter
progression calculations, including level-cap configuration, should be edited
between sessions; restart the game after changing them.

### 5. Patch the smallest suitable method

Use Harmony/HarmonyX through the local BepInEx installation. A prefix is
appropriate for changing input arguments, replacing a simple result, or
skipping a method only when the complete behavior is understood. A postfix is
appropriate for adjusting a returned value or observing completed work. A
transpiler is a last resort for a small stable instruction change when no
argument/result hook exists.

For methods with `ref` arguments, preserve the by-reference signature exactly.
For example, `SkillManager.OnActionDamagePreHit` and
`SkillManager.OnActionHealTarget` expose mutable values through `ref` damage
or healing parameters. A patch must use the matching `ref` parameter and must
not confuse preview values with executed values.

Keep patches idempotent. If a callback may execute more than once for a
multi-hit action, apply a modifier at the intended phase and do not multiply an
already modified value again. Use the action, target, source, proc type, and
combat state to scope the change where necessary.

### 6. Build, install, and test

Run:

```powershell
.\scripts\build.ps1
```

This performs a Release build and copies
`MonsterSanctuaryMod.dll` into `BepInEx/plugins`. If PowerShell execution
policy blocks the script, do not alter machine policy as part of the mod; run
the equivalent Release `dotnet build` for verification and copy only the
intended DLL when authorized.

Test in this order:

1. plugin load and configuration parsing;
2. a new game or disposable save;
3. entering and leaving the affected scene;
4. combat preview and real execution, if applicable;
5. menu open/close and controller/keyboard input;
6. save, reload, and a second session with the feature disabled;
7. `BepInEx/LogOutput.log` for exceptions, patch failures, and duplicate
   initialization.

Never test save-format or progression changes only on the primary save.

## Verified game API map

The following names were observed in this installation's
`Assembly-CSharp.dll`. They are starting points, not a substitute for checking
the complete signature before patching.

### Global state and scenes

`GameController` exposes `Instance`, `CurrentSceneName`, `GameState`,
`Combat`, `InputController`, `GameModes`, `IsStoryMode`, and
`OnSceneLoaded(Scene scene, LoadSceneMode mode)`. Scene changes include
`ChangeScene()` and `StartSceneChange(String targetScene, Vector2 originPosition,
SceneChangeType changeType, Single timer, String taggedObject,
Boolean activatePlayerAfterTeleport)`.

`GameStateManager` exposes `Instance`, `State`, and `LastState`. Useful checks
are `IsCombat()`, `IsExploring()`, `IsLoadingScene()`, `IsMainMenu()`, and
`IsInputAllowed(GameStates state)`. State changes go through
`SetState(GameStates state, Single duration)`, while
`OnStateChanged(GameStates prevState, GameStates newState)` is an observation
point.

Use `GameController.OnSceneLoaded` or a scene-aware component for lifecycle
work. Do not create persistent UI or cache scene objects without handling
`OnDestroy`, scene unload, and repeated scene loads.

### Combat and action effects

`CombatController` coordinates the battle lifecycle. Relevant methods include
`StartCombat`, `StartAction`, `PreviewAction`, `SelectAction`, `SelectTarget`,
`StartPlayerTurn`, `StartEnemyTurn`, `StartNextTurn`, `CheckEndCombat`,
`WinCombat`, `LoseCombat`, `GrantReward`, `UseConsumable`, and
`SwitchMonster`. State and context properties include `Instance`, `State`,
`IsPlayerTurn`, `TurnCount`, `PlayerMonsters`, `Enemies`,
`CurrentEncounter`, `CurrentMonster`, `TargetEnemies`, and
`TargetPlayerMonsters`.

Use lifecycle methods for observation or narrowly scoped behavior. Do not
patch turn queue or state transitions unless a soft-lock test plan exists.

The concrete action types are `ActionDamage`, `ActionHeal`, `ActionBuff`,
`ActionDebuff`, `ActionShield`, and `ActionRevive`.

For damage, inspect `ActionDamage.CalculateDamage`,
`ActionDamage.PrecalculateDamage`, `ActionDamage.HitMonster`,
`ActionDamage.AddDamageToQueue`, and `ActionDamage.StartAction`. Calculation
and preview are separate concerns; changing only `HitMonster` can make the
displayed preview disagree with the result.

For healing, inspect `ActionHeal.CalculateBaseHealValue`,
`ActionHeal.PrecalculateHeal`, `ActionHeal.HealMonster`, and
`ActionHeal.StartAction`. For shields, the corresponding methods are
`ActionShield.CalculateBaseShieldValue`, `ActionShield.PrecalculateShield`,
`ActionShield.ShieldMonster`, and `ActionShield.StartAction`.

### Skill, passive, and combat callbacks

`SkillManager` is the preferred starting point for many balance changes. Its
calculation methods include `GetDamageMultiplier`, `GetCritChanceBonus`,
`GetCritDamageBonus`, `GetDodgeChance`, `GetManaModifier`,
`GetEquipmentStatMultiplier`, `HasResistance`, `HasWeakness`, and
`IsActionHitUndodgeable`.

Its event-style methods provide more targeted hooks:

- `OnActionDamagePreHit` and `OnActionDamageHit` for damage adjustments;
- `OnActionDamagePostHit` for after-hit behavior;
- `OnActionHealTarget` for healing adjustments;
- `OnActionShieldTarget` for shield adjustments;
- `OnActionStarted`, `OnActionFinished`, and `OnActionDamageStarted` for
  action lifecycle observation;
- `OnApplyBuff`, `OnApplyDebuffToEnemy`, `OnReceiveBuff`, and
  `OnReceiveShield` for effect-related behavior;
- `OnManaConsumed` and `OnManaRegenerated` for resource events;
- `OnEnemyDeath`, `OnCombatStart`, `OnMonsterUpkeep`, and `PreventDeath` for
  broader passive/effect logic.

Skill-tree changes should begin with `LearnSkill`, `UnlearnSkill`,
`SetUltimate`, `ValidateSkillTree`, `LoadGame`, `SaveGame`, and
`ResetSkills`. Preserve the game's serialization flow. A rule that changes
what the UI displays but not what `SaveGame` stores will create misleading or
lost skill state.

### Monsters, teams, and stats

`MonsterManager` owns runtime collections exposed as `Active`, `Inactive`,
`FarmMonsters`, `Permadead`, `Familiar`, and `AllMonster`. Useful methods are
`GetActiveMonster`, `GetMonsterById`, `GetMonsterByIndex`, `GetHighestLevel`,
`GetHighestHatchableLevel`, `AddMonsterByPrefab`, `Remove`, `SwitchMonsters`,
`LoadGame`, and `SaveGame`.

`MonsterTeamManager` exposes `MonsterTeams` and has `LoadTeam`, `SaveTeam`,
`DeleteTeam`, `SwapTeams`, `GetMonsterByTeamId`, `StoreSkillsForMarkedTeams`,
`LoadGame`, and `SaveGame`. Team operations can mutate equipment and learned
skills; test all active and saved teams after patching them.

For stat work, inspect `Monster`, `MonsterStats`, `EquipmentManager`,
`Equipment`, `PassiveSkill`, and `SkillManager.CalculateCurrentStats`,
`SkillManager.CalculateCurrentStatsPostMultiplication`,
`SkillManager.CalculateTeamMonsterStats`, and
`SkillManager.CalculateEnemyStats`.

`GameModeManager.RelicMode` identifies saves using Relics of Chaos. Relic
equipment is marked by `Equipment.IsRelic`; this installation contains both
plain `Equipment` relics and specialized types such as `EquipmentAddBuff`,
`EquipmentDamageHit`, `EquipmentElementalDamageIncrease`,
`EquipmentRemoveDebuff`, and `EquipmentShield`. Their behavior is dispatched
through virtual callbacks including `OnCombatStart`, `OnTurnStart`,
`OnActionStarted`, `OnActionDamageStarted`, `OnActionDamageHit`,
`OnCriticalHit`, and `GetBuffStackCountIncrease`. Preserve restrictions held
in `MonsterTypeRestriction`, `OnlyFamiliars`, and `OnlyUnshifted` when adapting
a relic effect. Do not infer a relic's special behavior from its display name;
inspect its concrete local type and fields.

### Inventory, rewards, and persistence

`InventoryManager` exposes item lists such as `Weapons`, `Accessories`,
`Consumables`, `CombatConsumables`, `Food`, `Eggs`, `Catalysts`, `Uniques`,
and `CraftMaterials`. Main operations are `AddItem`, `RemoveItem`, `GetItem`,
`GetItemQuantity`, `GetListByItemType`, `HasEgg`, `HasUniqueItem`, `LoadGame`,
and `SaveGame`.

`CombatController.AddRewardItem` and `CombatController.GetRandomReward` are
reward-related inspection points. `GrantReward` is broader and should be
patched only after understanding gold, eggs, equipment, achievements, and
one-time progression effects.

`SaveGameManager` exposes `CurrentlyLoadedData`, `SaveGameSlot`,
`LoadSaveGame`, `SaveGame`, `CopySavegame`, `DeleteSavegame`,
`GetSavegamePath`, `GetSavegameReadPath`, `LoadOptions`, `SaveOptions`, and
`SetupEncounterLevels`. Do not change binary save types, IDs, or serialization
methods for a convenience feature. Prefer runtime state or a separate BepInEx
config file, and make a backup before testing persistence patches.

### Player, input, and UI

`PlayerController` exposes `Instance`, `Monsters`, `MonsterTeams`, `Gold`,
`PlayerPosition`, `BlobForm`, `NewGamePlus`, `InputAllowed()`,
`SetDirection`, `PlayAnimation`, `OpenMenu`, `CloseMenu`, `LoadGame`, and
`SaveGame`. Movement or form changes should respect `GameStateManager` and
the player state controller.

`InputController` exposes `Instance`, `RewiredPlayer`, `CurrentlyUsingKeyboard`,
`GetKey`, `GetKeyDown`, `GetKeyUp`, `GetHorizontalAxis`,
`GetVerticalAxis`, `GetActionMapping`, and `GetInputEntry`. Prefer existing
input mappings over reading Unity's raw input directly.

`UIController` exposes `Instance`, `ShowSkipButton`, `HideSkipButton`,
`SetSkipBarProgress`, `ShowSavingSign`, `PositionTimer`, and `Reset`. Runtime
overlays should be separate `GameObject`/`Canvas` objects owned by the plugin,
and must be destroyed or refreshed when scenes change.

## Patch design rules

- Patch a calculation method when the rule should affect all consumers; patch
  an execution callback when it should affect only a specific action phase.
- If both preview and execution matter, locate and patch both deliberately,
  with a shared pure calculation to prevent divergence.
- Check `GameStateManager.IsCombat()` or `IsExploring()` before applying
  context-specific behavior.
- Check the action's initiator, target, element, proc type, and whether the
  target is an enemy or ally before changing combat values.
- Do not assume an `Int32` is interchangeable with a `Single`; preserve the
  exact parameter and property types.
- Avoid modifying collection properties in place unless ownership and save
  semantics are understood.
- Do not patch `Awake`, constructors, or static initialization first. They run
  before many singletons and can break every scene.
- Unpatch or destroy plugin-owned objects on disable when the feature can be
  toggled during a session.
- Log the chosen patch target and configuration once at startup so a failed
  patch is diagnosable without flooding the game log.

## Common feature recipes

### Damage or healing modifier

1. Inspect `ActionDamage.PrecalculateDamage` and
   `ActionDamage.HitMonster`, or `ActionHeal.PrecalculateHeal` and
   `ActionHeal.HealMonster`.
2. Inspect the matching `SkillManager` callbacks, especially
   `OnActionDamagePreHit` / `OnActionDamageHit` or
   `OnActionHealTarget`.
3. Decide whether tooltips and previews must change.
4. Scope the modifier by source, target, element, action, and config toggle.
5. Test critical hits, multi-hit actions, counterattacks, damage-over-time,
   shields, and enemy turns.

### Inventory or reward quality of life

1. Inspect `InventoryManager.AddItem`, `RemoveItem`, and
   `GetItemQuantity`, then trace callers.
2. For combat rewards, inspect `CombatController.AddRewardItem`,
   `GetRandomReward`, and `GrantReward`.
3. Preserve item variation, equipment uniqueness, egg handling, and required
   progression items.
4. Test buying, selling, equipping, upgrading, consuming, saving, and loading.

### Movement or exploration ability

1. Find the ability check in the relevant player, trigger, or map component;
   do not globally skip all collision or state logic.
2. Use `PlayerController.InputAllowed()` and
   `GameStateManager.IsExploring()` to avoid applying movement during menus,
   cinematics, loading, or combat.
3. Test scene transitions, water, mounts, forms, falling, doors, and triggers.

### Skill-tree or New Game Plus behavior

1. Trace `SkillManager.LearnSkill`, `UnlearnSkill`, `SetUltimate`,
   `LoadGame`, and `SaveGame`.
2. Trace `MonsterManager.LoadGame` / `SaveGame` and
   `MonsterTeamManager.LoadTeam` / `SaveTeam` if team state is involved.
3. Test a fresh game, an existing save, New Game Plus, skill reset, team
   switching, and a reload after disabling the feature.

## Debugging checklist

When a patch appears not to work, check these in order:

1. The DLL is in `BepInEx/plugins` and the plugin GUID is unique.
2. `BepInEx/LogOutput.log` contains the plugin startup line.
3. Harmony found the exact overload and did not report an exception.
4. The method is actually called in the relevant mode; preview and execution
   often use different methods.
5. The patch signature matches every parameter, including `ref` parameters.
6. The feature config is enabled and was not overwritten by a stale config.
7. The target object is not null because the patch ran during a scene change.
8. The value was not recalculated later by `SkillManager` or an action queue.
9. The same patch is not installed twice by duplicate DLLs.

For difficult cases, add temporary logs containing the method phase, monster
name or instance identifier, action type, target, original value, modified
value, and current scene. Remove verbose diagnostics after the issue is
understood.

## Build and Git rules

The normal build is:

```powershell
.\scripts\build.ps1
```

The project currently compiles for `net472`. References to BepInEx,
Unity, Harmony, and game assemblies are machine-local and must not be copied
into source control. Commit original `.cs`, `.csproj`, `.ps1`, documentation,
and configuration templates only. Do not commit a compiled plugin, game DLL,
decompiled game source, save files, logs, or loader installation files.

Before handing off a change, run a Release build, inspect `git diff`, run
`git diff --check`, and report any game-version assumptions or untested save
behavior.

## External research and attribution

The following background informed this document after review of the public
upstream project. The recommendations below are this workspace's own
engineering rules, not copied project text:

- Its public layout demonstrates a practical BepInEx plugin workflow for this
  game family; this workspace adapts that idea to its own project structure.
- Its feature list helped identify useful areas to investigate, while the
  method names in this file come from the local managed assembly inspection.
- Its gameplay edge cases reinforced the need for session boundaries,
  progression reachability checks, and save/load testing; the specific rules
  above are written for this workspace and should be validated locally.

No upstream C# implementation, comments, or substantial prose is reproduced
here. Game class and method identifiers in this file come from reflection of
the local installed `Assembly-CSharp.dll`, not from a promise that another game
version has the same API.

### Sources

- [garfieldbanks/MonsterSanctuaryMods](https://github.com/garfieldbanks/MonsterSanctuaryMods)
  -- upstream project overview, build notes, plugin organization, and feature
  documentation.
- [Upstream README](https://github.com/garfieldbanks/MonsterSanctuaryMods/blob/main/README.md)
  -- external build and installation reference consulted for this document.
- Local runtime reference: `Monster Sanctuary_Data/Managed/Assembly-CSharp.dll`
  -- inspected in this installation to verify the API names listed above.
