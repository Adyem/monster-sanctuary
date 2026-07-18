using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSanctuaryMod;

internal enum UpgradeOperation
{
    IncreaseRarity,
    RerollModifiers
}

internal static class LegendaryTokenRegistry
{
    internal const int TokenId = 1_590_000_001;
    private static LevelBadge? _token;

    internal static LevelBadge? Token
    {
        get
        {
            EnsureRegistered();
            return _token;
        }
    }

    internal static bool IsToken(BaseItem? item) => item != null && item.ID == TokenId;

    internal static void EnsureRegistered()
    {
        var world = GameController.Instance?.WorldData;
        if (world == null)
        {
            return;
        }

        var cacheField = AccessTools.Field(typeof(WorldData), "ReferenceableCache");
        var cache = cacheField?.GetValue(world) as Dictionary<int, Referenceable>;
        if (cache != null && cache.TryGetValue(TokenId, out var existing) && existing is LevelBadge badge)
        {
            _token = badge;
            return;
        }

        if (_token == null)
        {
            var template = Prefabs.Instance?.LevelBadge40?.GetComponent<LevelBadge>()
                ?? world.Referenceables.OfType<LevelBadge>().FirstOrDefault();
            if (template == null)
            {
                Plugin.ModLog.LogError("Could not create the Legendary Upgrade Token because no LevelBadge template exists.");
                return;
            }

            var clone = Object.Instantiate(template.gameObject);
            clone.name = "RandomizedGear_LegendaryUpgradeToken";
            Object.DontDestroyOnLoad(clone);
            clone.SetActive(false);
            _token = clone.GetComponent<LevelBadge>();
            if (_token == null)
            {
                Object.Destroy(clone);
                return;
            }

            _token.ID = TokenId;
            _token.Name = "Legendary Upgrade Token";
            _token.MaxLevel = 0;
        }

        if (cache != null)
        {
            cache[TokenId] = _token;
        }
        if (!world.Referenceables.Contains(_token))
        {
            world.Referenceables.Add(_token);
        }
    }

    internal static Referenceable? Resolve(int id)
    {
        if (id != TokenId)
        {
            return null;
        }
        EnsureRegistered();
        return _token;
    }

    internal static void Shutdown()
    {
        if (_token != null)
        {
            Object.Destroy(_token.gameObject);
        }
        _token = null;
    }
}

internal static class UpgradeEconomy
{
    internal static void ValidateRuntimeAssets()
    {
        LegendaryTokenRegistry.EnsureRegistered();
        var materials = GameController.Instance?.WorldData?.Referenceables
            .OfType<CraftMaterial>()
            .Where(material => material != null && material.Level > 0)
            .ToList() ?? new List<CraftMaterial>();
        var tiers = materials.Select(material => material.Level).Distinct().OrderBy(level => level).ToList();
        if (LegendaryTokenRegistry.Token == null || materials.Count == 0)
        {
            Plugin.ModLog.LogError(
                $"Randomized gear upgrade assets are incomplete: token={LegendaryTokenRegistry.Token != null}, crafting materials={materials.Count}.");
            return;
        }

        Plugin.ModLog.LogInfo(
            $"Randomized gear upgrade validation passed ({materials.Count} crafting materials across tiers {string.Join(",", tiers)}; Legendary token registered)." );
    }

    internal static List<ItemQuantity> GetDisplayedMaterials(GearRecord record) =>
        record.Rarity == GearRarity.Legendary
            ? GetMaterials(record, UpgradeOperation.RerollModifiers)
            : GetMaterials(record, UpgradeOperation.IncreaseRarity);

    internal static List<ItemQuantity> GetMaterials(GearRecord record, UpgradeOperation operation)
    {
        var result = new List<ItemQuantity>();
        var materials = GetLevelAppropriateMaterials(record, operation);
        var rarityRank = (int)record.Rarity;
        var typeCount = rarityRank >= (int)GearRarity.Epic ? 2 : 1;
        var quantity = operation == UpgradeOperation.IncreaseRarity
            ? 1 + ((rarityRank + 1) / 2)
            : 1 + (rarityRank / 2);

        foreach (var material in materials.Take(typeCount))
        {
            result.Add(new ItemQuantity
            {
                Item = material.gameObject,
                Quantity = quantity
            });
        }

        if (operation == UpgradeOperation.IncreaseRarity && record.Rarity == GearRarity.Epic)
        {
            var token = LegendaryTokenRegistry.Token;
            if (token != null)
            {
                result.Add(new ItemQuantity { Item = token.gameObject, Quantity = 1 });
            }
        }

        return result;
    }

