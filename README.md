# Monster Sanctuary modding workspace

This repository is rooted in the local Monster Sanctuary installation so the
mod project can reference the game's managed Unity assemblies directly.

The Steam installation itself is intentionally ignored. Only mod source,
build scripts, and documentation should be committed.

## Setup

1. Install the local BepInEx/Doorstop files if they are not already present.
2. Build and install the starter plugin:

   ```powershell
   .\scripts\build.ps1
   ```

3. Start the game and check `BepInEx\LogOutput.log` for the plugin load message.

The project uses the managed assemblies under `Monster Sanctuary_Data\Managed`
as references; those files remain local and are never committed.
