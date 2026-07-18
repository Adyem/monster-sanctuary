using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MonsterSanctuaryMod;

internal static class LegendaryEffectRuntime
{
    private static int _actionSequence;
    private static readonly HashSet<string> Triggered = new();

    internal static void BeginAction()
    {
        _actionSequence++;
        Triggered.Clear();
    }

    internal static bool TryTrigger(LegendaryEffect effect, Monster target)
    {
        if (!RandomizedModeState.Active || target == null)
        {
            return false;
        }

        return Triggered.Add($"{_actionSequence}:{(int)effect}:{target.GetInstanceID()}");
    }

    internal static IEnumerable<LegendaryEffect> Effects(Monster caster) =>
        GearRegistry.GetLegendaryEffects(caster).Distinct();
}

[HarmonyPatch(typeof(CombatController), nameof(CombatController.StartAction))]
internal static class LegendaryActionSequencePatch
{
    private static void Prefix() => LegendaryEffectRuntime.BeginAction();
}

[HarmonyPatch(typeof(ActionHeal), "HealMonster")]
internal static class LegendaryHealEffectsPatch
{
    private static void Postfix(ActionHeal __instance, Monster monster, float healAmount)
    {
        var caster = __instance.Action?.Caster;
        if (!RandomizedModeState.Active || caster == null || monster == null || monster.IsDead())
        {
            return;
        }

        foreach (var effect in LegendaryEffectRuntime.Effects(caster))
        {
            if (!LegendaryEffectRuntime.TryTrigger(effect, monster))
            {
                continue;
            }

            switch (effect)
            {
                case LegendaryEffect.BarrierBloom:
                    monster.AddShield(
                        caster,
                        Mathf.Max(1, Mathf.RoundToInt(healAmount * 0.25f)),
                        false,
                        true,
                        true);
                    break;
                case LegendaryEffect.PurifyingTouch:
                    if (monster.BuffManager.GetTotalDebuffCount() > 0)
                    {
                        monster.BuffManager.RemoveRandomDebuff();
                    }
                    break;
                case LegendaryEffect.RallyingLight:
                    monster.BuffManager.AddRandomPandoraBuff(caster);
                    break;
            }
        }
    }
}

[HarmonyPatch(typeof(ActionShield), "ShieldMonster")]
internal static class LegendaryShieldEffectsPatch
{
    private static void Postfix(ActionShield __instance, Monster monster)
    {
        var caster = __instance.Action?.Caster;
        if (!RandomizedModeState.Active || caster == null || monster == null || monster.IsDead())
        {
            return;
        }

        foreach (var effect in LegendaryEffectRuntime.Effects(caster))
        {
            if (!LegendaryEffectRuntime.TryTrigger(effect, monster))
            {
                continue;
            }

            switch (effect)
            {
                case LegendaryEffect.CleansingWard:
                    if (monster.BuffManager.GetTotalDebuffCount() > 0)
                    {
                        monster.BuffManager.RemoveRandomDebuff();
                    }
                    break;
                case LegendaryEffect.EmpoweringWard:
                    monster.BuffManager.AddRandomPandoraBuff(caster);
                    break;
            }
        }
    }
}

[HarmonyPatch(typeof(ActionDamage), "HitMonster")]
internal static class LegendaryDamageEffectsPatch
{
    private static void Postfix(
        ActionDamage __instance,
        ActionDamage.DamageQueueItem damageItem,
        bool precalculation)
    {
        var caster = __instance.Action?.Caster;
        var target = damageItem?.target;
        if (precalculation ||
            !RandomizedModeState.Active ||
            caster == null ||
            target == null ||
            target.IsDead())
        {
            return;
        }

        if (LegendaryEffectRuntime.Effects(caster).Contains(LegendaryEffect.Spellbreaker) &&
            LegendaryEffectRuntime.TryTrigger(LegendaryEffect.Spellbreaker, target) &&
            target.BuffManager.GetTotalBuffCount() > 0)
        {
            target.BuffManager.RemoveRandomBuff(__instance, caster, null);
        }
    }
}

[HarmonyPatch(typeof(EquipmentManager), nameof(EquipmentManager.OnReceiveBuff))]
internal static class LegendaryReceiveBuffEffectsPatch
{
    private static void Postfix(Monster ___monster)
    {
        if (!RandomizedModeState.Active || ___monster == null || ___monster.IsDead())
        {
            return;
        }

        if (LegendaryEffectRuntime.Effects(___monster).Contains(LegendaryEffect.InspiringAegis))
        {
            ___monster.AddShield(
                ___monster,
                Mathf.Max(1, Mathf.RoundToInt(___monster.MaxHealth * 0.08f)),
                false,
                true,
                false);
        }
    }
}