    internal static int GetGoldFee(GearRecord record, UpgradeOperation operation) =>
        operation == UpgradeOperation.RerollModifiers
            ? 100 + record.ItemLevel * 15 + (int)record.Rarity * 75
            : 0;

    internal static bool CanAfford(
        GearRecord record,
        UpgradeOperation operation,
        out string reason)
    {
        var player = PlayerController.Instance;
        if (player == null)
        {
            reason = "The player inventory is unavailable.";
            return false;
        }

        var gold = GetGoldFee(record, operation);
        if (player.Gold < gold)
        {
            reason = $"Requires {gold} gold; you have {player.Gold}.";
            return false;
        }

        foreach (var cost in GetMaterials(record, operation))
        {
            var item = cost.Item?.GetComponent<BaseItem>();
            if (item == null)
            {
                continue;
            }

            var owned = player.Inventory.GetItemQuantity(item, 0);
            if (owned < cost.Quantity)
            {
                reason = $"Requires {cost.Quantity}x {item.GetName()}; you have {owned}.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    internal static void Consume(List<ItemQuantity> materials, int gold)
    {
        var player = PlayerController.Instance;
        if (player == null)
        {
            return;
        }

        player.Gold -= gold;
        foreach (var cost in materials)
        {
            var item = cost.Item?.GetComponent<BaseItem>();
            if (item != null)
            {
                player.Inventory.RemoveItem(item, cost.Quantity, 0);
            }
        }
    }

    internal static string DescribeCost(GearRecord record, UpgradeOperation operation)
    {
        var parts = GetMaterials(record, operation)
            .Select(cost =>
            {
                var item = cost.Item?.GetComponent<BaseItem>();
                return item == null ? string.Empty : $"{cost.Quantity}x {item.GetName()}";
            })
            .Where(text => text.Length > 0)
            .ToList();
        var gold = GetGoldFee(record, operation);
        if (gold > 0)
        {
            parts.Insert(0, $"{gold} gold");
        }
        return parts.Count == 0 ? "No materials" : string.Join(", ", parts);
    }

    private static List<CraftMaterial> GetLevelAppropriateMaterials(
        GearRecord record,
        UpgradeOperation operation)
    {
        var world = GameController.Instance?.WorldData;
        var all = world?.Referenceables
            .OfType<CraftMaterial>()
            .Where(material => material != null && material.ID > 0 && material.Level > 0)
            .GroupBy(material => material.ID)
            .Select(group => group.First())
            .ToList() ?? new List<CraftMaterial>();
        if (all.Count == 0)
        {
            return all;
        }

        var maximumTier = all.Max(material => material.Level);
        var desiredTier = Mathf.Clamp(1 + (record.ItemLevel - 1) / 8, 1, maximumTier);
        var nearestDistance = all.Min(material => Math.Abs(material.Level - desiredTier));
        var candidates = all
            .Where(material => Math.Abs(material.Level - desiredTier) == nearestDistance)
            .ToList();

        var seed = unchecked(
            record.SourceEquipmentId * 397 ^
            record.ItemLevel * 7919 ^
            record.RerollCount * 104729 ^
            (int)record.Rarity * 1543 ^
            (int)operation * 65537);
        var random = new System.Random(seed);
        return candidates.OrderBy(_ => random.Next()).ToList();
    }
}

internal static class LegendaryTokenDrops
{
    private static int _combatSequence;
    private static int _rewardedSequence = -1;

    internal static void BeginCombat() => _combatSequence++;

    internal static float GetDropChance(int encounterLevel, bool champion)
    {
        var progress = Mathf.InverseLerp(1, GearBalance.MaximumItemLevel, encounterLevel);
        return Mathf.Clamp(Mathf.Lerp(0.005f, 0.06f, progress) + (champion ? 0.025f : 0f), 0f, 0.10f);
    }

    internal static void TryAddReward(List<InventoryItem> rareItems)
    {
        if (!RandomizedModeState.Active || _rewardedSequence == _combatSequence)
        {
            return;
        }

        var combat = CombatController.Instance;
        var encounter = combat?.CurrentEncounter;
        if (combat == null || encounter == null || encounter.IsOnlineBattle || combat.RetreatedCombat)
        {
            return;
        }

        var chance = GetDropChance(GearGenerator.GetCombatItemLevel(combat), encounter.IsChampion);
        if (UnityEngine.Random.value > chance)
        {
            return;
        }

        var token = LegendaryTokenRegistry.Token;
        if (token == null)
        {
            return;
        }

        _rewardedSequence = _combatSequence;
        combat.AddRewardItem(rareItems, token, 1, 0);
        Plugin.Debug($"Legendary Upgrade Token dropped at {chance:P1} chance.");
    }
}
