using BepInEx;
using UnityEngine;

namespace MonsterSanctuaryMod;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    private const string ModGuid = "com.adyem.monstersanctuary.mod";
    private const string ModName = "Monster Sanctuary Mod";
    private const string ModVersion = "0.1.0";

    private void Awake()
    {
        Logger.LogInfo($"{ModName} {ModVersion} loaded");
    }
}
