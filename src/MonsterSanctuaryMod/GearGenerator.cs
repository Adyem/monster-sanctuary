using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MonsterSanctuaryMod;

internal static class GearGenerator
{
    private static System.Random? _random;
    private static int _randomSeed;

    internal static Equipment Generate(Equipment original, int itemLevel, GearSource source)
    {
        if (GearRegistry.IsGenerated(original) || original.IsRelic)
        {
            return original;
        }

        var template = FindSafeTemplate(original);
        if (template == null)
        {
            Plugin.ModLog.LogWarning($"No safe randomized gear template found for {original.name}; keeping the original item.");
            return original;
        }

        var random = GetRandom();
        var level = GearBalance.ClampItemLevel(itemLevel);
        var rarity = GearBalance.RollRarity(level, random);
        var affixes = RollAffixes(template.Type == Equipment.EquipmentType.Weapon, rarity, random);
        var rolls = affixes.Select(_ => random.Next(8500, 11501)).ToList();
        var legendary = rarity == GearRarity.Legendary
            ? (LegendaryEffect)random.Next(1, Enum.GetValues(typeof(LegendaryEffect)).Length)
            : LegendaryEffect.None;
        var relicEcho = rarity == GearRarity.Legendary
            ? RelicEchoCatalog.Roll(template.Type == Equipment.EquipmentType.Weapon)
            : 0;

        var record = GearRegistry.AddRecord(
            template.ID,
            original.GetBaseEquipment()?.ID ?? original.ID,
            level,
            rarity,
            legendary,
            relicEcho,
            affixes,
            rolls);
        var generated = GearRegistry.GetOrCreateEquipment(record.Id);
        if (generated == null)
        {
            Plugin.ModLog.LogError($"Failed to create randomized equipment record {record.Id}; keeping the original item.");
            return original;
        }

        Plugin.Debug($"Generated {GearRegistry.GetDisplayName(generated, record)} from {source}.");
        return generated;
    }

    internal static int GetPartyItemLevel()
    {
        var monsters = PlayerController.Instance?.Monsters;
        return GearBalance.ClampItemLevel(monsters?.GetHighestLevel() ?? 1);
    }

    internal static int GetCombatItemLevel(CombatController controller)
    {
        var encounterLevel = controller.CurrentEncounter?.Level ?? 0;
        if (encounterLevel > 0)
        {
            return GearBalance.ClampItemLevel(encounterLevel);
        }

        var enemyLevel = controller.Enemies?
            .Where(monster => monster != null)
            .Select(monster => monster.Level)
            .DefaultIfEmpty(0)
            .Max() ?? 0;
        return enemyLevel > 0
            ? GearBalance.ClampItemLevel(enemyLevel)
            : GetPartyItemLevel();
    }

    internal static int GetWorldItemLevel()
    {
        var minimapLevel = PlayerController.Instance?.Minimap?.CurrentEntry?.EncounterLevel ?? 0;
        if (minimapLevel > 0)
        {
            return GearBalance.ClampItemLevel(minimapLevel);
        }

        var encounterLevels = UnityEngine.Object.FindObjectsOfType<MonsterEncounter>()
            .Where(encounter => encounter != null && !encounter.IsOnlineBattle)
            .Select(encounter => encounter.Level)
            .Where(level => level > 0)
            .OrderBy(level => level)
            .ToList();
        return encounterLevels.Count > 0
            ? GearBalance.ClampItemLevel(encounterLevels[encounterLevels.Count / 2])
            : GetPartyItemLevel();
    }

    internal static void ValidateTemplates()
    {
        var world = GameController.Instance?.WorldData;
        if (world == null)
        {
            Plugin.ModLog.LogWarning("Randomized gear template validation could not run because WorldData is unavailable.");
            return;
        }

        var templates = GetSafeTemplates(world);
        var weapons = templates.Count(item => item.Type == Equipment.EquipmentType.Weapon);
        var accessories = templates.Count - weapons;
        if (weapons == 0 || accessories == 0)
        {
            Plugin.ModLog.LogWarning(
                $"Randomized gear needs plain Equipment templates, but found {weapons} weapon and {accessories} accessory templates.");
            return;
        }

        Plugin.ModLog.LogInfo(
            $"Randomized gear template validation passed ({weapons} weapons, {accessories} accessories).");
    }

    internal static void ConvertExistingPlayerEquipment()
    {
        if (!RandomizedModeState.Active)
        {
            return;
        }

        var player = PlayerController.Instance;
        if (player == null)
        {
            return;
        }

        ConvertInventory(player.Inventory);

        var monsters = player.Monsters?.AllMonster;
        if (monsters == null)
        {
            return;
        }

        foreach (var monster in monsters)
        {
            ConvertEquippedItems(monster);
        }
    }

    private static void ConvertInventory(InventoryManager inventory)
    {
        if (inventory == null)
        {
            return;
        }

        ConvertInventoryList(inventory, inventory.Weapons);
        ConvertInventoryList(inventory, inventory.Accessories);
    }

    private static void ConvertInventoryList(InventoryManager inventory, List<InventoryItem> list)
    {
        foreach (var entry in list.ToList())
        {
            var equipment = entry.Equipment;
            if (equipment == null || equipment.IsRelic || GearRegistry.IsGenerated(equipment))
            {
                continue;
            }

            var quantity = Math.Max(1, entry.Quantity);
            list.Remove(entry);
            try
            {
                RandomizedModeState.BypassInventoryRandomization = true;
                for (var i = 0; i < quantity; i++)
                {
                    var generated = Generate(equipment, GetPartyItemLevel(), GearSource.Migration);
                    inventory.AddItem(generated, 1, 0);
                }
            }
            finally
            {
                RandomizedModeState.BypassInventoryRandomization = false;
            }
        }
    }

