using System;
using System.Linq;
using HarmonyLib;

namespace MonsterSanctuaryMod;

internal static class UpgradeInteraction
{
    private static UpgradeMenu? _menu;
    private static MenuList? _itemList;
    private static Equipment? _equipment;

    internal static bool TryOpen(UpgradeMenu menu, MenuList itemList, MenuListItem menuItem)
    {
        var equipment = ResolveEquipment(menuItem);
        if (!RandomizedModeState.Active ||
            equipment == null ||
            !GearRegistry.TryGetRecord(equipment, out var record))
        {
            return false;
        }

        _menu = menu;
        _itemList = itemList;
        _equipment = equipment;
        itemList.SetLocked(true);

        var options = record.Rarity == GearRarity.Legendary
            ? new[]
            {
                $"Reroll modifiers ({UpgradeEconomy.DescribeCost(record, UpgradeOperation.RerollModifiers)})",
                "Cancel"
            }
            : new[]
            {
                $"Increase to {record.Rarity + 1} ({UpgradeEconomy.DescribeCost(record, UpgradeOperation.IncreaseRarity)})",
                $"Reroll modifiers ({UpgradeEconomy.DescribeCost(record, UpgradeOperation.RerollModifiers)})",
                "Cancel"
            };

        MultiChoicePopup.Instance.Open(
            true,
            OnActionSelected,
            "Modify Randomized Gear",
            0,
            options);
        return true;
    }

    internal static string GetHoverText(Equipment equipment)
    {
        if (!GearRegistry.TryGetRecord(equipment, out var record))
        {
            return string.Empty;
        }

        if (record.Rarity == GearRarity.Legendary)
        {
            return "Legendary rarity is maximum. Select the item to reroll all five modifiers while keeping its item level and Legendary effect.\n\n" +
                   "Reroll cost: " + UpgradeEconomy.DescribeCost(record, UpgradeOperation.RerollModifiers);
        }

        return $"Increase rarity to {record.Rarity + 1}. The item keeps every current modifier and roll, then gains one new modifier." +
               (record.Rarity == GearRarity.Epic
                   ? " Reaching Legendary also grants a triggered Legendary power and requires a Legendary Upgrade Token." +
                     (GameModeManager.Instance?.RelicMode == true
                         ? " Because Relics of Chaos is active, it also gains one compatible Relic Echo."
                         : string.Empty)
                   : string.Empty) +
               "\n\nRarity cost: " + UpgradeEconomy.DescribeCost(record, UpgradeOperation.IncreaseRarity) +
               "\nReroll cost: " + UpgradeEconomy.DescribeCost(record, UpgradeOperation.RerollModifiers);
    }

    private static void OnActionSelected(int selection)
    {
        _itemList?.SetLocked(false);
        var equipment = _equipment;
        if (equipment == null || !GearRegistry.TryGetRecord(equipment, out var record))
        {
            ClearSelection();
            return;
        }

        var operation = record.Rarity == GearRarity.Legendary
            ? selection == 0 ? UpgradeOperation.RerollModifiers : (UpgradeOperation?)null
            : selection switch
            {
                0 => UpgradeOperation.IncreaseRarity,
                1 => UpgradeOperation.RerollModifiers,
                _ => null
            };
        if (operation == null)
        {
            ClearSelection();
            return;
        }

        if (!UpgradeEconomy.CanAfford(record, operation.Value, out var reason))
        {
            PopupController.Instance?.ShowMessage("Cannot Modify Gear", reason, null);
            ClearSelection();
            return;
        }

        // Capture the pre-upgrade price. Increasing rarity and rerolling both
        // change fields used to derive the deterministic material selection.
        var materialCost = UpgradeEconomy.GetMaterials(record, operation.Value);
        var goldCost = UpgradeEconomy.GetGoldFee(record, operation.Value);
        var completed = operation == UpgradeOperation.IncreaseRarity
            ? GearRegistry.IncreaseRarity(equipment, out _)
            : GearRegistry.RerollModifiers(equipment);
        if (!completed)
        {
            PopupController.Instance?.ShowMessage("Cannot Modify Gear", "This item cannot be modified.", null);
            ClearSelection();
            return;
        }

        UpgradeEconomy.Consume(materialCost, goldCost);
        RecalculateOwners(equipment);
        PlayUpgradeSound();
        RefreshMenu();

        var result = operation == UpgradeOperation.IncreaseRarity
            ? $"The item is now {record.Rarity} and gained one modifier."
            : $"Rerolled all {record.Affixes.Count} modifiers. The item level, rarity, and Legendary power were preserved.";
        PopupController.Instance?.ShowMessage("Gear Modified", result, null);
        ClearSelection();
    }

    private static Equipment? ResolveEquipment(MenuListItem menuItem) =>
        menuItem.Displayable switch
        {
            Equipment equipment => equipment,
            InventoryItem inventoryItem => inventoryItem.Equipment,
            _ => null
        };

    private static void RecalculateOwners(Equipment equipment)
    {
        var monsters = PlayerController.Instance?.Monsters?.AllMonster;
        if (monsters == null)
        {
            return;
        }

        foreach (var monster in monsters.Where(monster =>
                     monster?.Equipment?.Equipment?.Any(item => item != null && item.ID == equipment.ID) == true))
        {
            monster.CalculateCurrentStats();
        }
    }

    private static void PlayUpgradeSound()
    {
        if (SFXController.Instance != null && SFXController.Instance.SFXBlacksmithing != null)
        {
            SFXController.Instance.PlaySFX(SFXController.Instance.SFXBlacksmithing, 1f, 1f);
        }
    }

    private static void RefreshMenu()
    {
        if (_menu == null)
        {
            return;
        }

        AccessTools.Method(typeof(UpgradeMenu), "UpdateMenuList")?.Invoke(_menu, null);
        _menu.PagedItemList?.ValidateCurrentSelected();
    }

    private static void ClearSelection()
    {
        _menu = null;
        _itemList = null;
        _equipment = null;
    }
}
