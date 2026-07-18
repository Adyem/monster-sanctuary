using HarmonyLib;
using UnityEngine;

namespace MonsterSanctuaryMod;

internal static class PercentageAffixRuntime
{
    internal static float GetEquipmentFactor(
        EquipmentManager manager,
        GearAffix affix,
        bool applySecondarySlotPenalty)
    {
        if (!RandomizedModeState.Active || manager?.Equipment == null)
        {
            return 1f;
        }

        var factor = 1f;
        for (var index = 0; index < manager.Equipment.Length; index++)
        {
            var magnitude = GearRegistry.GetAffixMagnitude(manager.Equipment[index], affix);
            if (magnitude <= 0f)
            {
                continue;
            }

            // EquipmentManager gives its secondary slot half weight for ordinary
            // scalable stats. Maximum Health follows that convention; effect
            // multipliers behave like the game's native ShieldBonus and do not.
            var slotMultiplier = applySecondarySlotPenalty && index == 1 ? 0.5f : 1f;
            factor *= 1f + magnitude * slotMultiplier;
        }
        return factor;
    }

    internal static float GetCombinedDamageReduction(Monster monster)
    {
        if (monster == null)
        {
            return 0f;
        }

        var defenseReduction = Mathf.Clamp01(monster.GetDamageReduction());
        var statRemaining = Mathf.Max(0f, monster.CurrentStats.damageReduction);
        return 1f - (1f - defenseReduction) * statRemaining;
    }

    internal static float GetRemainingDamageMultiplierIncludingBuffs(Monster monster)
    {
        var remaining = 1f - GetCombinedDamageReduction(monster);
        if (monster?.BuffManager != null)
        {
            remaining *= Mathf.Max(0f, monster.BuffManager.GetDamageReductionMultiplier());
        }
        return remaining;
    }

    internal static float GetMinimumLayerMultiplier(float remainingBeforeLayer)
    {
        if (remainingBeforeLayer <= 0f)
        {
            return 1f;
        }

        return (1f - GearBalance.MaximumTotalDamageReduction) / remainingBeforeLayer;
    }

    internal static float ClampReductionLayer(float remainingBeforeLayer, float proposedMultiplier) =>
        Mathf.Max(proposedMultiplier, GetMinimumLayerMultiplier(remainingBeforeLayer));
}

[HarmonyPatch(typeof(EquipmentManager), nameof(EquipmentManager.CalculateMonsterStats))]
internal static class PercentageAffixMonsterStatsPatch
{
    private static void Postfix(EquipmentManager __instance, Monster ___monster)
    {
        if (!RandomizedModeState.Active || ___monster == null)
        {
            return;
        }

        var stats = ___monster.CurrentStats;
        stats.healthMulti *= PercentageAffixRuntime.GetEquipmentFactor(
            __instance,
            GearAffix.HealthPercent,
            true);
        stats.BuffMultiplier *= PercentageAffixRuntime.GetEquipmentFactor(
            __instance,
            GearAffix.BuffEffect,
            false);
        ___monster.CurrentStats = stats;
    }
}

[HarmonyPatch(typeof(EquipmentManager), nameof(EquipmentManager.CalculateEnemyStats))]
internal static class PercentageAffixEnemyStatsPatch
{
    private static void Postfix(EquipmentManager __instance, Monster __0)
    {
        if (!RandomizedModeState.Active || __0 == null)
        {
            return;
        }

        var factor = PercentageAffixRuntime.GetEquipmentFactor(
            __instance,
            GearAffix.DamageOverTime,
            false);
        if (Mathf.Approximately(factor, 1f))
        {
            return;
        }

        var stats = __0.CurrentStats;
        stats.poisonMultiplicator *= factor;
        stats.burnMultiplicator *= factor;
        stats.congealMultiplicator *= factor;
        __0.CurrentStats = stats;
    }
}

[HarmonyPatch(typeof(Monster), nameof(Monster.GetDamageReduction))]
internal static class DefenseReductionCapPatch
{
    private static void Postfix(Monster __instance, ref float __result)
    {
        if (RandomizedModeState.Active && __instance != null && __instance.BelongsToPlayer)
        {
            __result = Mathf.Min(__result, GearBalance.MaximumTotalDamageReduction);
        }
    }
}

[HarmonyPatch(typeof(Monster), nameof(Monster.CalculateCurrentStats))]
internal static class CombinedReductionCapPatch
{
    private static void Postfix(Monster __instance)
    {
        if (!RandomizedModeState.Active || __instance == null || !__instance.BelongsToPlayer)
        {
            return;
        }

        var defenseRemaining = 1f - Mathf.Clamp01(__instance.GetDamageReduction());
        if (defenseRemaining <= 0f)
        {
            return;
        }

        var minimumStatRemaining =
            PercentageAffixRuntime.GetMinimumLayerMultiplier(defenseRemaining);
        var stats = __instance.CurrentStats;
        if (stats.damageReduction < minimumStatRemaining)
        {
            stats.damageReduction = minimumStatRemaining;
            __instance.CurrentStats = stats;
        }
    }
}

[HarmonyPatch(typeof(BuffManager), nameof(BuffManager.GetDamageReductionMultiplier))]
internal static class BuffReductionCapPatch
{
    private static void Postfix(Monster ___monster, ref float __result)
    {
        if (!RandomizedModeState.Active || ___monster == null || !___monster.BelongsToPlayer)
        {
            return;
        }

        var remainingBeforeBuff = 1f - PercentageAffixRuntime.GetCombinedDamageReduction(___monster);
        if (remainingBeforeBuff <= 0f)
        {
            __result = 1f;
            return;
        }

        __result = PercentageAffixRuntime.ClampReductionLayer(remainingBeforeBuff, __result);
    }
}
