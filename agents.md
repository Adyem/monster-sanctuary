# Monster Sanctuary modding notes

## Local game facts

- The game is a Unity Mono build: `Monster Sanctuary_Data\Managed` contains
  `Assembly-CSharp.dll` and the Unity managed modules.
- The repository root is the Steam install directory, but game content and
  third-party loader binaries must remain local.
- BepInEx Mono with Unity Doorstop is the intended runtime loading baseline.

## Repository rules

- Commit mod source, project files, scripts, and notes only.
- Never force-add files from the game installation or `BepInEx`.
- Keep generated `bin`/`obj` output and installed plugin DLLs local.

## Useful next steps

- Inspect `Assembly-CSharp.dll` with a managed decompiler.
- Add Harmony patches only after identifying stable game methods.
- Keep experimental patches behind config flags and test on a backed-up save.
