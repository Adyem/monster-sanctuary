using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MonsterSanctuaryMod;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    private const string ModGuid = "com.adyem.monstersanctuary.randomizedgear";
    private const string ModName = "Randomized Gear Mode";
    private const string ModVersion = "0.4.0";

    internal static ManualLogSource ModLog { get; private set; } = null!;
    internal static ConfigEntry<bool> DebugLogging { get; private set; } = null!;

    private Harmony? _harmony;

    private void Awake()
    {
        ModLog = Logger;
        DebugLogging = Config.Bind(
            "Diagnostics",
            "VerboseLogging",
            false,
            "Log generated item details and mode lifecycle events.");

        GearRegistry.Initialize();
        _harmony = new Harmony(ModGuid);
        _harmony.PatchAll();

        Logger.LogInfo($"{ModName} {ModVersion} loaded. Select Randomized Gear when starting a game to activate it.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        LegendaryTokenRegistry.Shutdown();
        GearRegistry.Shutdown();
    }

    internal static void Debug(string message)
    {
        if (DebugLogging.Value)
        {
            ModLog.LogInfo(message);
        }
    }
}
