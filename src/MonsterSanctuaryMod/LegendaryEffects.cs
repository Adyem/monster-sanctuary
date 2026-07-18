using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MonsterSanctuaryMod;

internal static class LegendaryEffectRuntime
{
    private static int _actionSequence;
    private static readonly HashSet<string> Triggered = new();
    private static readonly HashSet<string> CombatTriggered = new();

    internal static void BeginAction()
    {
        _actionSequence++;
        Triggered.Clear();
    }

    internal static void BeginCombat()
    {
        _actionSequence = 0;
        Triggered.Clear();
        CombatTriggered.Clear();
    }

    internal static bool TryTrigger(LegendaryEffect effect, Monster target)
    {
        if (!RandomizedModeState.Active || target == null)
        {
            return false;
        }

        return Triggered.Add($"{_actionSequence}:{(int)effect}:{target.GetInstanceID()}");
    }

    internal static bool TryTriggerPerTurn(LegendaryEffect effect, Monster monster)
    {
        if (!RandomizedModeState.Active || monster == null || CombatController.Instance == null)
        {
            return false;
        }

        return CombatTriggered.Add(
            $"turn:{CombatController.Instance.TurnCount}:{(int)effect}:{monster.GetInstanceID()}");
    }

    internal static bool TryTriggerPerCombat(LegendaryEffect effect, Monster monster)
    {
        if (!RandomizedModeState.Active || monster == null)
        {
            return false;
        }

        return CombatTriggered.Add($"combat:{(int)effect}:{monster.GetInstanceID()}");
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
            switch (effect)
            {
                case LegendaryEffect.BarrierBloom:
                    if (!LegendaryEffectRuntime.TryTrigger(effect, monster)) break;
                    monster.AddShield(
                        caster,
                        Mathf.Max(1, Mathf.RoundToInt(healAmount * 0.25f)),
                        false,
                        true,
                        true);
                    break;
                case LegendaryEffect.PurifyingTouch:
                    if (!LegendaryEffectRuntime.TryTrigger(effect, monster)) break;
                    if (monster.BuffManager.GetTotalDebuffCount() > 0)
                    {
                        monster.BuffManager.RemoveRandomDebuff();
                    }
                    break;
                case LegendaryEffect.RallyingLight:
                    if (!LegendaryEffectRuntime.TryTrigger(effect, monster)) break;
                    monster.BuffManager.AddRandomPandoraBuff(caster);
                    break;
                case LegendaryEffect.ArcaneShelter:
                    if (!LegendaryEffectRuntime.TryTrigger(effect, monster)) break;
                    monster.ModifyMana(
                        Mathf.Max(1, Mathf.RoundToInt(monster.MaxMana * 0.05f)),
                        true,
                        true,
                        EManaHealSource.None);
                    break;
            }
        }
    }
}