    private static void ConvertEquippedItems(Monster monster)
    {
        var manager = monster.Equipment;
        if (manager?.Equipment == null)
        {
            return;
        }

        for (var i = 0; i < manager.Equipment.Length; i++)
        {
            var current = manager.Equipment[i];
            if (current == null || current.IsRelic || GearRegistry.IsGenerated(current))
            {
                continue;
            }

            var generated = Generate(current, monster.Level, GearSource.Migration);
            manager.Equip(generated, (EquipmentManager.EquipmentSlot)i, false);
        }

        monster.CalculateCurrentStats();
    }

    private static Equipment? FindSafeTemplate(Equipment original)
    {
        var world = GameController.Instance?.WorldData;
        if (world == null)
        {
            return null;
        }

        var isWeapon = original.Type == Equipment.EquipmentType.Weapon;
        var templates = GetSafeTemplates(world);

        return templates.FirstOrDefault(item => item.Type == original.Type)
            ?? templates.FirstOrDefault(item =>
                (item.Type == Equipment.EquipmentType.Weapon) == isWeapon);
    }

    private static List<Equipment> GetSafeTemplates(WorldData world) =>
        world.Referenceables
            .OfType<Equipment>()
            .Where(item =>
                item != null &&
                item.ID > 0 &&
                item.GetType() == typeof(Equipment) &&
                !item.IsRelic &&
                item.UpgradesFrom == null)
            .ToList();

    internal static void AddRarityAffix(GearRecord record, bool weapon)
    {
        var random = GetRandom();
        var available = Enum.GetValues(typeof(GearAffix))
            .Cast<GearAffix>()
            // A weapon keeps its original offensive profile when rarity rises;
            // accessories may never acquire an offensive profile.
            .Where(affix => !IsOffensiveAffix(affix))
            .Where(affix => !record.Affixes.Contains(affix))
            .ToList();
        if (available.Count == 0)
        {
            return;
        }

        var affix = available[random.Next(available.Count)];
        record.Affixes.Add(affix);
        record.RollBasisPoints.Add(random.Next(8500, 11501));
    }

    internal static void RerollAffixes(GearRecord record, bool weapon)
    {
        var random = GetRandom();
        record.Affixes.Clear();
        record.RollBasisPoints.Clear();
        record.Affixes.AddRange(RollAffixes(weapon, record.Rarity, random));
        record.RollBasisPoints.AddRange(record.Affixes.Select(_ => random.Next(8500, 11501)));
        record.RerollCount++;
    }

    internal static LegendaryEffect RollLegendaryEffect()
    {
        var random = GetRandom();
        return (LegendaryEffect)random.Next(1, Enum.GetValues(typeof(LegendaryEffect)).Length);
    }

    internal static void NormalizeAffixes(GearRecord record, bool weapon)
    {
        var offensive = record.Affixes
            .Select((affix, index) => new { affix, index })
            .Where(entry => IsOffensiveAffix(entry.affix))
            .ToList();
        if (!weapon)
        {
            for (var i = offensive.Count - 1; i >= 0; i--)
            {
                record.Affixes.RemoveAt(offensive[i].index);
                record.RollBasisPoints.RemoveAt(offensive[i].index);
            }
        }
        else if (offensive.Count == 0)
        {
            var profile = (GearAffix)GetRandom().Next(
                (int)GearAffix.Attack,
                (int)GearAffix.HybridOffense + 1);
            record.Affixes.Insert(0, profile);
            record.RollBasisPoints.Insert(0, GetRandom().Next(8500, 11501));
        }
        else if (offensive.Count > 1)
        {
            var quality = Mathf.RoundToInt((float)offensive
                .Select(entry => record.RollBasisPoints[entry.index])
                .Average());
            for (var i = offensive.Count - 1; i >= 0; i--)
            {
                record.Affixes.RemoveAt(offensive[i].index);
                record.RollBasisPoints.RemoveAt(offensive[i].index);
            }
            record.Affixes.Insert(0, GearAffix.HybridOffense);
            record.RollBasisPoints.Insert(0, quality);
        }

        while (record.Affixes.Count < GearBalance.GetAffixCount(record.Rarity))
        {
            AddRarityAffix(record, weapon);
        }
    }

    private static List<GearAffix> RollAffixes(bool weapon, GearRarity rarity, System.Random random)
    {
        var available = Enum.GetValues(typeof(GearAffix))
            .Cast<GearAffix>()
            .Where(affix => weapon || !IsOffensiveAffix(affix))
            .ToList();
        var result = new List<GearAffix>();
        var count = GearBalance.GetAffixCount(rarity);

        if (weapon)
        {
            var offensive = (GearAffix)random.Next(
                (int)GearAffix.Attack,
                (int)GearAffix.HybridOffense + 1);
            result.Add(offensive);
            available.RemoveAll(IsOffensiveAffix);
        }

        while (result.Count < count && available.Count > 0)
        {
            var index = random.Next(available.Count);
            result.Add(available[index]);
            available.RemoveAt(index);
        }

        return result;
    }

    private static bool IsOffensiveAffix(GearAffix affix) =>
        affix == GearAffix.Attack ||
        affix == GearAffix.Magic ||
        affix == GearAffix.HybridOffense;

    private static System.Random GetRandom()
    {
        var gameSeed = GameModeManager.Instance?.Seed ?? 0;
        if (_random == null || _randomSeed != gameSeed)
        {
            _randomSeed = gameSeed;
            _random = new System.Random(unchecked(gameSeed * 486187739 + Environment.TickCount));
        }
        return _random;
    }
}
