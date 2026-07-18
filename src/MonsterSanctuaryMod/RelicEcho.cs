using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MonsterSanctuaryMod;

internal static class RelicEchoCatalog
{
    private static readonly HashSet<string> SupportedRelics = new(StringComparer.Ordinal)
    {
        "Ancient Clock", "Arch Bow", "Assisting Bow", "Cursed Drum", "Devonian Medal",
        "Dragon Saddle", "Droid Sphere", "Dwarven Crown", "Earth Symbol", "Fire Symbol",
        "Fishing Pole", "Gold Feather", "Grey Stone", "Grim Ripper", "Heavy Greaves",
        "Holy Necklace", "Infinity Blaze", "Lion Fang", "Nimble Wings", "One Punch Fist",
        "Pandora's Chest", "Projectile Sphere", "Pure Leaf", "Reptilian Idol", "Sharp Fin",
        "Slimy Ball", "Spirit Blaze", "Staff of Doom", "Static Loop", "Tiny Pin", "Toxic Pot",
        "Warlock Hat", "Water Symbol", "Wind Symbol"
    };

    internal static int Roll(bool weapon)
    {
        if (GameModeManager.Instance?.RelicMode != true)
        {
            return 0;
        }

        var candidates = GetRelics()
            .Where(relic => (relic.Type == Equipment.EquipmentType.Weapon) == weapon)
            .ToList();
        return candidates.Count == 0 ? 0 : candidates[UnityEngine.Random.Range(0, candidates.Count)].ID;
    }

    internal static bool TryGet(GearRecord record, out Equipment relic)
    {
        relic = null!;
        if (record.RelicEchoEquipmentId <= 0 || GameModeManager.Instance?.RelicMode != true)
        {
            return false;
        }

        var resolved = GameController.Instance?.WorldData?.GetReferenceable(record.RelicEchoEquipmentId) as Equipment;
        if (resolved == null || !resolved.IsRelic || !SupportedRelics.Contains(resolved.GetOriginalName()))
        {
            return false;
        }

        relic = resolved;
        return true;
    }

    internal static bool TryGet(Equipment generated, out Equipment relic)
    {
        relic = null!;
        return GearRegistry.TryGetRecord(generated, out var record) && TryGet(record, out relic);
    }

    internal static void Apply(Equipment equipment, GearRecord record)
    {
        if (!TryGet(record, out var relic))
        {
            return;
        }

        // Flat relic stats are deliberately excluded. The generated affix budget
        // remains authoritative, while percentage effects, drawbacks and equip
        // restrictions form the inherited Relic Echo.
        equipment.DamageBonus += relic.DamageBonus;
        equipment.DamageReduction += relic.DamageReduction;
        equipment.DodgeChance += relic.DodgeChance;
        equipment.FirstHitDamageIncrease += relic.FirstHitDamageIncrease;
        equipment.HealBonus += relic.HealBonus;
        equipment.ShieldBonus += relic.ShieldBonus;
        equipment.LifestealPercent += relic.LifestealPercent;
        equipment.NonCritDamage += relic.NonCritDamage;
        equipment.IsInstrument = relic.IsInstrument;
        equipment.OnlyFamiliars = relic.OnlyFamiliars;
        equipment.OnlyUnshifted = relic.OnlyUnshifted;
        equipment.MonsterTypeRestriction = relic.MonsterTypeRestriction;
    }