[HarmonyPatch(typeof(ActionShield), "ShieldMonster")]
internal static class LegendaryShieldEffectsPatch
{
    private static void Postfix(ActionShield __instance, Monster monster, float shieldAmount)
    {
        var caster = __instance.Action?.Caster;
        if (!RandomizedModeState.Active || caster == null || monster == null || monster.IsDead())
        {
            return;
        }

        foreach (var effect in LegendaryEffectRuntime.Effects(caster))
        {
            switch (effect)
            {
                case LegendaryEffect.CleansingWard:
                    if (!LegendaryEffectRuntime.TryTrigger(effect, monster)) break;
                    if (monster.BuffManager.GetTotalDebuffCount() > 0)
                    {
                        monster.BuffManager.RemoveRandomDebuff();
                    }
                    break;
                case LegendaryEffect.EmpoweringWard:
                    if (!LegendaryEffectRuntime.TryTrigger(effect, monster)) break;
                    monster.BuffManager.AddRandomPandoraBuff(caster);
                    break;
                case LegendaryEffect.MendingWard:
                    if (!LegendaryEffectRuntime.TryTrigger(effect, monster)) break;
                    monster.Heal(
                        caster,
                        Mathf.Max(1, Mathf.RoundToInt(shieldAmount * 0.15f)),
                        true,
                        false,
                        false,
                        false,
                        false,
                        EManaHealSource.None);
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
        bool precalculation,
        int __result)
    {
        var caster = __instance.Action?.Caster;
        var target = damageItem?.target;
        if (precalculation ||
            !RandomizedModeState.Active ||
            caster == null ||
            target == null)
        {
            return;
        }

        var effects = LegendaryEffectRuntime.Effects(caster).ToList();
        if (effects.Contains(LegendaryEffect.Spellbreaker) &&
            !target.IsDead() &&
            LegendaryEffectRuntime.TryTrigger(LegendaryEffect.Spellbreaker, target) &&
            target.BuffManager.GetTotalBuffCount() > 0)
        {
            target.BuffManager.RemoveRandomBuff(__instance, caster, null);
        }

        if (effects.Contains(LegendaryEffect.HexingEdge) &&
            !target.IsDead() &&
            LegendaryEffectRuntime.TryTrigger(LegendaryEffect.HexingEdge, target))
        {
            target.BuffManager.AddRandomDebuff(caster, __instance.Action);
        }

        if (effects.Contains(LegendaryEffect.VampiricPulse) &&
            __result > 0 &&
            LegendaryEffectRuntime.TryTrigger(LegendaryEffect.VampiricPulse, target))
        {
            caster.Heal(
                caster,
                Mathf.Max(1, Mathf.RoundToInt(__result * 0.10f)),
                true,
                false,
                false,
                false,
                false,
                EManaHealSource.None);
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

[HarmonyPatch(typeof(EquipmentManager), nameof(EquipmentManager.OnCriticalHit))]
internal static class LegendaryCriticalMomentumPatch
{
    private static void Postfix(Monster ___monster)
    {
        if (!RandomizedModeState.Active || ___monster == null || ___monster.IsDead())
        {
            return;
        }

        if (LegendaryEffectRuntime.Effects(___monster).Contains(LegendaryEffect.CriticalMomentum) &&
            LegendaryEffectRuntime.TryTrigger(LegendaryEffect.CriticalMomentum, ___monster))
        {
            ___monster.BuffManager.AddRandomPandoraBuff(___monster);
        }
    }
}

[HarmonyPatch(typeof(SkillManager), nameof(SkillManager.OnActionFinished))]
internal static class LegendaryManaBatteryPatch
{
    private static void Postfix(Monster ___monster)
    {
        if (!RandomizedModeState.Active || ___monster == null || ___monster.IsDead())
        {
            return;
        }

        if (LegendaryEffectRuntime.Effects(___monster).Contains(LegendaryEffect.ManaBattery) &&
            LegendaryEffectRuntime.TryTriggerPerTurn(LegendaryEffect.ManaBattery, ___monster))
        {
            ___monster.ModifyMana(
                Mathf.Max(1, Mathf.RoundToInt(___monster.MaxMana * 0.08f)),
                true,
                true,
                EManaHealSource.None);
        }
    }
}

[HarmonyPatch(typeof(SkillManager), nameof(SkillManager.OnPostBeingHit))]
internal static class LegendaryDefensiveReactionPatch
{
    private static void Postfix(float damage, Monster ___monster)
    {
        if (!RandomizedModeState.Active ||
            ___monster == null ||
            ___monster.IsDead() ||
            damage <= 0f)
        {
            return;
        }

        var effects = LegendaryEffectRuntime.Effects(___monster).ToList();
        if (effects.Contains(LegendaryEffect.RetaliatoryWard) &&
            LegendaryEffectRuntime.TryTrigger(LegendaryEffect.RetaliatoryWard, ___monster))
        {
            ___monster.AddShield(
                ___monster,
                Mathf.Max(1, Mathf.RoundToInt(damage * 0.25f)),
                false,
                true,
                false);
        }

        if (effects.Contains(LegendaryEffect.LastBastion) &&
            ___monster.CurrentHealth > 0 &&
            ___monster.CurrentHealth <= Mathf.RoundToInt(___monster.MaxHealth * 0.30f) &&
            LegendaryEffectRuntime.TryTriggerPerCombat(LegendaryEffect.LastBastion, ___monster))
        {
            ___monster.AddShield(
                ___monster,
                Mathf.Max(1, Mathf.RoundToInt(___monster.MaxHealth * 0.20f)),
                false,
                true,
                false);
        }
    }
}

[HarmonyPatch(typeof(SkillManager), nameof(SkillManager.PreventDeath))]
internal static class LegendaryPhoenixOathPatch
{
    private static void Postfix(Monster ___monster, ref bool __result)
    {
        if (__result ||
            !RandomizedModeState.Active ||
            ___monster == null ||
            !LegendaryEffectRuntime.Effects(___monster).Contains(LegendaryEffect.PhoenixOath) ||
            !LegendaryEffectRuntime.TryTriggerPerCombat(LegendaryEffect.PhoenixOath, ___monster))
        {
            return;
        }

        ___monster.CurrentHealth = Mathf.Max(1, Mathf.RoundToInt(___monster.MaxHealth * 0.10f));
        __result = true;
    }
}

[HarmonyPatch(typeof(EquipmentManager), nameof(EquipmentManager.OnCombatStart))]
internal static class LegendaryOpeningGambitPatch
{
    private static void Postfix(Monster ___monster)
    {
        if (!RandomizedModeState.Active ||
            ___monster == null ||
            ___monster.IsDead() ||
            !LegendaryEffectRuntime.Effects(___monster).Contains(LegendaryEffect.OpeningGambit) ||
            !LegendaryEffectRuntime.TryTriggerPerCombat(LegendaryEffect.OpeningGambit, ___monster))
        {
            return;
        }

        ___monster.BuffManager.AddRandomPandoraBuff(___monster);
        ___monster.BuffManager.AddRandomPandoraBuff(___monster);
    }
}