    internal static string GetDescription(Equipment relic) => relic.GetOriginalName() switch
    {
        "Ancient Clock" => "allied Age stacks are twice as effective; Ancient monsters only",
        "Arch Bow" => "attacks produce three additional hits at 20% power",
        "Assisting Bow" => "attacks produce three additional hits at 20% power",
        "Cursed Drum" => "healing is 30% stronger, but incoming damage is increased by 10%",
        "Devonian Medal" => "starts combat with 3 Age and 10 Charge stacks; Insect monsters only",
        "Dragon Saddle" => "restores 15% maximum health each turn; Dragon monsters only",
        "Droid Sphere" => "gains 1000 shield each turn; Construct monsters only",
        "Dwarven Crown" => "starts combat with shield equal to 200% of Defense",
        "Earth Symbol" => "Earth damage is 20% stronger and Earth hits may Poison; incoming damage +10%",
        "Fire Symbol" => "Fire damage is 20% stronger and Fire hits may Burn; incoming damage +10%",
        "Fishing Pole" => "non-damaging actions grant two random buffs",
        "Gold Feather" => "attacking or being attacked grants a random buff or applies a random debuff; Bird monsters only",
        "Grey Stone" => "non-damaging actions grant three random buffs and defensive support is amplified; unshifted monsters only",
        "Grim Ripper" => "heals for 20% of damage dealt, but incoming damage is increased by 10%",
        "Heavy Greaves" => "hits have a 10% chance to apply Weakness or Armor Break",
        "Holy Necklace" => "allows one additional half-strength stack of every buff",
        "Infinity Blaze" => "damage, healing, shielding and damage reduction are increased by 25%; Spectral Familiars only",
        "Lion Fang" => "damage and damage reduction are increased by 10%; Beast monsters only",
        "Nimble Wings" => "adds 15% dodge chance; Aerial monsters only",
        "One Punch Fist" => "the first hit deals 30% more damage",
        "Pandora's Chest" => "grants four random buffs at the start of each turn",
        "Projectile Sphere" => "attacks produce two additional hits at 40% power and support output is increased by 20%",
        "Pure Leaf" => "healing, buffing and shielding actions remove two debuffs; Nature monsters only",
        "Reptilian Idol" => "grants two random buffs each turn; Reptile monsters only",
        "Sharp Fin" => "attackers receive Armor Break; Aquatic monsters only",
        "Slimy Ball" => "enemy debuffs are 15% more effective; Slime monsters only",
        "Spirit Blaze" => "critical hits have a 25% chance to grant a random buff; Spirit monsters only",
        "Staff of Doom" => "hits and heals may apply a random debuff; Occult monsters only",
        "Static Loop" => "non-critical damage is increased by 20%",
        "Tiny Pin" => "damage +30%, damage reduction +10%, and 30% lifesteal",
        "Toxic Pot" => "enemy debuffs are 20% more effective",
        "Warlock Hat" => "damage, healing and shielding are increased by 13%; Mage monsters only",
        "Water Symbol" => "Water damage is 20% stronger and Water hits may Chill; incoming damage +10%",
        "Wind Symbol" => "Wind damage is 20% stronger and Wind hits may Shock; incoming damage +10%",
        _ => "inherits this relic's special behavior and restrictions"
    };

    internal static void Validate()
    {
        var relics = GetRelics();
        var weapons = relics.Count(relic => relic.Type == Equipment.EquipmentType.Weapon);
        var accessories = relics.Count - weapons;
        Plugin.ModLog.LogInfo($"Relic Echo validation passed ({weapons} weapon and {accessories} accessory donors)." );
    }

    private static List<Equipment> GetRelics() =>
        GameController.Instance?.WorldData?.Referenceables
            .OfType<Equipment>()
            .Where(relic => relic != null && relic.IsRelic && relic.UpgradesTo == null &&
                            SupportedRelics.Contains(relic.GetOriginalName()))
            .GroupBy(relic => relic.ID)
            .Select(group => group.First())
            .ToList() ?? new List<Equipment>();
}

internal static class RelicEchoForwarder
{
    internal static bool TryGet(Equipment generated, out Equipment relic) =>
        RelicEchoCatalog.TryGet(generated, out relic);
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.GetBuffStackCountIncrease))]
internal static class RelicEchoBuffStackPatch
{
    private static void Postfix(Equipment __instance, BuffManager.BuffType __0, ref int __result)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic))
        {
            __result += relic.GetBuffStackCountIncrease(__0);
        }
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.CalculateAllyMonsterStats))]
internal static class RelicEchoAllyStatsPatch
{
    private static void Postfix(Equipment __instance, Monster __0, float __1)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.CalculateAllyMonsterStats(__0, __1);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.CalculateEnemyStats))]
internal static class RelicEchoEnemyStatsPatch
{
    private static void Postfix(Equipment __instance, Monster __0)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.CalculateEnemyStats(__0);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnCombatStart))]
internal static class RelicEchoCombatStartPatch
{
    private static void Postfix(Equipment __instance, Monster __0)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnCombatStart(__0);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnTurnStart))]
internal static class RelicEchoTurnStartPatch
{
    private static void Postfix(Equipment __instance, Monster __0)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnTurnStart(__0);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnReceiveBuff))]
internal static class RelicEchoReceiveBuffPatch
{
    private static void Postfix(Equipment __instance, Monster __0)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnReceiveBuff(__0);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnActionStarted))]
internal static class RelicEchoActionStartedPatch
{
    private static void Postfix(Equipment __instance, BaseAction __0)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnActionStarted(__0);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnActionBuffStarted))]
internal static class RelicEchoActionBuffPatch
{
    private static void Postfix(Equipment __instance, ActionBuff __0)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnActionBuffStarted(__0);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnActionHealStarted))]
internal static class RelicEchoActionHealPatch
{
    private static void Postfix(Equipment __instance, ActionHeal __0)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnActionHealStarted(__0);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnActionShieldStarted))]
internal static class RelicEchoActionShieldPatch
{
    private static void Postfix(Equipment __instance, ActionShield __0)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnActionShieldStarted(__0);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnActionDamageStarted))]
internal static class RelicEchoActionDamageStartedPatch
{
    private static void Postfix(Equipment __instance, ActionDamage __0, float __1)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnActionDamageStarted(__0, __1);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnCriticalHit))]
internal static class RelicEchoCriticalHitPatch
{
    private static void Postfix(Equipment __instance, BaseAction __0, float __1)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnCriticalHit(__0, __1);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnGettingAttacked))]
internal static class RelicEchoGettingAttackedPatch
{
    private static void Postfix(Equipment __instance, Monster __0, ActionDamage __1)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnGettingAttacked(__0, __1);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnAllyApplySpecialBuff))]
internal static class RelicEchoSpecialBuffPatch
{
    private static void Postfix(Equipment __instance, Monster __0, BuffManager.ESpecialBuff __1, int __2)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnAllyApplySpecialBuff(__0, __1, __2);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnHealTarget))]
internal static class RelicEchoHealTargetPatch
{
    private static void Postfix(Equipment __instance, Monster __0, Monster __1)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnHealTarget(__0, __1);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnActionDamageHit))]
internal static class RelicEchoDamageHitPatch
{
    private static void Postfix(Equipment __instance, ActionDamage __0, Monster __1, ref float __2, bool __3)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnActionDamageHit(__0, __1, ref __2, __3);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnCounterAttackHit))]
internal static class RelicEchoCounterHitPatch
{
    private static void Postfix(Equipment __instance, Monster __0, Monster __1, ref float __2, bool __3, float __4, float __5)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnCounterAttackHit(__0, __1, ref __2, __3, __4, __5);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnActionDamagePostHit))]
internal static class RelicEchoDamagePostHitPatch
{
    private static void Postfix(Equipment __instance, ActionDamage __0, Monster __1, float __2, bool __3)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnActionDamagePostHit(__0, __1, __2, __3);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.OnCounterAttackPostHit))]
internal static class RelicEchoCounterPostHitPatch
{
    private static void Postfix(Equipment __instance, Monster __0, float __1)
    {
        if (RelicEchoForwarder.TryGet(__instance, out var relic)) relic.OnCounterAttackPostHit(__0, __1);
    }
}
