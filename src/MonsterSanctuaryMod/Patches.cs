using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSanctuaryMod;

[HarmonyPatch(typeof(GameController), "Start")]
internal static class GameStartupValidationPatch
{
    private static void Postfix()
    {
        LegendaryTokenRegistry.EnsureRegistered();
        GearGenerator.ValidateTemplates();
        UpgradeEconomy.ValidateRuntimeAssets();
        RelicEchoCatalog.Validate();
    }
}

internal static class ModeMenuIntegration
{
    internal static MenuListItem? ModeMenuItem { get; private set; }
    internal static tk2dTextMesh? ModeLabel { get; private set; }

    internal static void CreateModeItem(NewGameMenu menu)
    {
        if (ModeMenuItem != null || menu.RelicItem == null || menu.Menu == null)
        {
            return;
        }

        var root = menu.RelicItem.transform.parent;
        var relicLabel = root.Find("RelicLabel");
        var timerLabel = root.Find("TimerLabel");
        if (relicLabel == null || timerLabel == null || menu.TimerItem == null)
        {
            Plugin.ModLog.LogError("Could not create a separate Randomized Gear row because the new-game menu layout was not recognized.");
            return;
        }

        const float rowSpacing = 14f;
        var clone = Object.Instantiate(menu.RelicItem.gameObject, root);
        clone.name = "RandomizedGearModeItem";
        clone.transform.SetSiblingIndex(menu.RelicItem.transform.GetSiblingIndex() + 1);
        clone.transform.localPosition = menu.RelicItem.transform.localPosition + Vector3.down * rowSpacing;
        clone.SetActive(true);

        ModeMenuItem = clone.GetComponent<MenuListItem>();
        if (ModeMenuItem == null)
        {
            Object.Destroy(clone);
            return;
        }

        var labelClone = Object.Instantiate(relicLabel.gameObject, root);
        labelClone.name = "RandomizedGearModeLabel";
        labelClone.transform.SetSiblingIndex(clone.transform.GetSiblingIndex() + 1);
        labelClone.transform.localPosition = relicLabel.localPosition + Vector3.down * rowSpacing;
        labelClone.SetActive(true);
        var localizer = labelClone.GetComponent<Localizer>();
        if (localizer != null)
        {
            localizer.enabled = false;
        }
        ModeLabel = labelClone.GetComponent<tk2dTextMesh>();
        if (ModeLabel != null)
        {
            ModeLabel.text = "Randomized Gear";
        }

        menu.TimerItem.transform.localPosition += Vector3.down * rowSpacing;
        timerLabel.localPosition += Vector3.down * rowSpacing;

        // The stock panel is sized for the original settings rows. Add enough
        // height for the new row and move its center down by half that amount
        // so the header remains where the game placed it.
        var panel = menu.Menu.RootElement?.GetComponent<tk2dSlicedSprite>();
        if (panel != null)
        {
            const float panelExtension = 42f;
            panel.dimensions = new Vector2(panel.dimensions.x, panel.dimensions.y + panelExtension);
            panel.transform.localPosition += Vector3.down * (panelExtension * 0.5f);
        }
        ModeMenuItem.IsSelectable = true;
        var listIndex = Math.Max(0, menu.Menu.GetListContainingMenuItem(menu.RelicItem));
        menu.Menu.AddMenuItem(ModeMenuItem, listIndex);

        // AddMenuItem appends to the selected list. Move the clone beside the
        // Relics button so keyboard/controller traversal follows the visual
        // order instead of jumping from Relics to Timer and then back up.
        var selectableList = menu.Menu.Lists[listIndex];
        selectableList.Remove(ModeMenuItem);
        var relicIndex = selectableList.IndexOf(menu.RelicItem);
        selectableList.Insert(Math.Max(0, relicIndex + 1), ModeMenuItem);
        UpdateModeButton(menu);
        menu.Menu.UpdateItemPositions(false);
        Plugin.ModLog.LogInfo("Added the Randomized Gear toggle to the new-game mode menu.");
    }

    internal static void UpdateModeButton(NewGameMenu menu)
    {
        if (ModeMenuItem?.Text != null)
        {
            // The game formats active settings with its own localized green
            // markup. Reuse that private helper so this row matches every
            // built-in mode button instead of displaying plain white text.
            var getOnOffText = AccessTools.Method(typeof(NewGameMenu), "GetOnOffText");
            ModeMenuItem.Text.text = getOnOffText?.Invoke(
                menu,
                new object[] { RandomizedModeState.PendingNewGameSelection }) as string ??
                (RandomizedModeState.PendingNewGameSelection ? "On" : "Off");
        }
    }

    internal static void ShowDescription(NewGameMenu menu)
    {
        menu.DescriptionText.text =
            "Replaces ordinary equipment rewards and merchant gear with unidentified, " +
            "level-scaled randomized items. Higher rarities gain more modifiers; " +
            "Legendary items also gain a powerful effect. When Relics of Chaos is enabled, " +
            "Legendary gear inherits one compatible Relic Echo. PvP is disabled for this save.";
    }
}

[HarmonyPatch(typeof(NewGameMenu), nameof(NewGameMenu.Start))]
internal static class NewGameMenuStartPatch
{
    private static void Postfix(NewGameMenu __instance) =>
        ModeMenuIntegration.CreateModeItem(__instance);
}

[HarmonyPatch(typeof(NewGameMenu), nameof(NewGameMenu.Open))]
internal static class NewGameMenuOpenPatch
{
    private static void Prefix(bool resetSetting)
    {
        if (resetSetting)
        {
            RandomizedModeState.PendingNewGameSelection = false;
        }
    }

    private static void Postfix(NewGameMenu __instance)
    {
        ModeMenuIntegration.CreateModeItem(__instance);
        ModeMenuIntegration.UpdateModeButton(__instance);
    }
}

[HarmonyPatch(typeof(NewGameMenu), "UpdateButtonText")]
internal static class NewGameMenuUpdateButtonPatch
{
    private static void Postfix(NewGameMenu __instance) =>
        ModeMenuIntegration.UpdateModeButton(__instance);
}

[HarmonyPatch(typeof(NewGameMenu), "OnMenuItemSelected")]
internal static class NewGameMenuSelectionPatch
{
    private static bool Prefix(NewGameMenu __instance, MenuListItem menuItem)
    {
        if (menuItem != ModeMenuIntegration.ModeMenuItem)
        {
            return true;
        }

        RandomizedModeState.PendingNewGameSelection =
            !RandomizedModeState.PendingNewGameSelection;
        ModeMenuIntegration.UpdateModeButton(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(NewGameMenu), "OnMenuItemHovered")]
internal static class NewGameMenuHoverPatch
{
    private static void Prefix(NewGameMenu __instance, MenuListItem menuItem)
    {
        if (menuItem == ModeMenuIntegration.ModeMenuItem)
        {
            ModeMenuIntegration.ShowDescription(__instance);
        }
    }
}

[HarmonyPatch(typeof(NewGameMenu), nameof(NewGameMenu.SetupSettings))]
internal static class NewGameMenuSetupPatch
{
    private static void Postfix()
    {
        if (RandomizedModeState.PendingNewGameSelection)
        {
            GearRegistry.BeginNewModeSession();
            Plugin.ModLog.LogInfo("Randomized Gear mode enabled for the new game.");
        }
        else
        {
            RandomizedModeState.Active = false;
        }
    }
}

[HarmonyPatch(typeof(GameModeManager), nameof(GameModeManager.SaveGame))]
internal static class GameModeSavePatch
{
    private static void Postfix(SaveGameData saveData)
    {
        saveData.ProgressionBools ??= new List<string>();
        saveData.ProgressionBools.RemoveAll(value => value == RandomizedModeState.SaveMarker);
        if (RandomizedModeState.Active)
        {
            saveData.ProgressionBools.Add(RandomizedModeState.SaveMarker);
        }
    }
}

[HarmonyPatch(typeof(GameModeManager), nameof(GameModeManager.LoadGame))]
internal static class GameModeLoadPatch
{
    private static void Postfix(SaveGameData saveData)
    {
        RandomizedModeState.Active =
            saveData.ProgressionBools?.Contains(RandomizedModeState.SaveMarker) == true;
        Plugin.Debug($"Randomized Gear mode loaded: {RandomizedModeState.Active}.");
    }
}

[HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.LoadSaveGame), new[] { typeof(string) })]
internal static class SaveFileReadPatch
{
    private static void Prefix(string fileName)
    {
        if (RandomizedModeState.SaveInProgress)
        {
            return;
        }

        var slot = GearRegistry.TryParseSlot(fileName);
        if (slot >= 0)
        {
            GearRegistry.LoadSlot(slot);
        }
    }
}

[HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.LoadSaveGame), new[]
{
    typeof(SaveGameData), typeof(int), typeof(bool)
})]
internal static class SaveGameLoadedPatch
{
    private static void Postfix(SaveGameData saveData, int saveGameSlot)
    {
        if (!RandomizedModeState.Active &&
            saveData.ProgressionBools?.Contains(RandomizedModeState.SaveMarker) == true)
        {
            GearRegistry.LoadSlot(saveGameSlot);
            RandomizedModeState.Active = true;
        }

        if (RandomizedModeState.Active)
        {
            GearGenerator.ConvertExistingPlayerEquipment();
        }
    }
}

[HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.SaveGame), new[] { typeof(bool) })]
internal static class SaveFileWritePatch
{
    private static void Prefix(SaveGameManager __instance)
    {
        RandomizedModeState.SaveInProgress = true;
        if (RandomizedModeState.Active)
        {
            GearRegistry.SaveSlot(__instance.SaveGameSlot);
        }
    }

    private static void Postfix(SaveGameManager __instance)
    {
        if (RandomizedModeState.Active)
        {
            GearRegistry.SaveSlot(__instance.SaveGameSlot);
        }
        RandomizedModeState.SaveInProgress = false;
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        RandomizedModeState.SaveInProgress = false;
        return __exception;
    }
}

[HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.CopySavegame))]
internal static class CopySavePatch
{
    private static void Postfix(int slotCopyFrom, int slotCopyTo) =>
        GearRegistry.CopySlot(slotCopyFrom, slotCopyTo);
}

[HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.DeleteSavegame))]
internal static class DeleteSavePatch
{
    private static void Postfix(int slotToBeDeleted) =>
        GearRegistry.DeleteSlot(slotToBeDeleted);
}

[HarmonyPatch]
internal static class WorldDataReferencePatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.GetDeclaredMethods(typeof(WorldData))
            .Single(method =>
                method.Name == nameof(WorldData.GetReferenceable) &&
                !method.IsGenericMethod &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(int));

    private static void Postfix(int id, ref Referenceable __result)
    {
        if (__result == null)
        {
            var generated = GearRegistry.GetOrCreateEquipment(id);
            if (generated != null)
            {
                __result = generated;
                return;
            }

            var token = LegendaryTokenRegistry.Resolve(id);
            if (token != null)
            {
                __result = token;
            }
        }
    }
}

[HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.AddItem))]
internal static class InventoryAddItemPatch
{
    private static bool Prefix(
        InventoryManager __instance,
        BaseItem item,
        int quantity,
        int variation)
    {
        if (!RandomizedModeState.Active ||
            RandomizedModeState.BypassInventoryRandomization ||
            item is not Equipment equipment ||
            equipment.IsRelic ||
            GearRegistry.IsGenerated(equipment) ||
            quantity <= 0)
        {
            return true;
        }

        var source = RandomizedModeState.VendorPurchaseInProgress
            ? GearSource.Vendor
            : GearSource.Unknown;
        var level = source == GearSource.Vendor
            ? GearGenerator.GetPartyItemLevel()
            : GearGenerator.GetWorldItemLevel();

        try
        {
            RandomizedModeState.BypassInventoryRandomization = true;
            for (var i = 0; i < quantity; i++)
            {
                Equipment prepared = null!;
                var popupAlreadyDisplayed =
                    source == GearSource.Unknown &&
                    PendingGearTransfers.TryTakeInventoryGrant(equipment, out prepared);
                var generated = popupAlreadyDisplayed
                    ? prepared
                    : GearGenerator.Generate(equipment, level, source);
                __instance.AddItem(generated, 1, variation);
                if (RandomizedModeState.VendorPurchaseInProgress &&
                    GearRegistry.TryGetRecord(generated, out var record))
                {
                    RandomizedModeState.VendorReveals.Add(
                        GearRegistry.GetDisplayName(generated, record));
                }
                else if (source == GearSource.Unknown &&
                         !popupAlreadyDisplayed &&
                         GearRegistry.IsGenerated(generated))
                {
                    PendingGearTransfers.QueuePopupReveal(equipment, generated);
                }
            }
        }
        finally
        {
            RandomizedModeState.BypassInventoryRandomization = false;
        }

        return false;
    }
}

[HarmonyPatch(typeof(PopupController), nameof(PopupController.ShowReceiveItem))]
internal static class ReceiveItemPopupPatch
{
    private static void Prefix(ref BaseItem item)
    {
        if (!RandomizedModeState.Active ||
            item is not Equipment equipment ||
            equipment.IsRelic ||
            GearRegistry.IsGenerated(equipment))
        {
            return;
        }

        if (PendingGearTransfers.TryTakePopupReveal(equipment, out var generated))
        {
            item = generated;
            return;
        }

        generated = GearGenerator.Generate(
            equipment,
            GearGenerator.GetWorldItemLevel(),
            GearSource.Unknown);
        item = generated;
        if (GearRegistry.IsGenerated(generated))
        {
            PendingGearTransfers.QueueInventoryGrant(equipment, generated);
        }
    }
}

internal static class PendingGearTransfers
{
    internal static void QueuePopupReveal(Equipment original, Equipment generated) =>
        Queue(RandomizedModeState.PendingPopupReveals, original, generated);

    internal static void QueueInventoryGrant(Equipment original, Equipment generated) =>
        Queue(RandomizedModeState.PendingInventoryGrants, original, generated);

    internal static bool TryTakePopupReveal(Equipment original, out Equipment generated) =>
        TryTake(RandomizedModeState.PendingPopupReveals, original, out generated);

    internal static bool TryTakeInventoryGrant(Equipment original, out Equipment generated) =>
        TryTake(RandomizedModeState.PendingInventoryGrants, original, out generated);

    private static void Queue(
        List<PendingGearTransfer> transfers,
        Equipment original,
        Equipment generated)
    {
        Prune(transfers);
        transfers.Add(new PendingGearTransfer
        {
            OriginalEquipmentId = original.ID,
            Generated = generated,
            Frame = Time.frameCount
        });
    }

    private static bool TryTake(
        List<PendingGearTransfer> transfers,
        Equipment original,
        out Equipment generated)
    {
        Prune(transfers);
        var index = transfers.FindIndex(transfer =>
            transfer.OriginalEquipmentId == original.ID &&
            transfer.Frame == Time.frameCount);
        if (index >= 0)
        {
            generated = transfers[index].Generated;
            transfers.RemoveAt(index);
            return generated != null;
        }

        generated = null!;
        return false;
    }

    private static void Prune(List<PendingGearTransfer> transfers) =>
        transfers.RemoveAll(transfer => transfer.Frame != Time.frameCount);
}

[HarmonyPatch(typeof(CombatController), nameof(CombatController.AddRewardItem))]
internal static class CombatRewardPatch
{
    private static bool Prefix(
        CombatController __instance,
        List<InventoryItem> items,
        BaseItem item,
        int quantity,
        int variation)
    {
        if (!RandomizedModeState.Active ||
            RandomizedModeState.BypassRewardRandomization ||
            item is not Equipment equipment ||
            equipment.IsRelic ||
            GearRegistry.IsGenerated(equipment) ||
            quantity <= 0)
        {
            return true;
        }

        try
        {
            RandomizedModeState.BypassRewardRandomization = true;
            for (var i = 0; i < quantity; i++)
            {
                var generated = GearGenerator.Generate(
                    equipment,
                    GearGenerator.GetCombatItemLevel(__instance),
                    GearSource.Combat);
                __instance.AddRewardItem(items, generated, 1, variation);
            }
        }
        finally
        {
            RandomizedModeState.BypassRewardRandomization = false;
        }

        return false;
    }
}

[HarmonyPatch(typeof(Chest), "GrantItemReward")]
internal static class ChestRewardPatch
{
    private static void Prefix(ref BaseItem itemComponent)
    {
        if (RandomizedModeState.Active &&
            itemComponent is Equipment equipment &&
            !equipment.IsRelic &&
            !GearRegistry.IsGenerated(equipment))
        {
            itemComponent = GearGenerator.Generate(
                equipment,
                GearGenerator.GetWorldItemLevel(),
                GearSource.Chest);
        }
    }
}

[HarmonyPatch(typeof(TradeMenu), "OnShowDisplayable")]
internal static class TradeMenuDisplayPatch
{
    private static void Postfix(
        IMenuListDisplayable displayable,
        MenuListItem menuItem,
        bool ___sell)
    {
        if (RandomizedModeState.Active && !___sell && displayable is Equipment equipment && !equipment.IsRelic)
        {
            menuItem.SetText(equipment.Type == Equipment.EquipmentType.Weapon
                ? "Unidentified Weapon"
                : "Unidentified Accessory");
        }
    }
}

[HarmonyPatch(typeof(TradeMenu), "OnItemHovered")]
internal static class TradeMenuHoverPatch
{
    private static bool Prefix(TradeMenu __instance, MenuListItem menuItem, bool ___sell)
    {
        if (!RandomizedModeState.Active || ___sell || menuItem.Displayable is not Equipment equipment || equipment.IsRelic)
        {
            return true;
        }

        var category = equipment.Type == Equipment.EquipmentType.Weapon ? "weapon" : "accessory";
        __instance.ItemTooltip.OpenText(
            $"The item's rarity and modifiers are revealed after purchase. Its item level will match your current party level.\n\nCategory: {category}",
            false,
            "Unidentified Gear");
        return false;
    }
}

[HarmonyPatch(typeof(TradePopup), "OnItemSelected")]
internal static class TradePopupPurchasePatch
{
    private static void Prefix(
        TradePopup __instance,
        MenuListItem menuItem,
        bool ___Sell,
        BaseItem ___Item)
    {
        RandomizedModeState.VendorPurchaseInProgress =
            RandomizedModeState.Active &&
            !___Sell &&
            menuItem == __instance.ConfirmItem &&
            ___Item is Equipment equipment &&
            !equipment.IsRelic;

        if (RandomizedModeState.VendorPurchaseInProgress)
        {
            RandomizedModeState.VendorReveals.Clear();
        }
    }

    private static void Postfix()
    {
        if (!RandomizedModeState.VendorPurchaseInProgress)
        {
            return;
        }

        RandomizedModeState.VendorPurchaseInProgress = false;
        if (RandomizedModeState.VendorReveals.Count == 0)
        {
            return;
        }

        var message = string.Join("\n", RandomizedModeState.VendorReveals.Take(8));
        if (RandomizedModeState.VendorReveals.Count > 8)
        {
            message += $"\n...and {RandomizedModeState.VendorReveals.Count - 8} more";
        }
        PopupController.Instance?.ShowMessage("Gear Identified", message, null);
        RandomizedModeState.VendorReveals.Clear();
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        RandomizedModeState.VendorPurchaseInProgress = false;
        return __exception;
    }
}

[HarmonyPatch(typeof(TradePopup), nameof(TradePopup.Open))]
internal static class TradePopupOpenPatch
{
    private static void Postfix(
        BaseItem item,
        bool sell,
        tk2dTextMesh ___ItemName,
        tk2dSprite ___Icon)
    {
        var equipment = item as Equipment;
        var concealed =
            RandomizedModeState.Active &&
            !sell &&
            equipment != null &&
            !equipment.IsRelic;

        if (concealed && equipment != null)
        {
            ___ItemName.text = equipment.Type == Equipment.EquipmentType.Weapon
                ? "Unidentified Weapon"
                : "Unidentified Accessory";
        }

        ___Icon.gameObject.SetActive(!concealed);
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.GetName))]
internal static class EquipmentNamePatch
{
    private static void Postfix(Equipment __instance, ref string __result)
    {
        if (GearRegistry.TryGetRecord(__instance, out var record))
        {
            __result = GearRegistry.GetDisplayName(__instance, record);
        }
    }
}

[HarmonyPatch(typeof(Equipment), nameof(Equipment.GetTooltip))]
internal static class EquipmentTooltipPatch
{
    private static void Postfix(Equipment __instance, ref string __result)
    {
        if (GearRegistry.TryGetRecord(__instance, out var record))
        {
            __result += "\n" + GearRegistry.GetAdditionalTooltip(__instance, record);
        }
    }
}

[HarmonyPatch(typeof(UpgradeMenu), "UpdateMenuItemAvailability")]
internal static class UpgradeAvailabilityPatch
{
    private static bool Prefix(Equipment equipment, MenuListItem menuItem)
    {
        if (RandomizedModeState.Active && GearRegistry.IsGenerated(equipment))
        {
            GearRegistry.PrepareUpgradeOptions(equipment);
            menuItem.SetDisabled(false);
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(UpgradeMenu), "OnItemSelected")]
internal static class UpgradeSelectionPatch
{
    private static bool Prefix(UpgradeMenu __instance, MenuListItem menuItem, MenuList ___ItemList) =>
        !UpgradeInteraction.TryOpen(__instance, ___ItemList, menuItem);
}

[HarmonyPatch(typeof(UpgradeMenu), "OnItemHovered")]
internal static class UpgradeHoverPatch
{
    private static void Postfix(MenuListItem menuItem, ItemTooltip ___UpgradeTooltip)
    {
        var equipment = menuItem.Displayable switch
        {
            Equipment direct => direct,
            InventoryItem inventory => inventory.Equipment,
            _ => null
        };
        if (!RandomizedModeState.Active || equipment == null || !GearRegistry.IsGenerated(equipment))
        {
            return;
        }

        ___UpgradeTooltip.OpenText(
            UpgradeInteraction.GetHoverText(equipment),
            false,
            "Rarity & Reroll");
    }
}

[HarmonyPatch(typeof(CombatController), nameof(CombatController.StartCombat))]
internal static class LegendaryTokenCombatStartPatch
{
    private static void Prefix() => LegendaryTokenDrops.BeginCombat();
}

[HarmonyPatch(typeof(PopupController), nameof(PopupController.ShowRewards))]
internal static class LegendaryTokenRewardPatch
{
    private static void Prefix(List<InventoryItem> rareItems) =>
        LegendaryTokenDrops.TryAddReward(rareItems);
}

[HarmonyPatch(typeof(LevelBadge), nameof(LevelBadge.GetName))]
internal static class LegendaryTokenNamePatch
{
    private static void Postfix(LevelBadge __instance, ref string __result)
    {
        if (LegendaryTokenRegistry.IsToken(__instance))
        {
            __result = "Legendary Upgrade Token";
        }
    }
}

[HarmonyPatch(typeof(LevelBadge), nameof(LevelBadge.GetTooltip))]
internal static class LegendaryTokenTooltipPatch
{
    private static void Postfix(LevelBadge __instance, ref string __result)
    {
        if (LegendaryTokenRegistry.IsToken(__instance))
        {
            __result = "A rare encounter reward consumed by the blacksmith when upgrading Epic randomized gear to Legendary rarity.";
        }
    }
}

[HarmonyPatch(typeof(LevelBadge), nameof(LevelBadge.CanBeUsedOnMonster))]
internal static class LegendaryTokenUseCheckPatch
{
    private static bool Prefix(LevelBadge __instance, ref bool __result)
    {
        if (!LegendaryTokenRegistry.IsToken(__instance))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(LevelBadge), nameof(LevelBadge.Use))]
internal static class LegendaryTokenUsePatch
{
    private static bool Prefix(LevelBadge __instance) =>
        !LegendaryTokenRegistry.IsToken(__instance);
}

[HarmonyPatch(typeof(SkillManager), nameof(SkillManager.OnBeingHit))]
internal static class TypedDefensePatch
{
    private static void Postfix(ActionDamage action, ref float damage, Monster ___monster)
    {
        if (!RandomizedModeState.Active || action == null || ___monster == null)
        {
            return;
        }

        var rating = GearRegistry.GetTypedDefense(___monster, action.Type);
        if (rating > 0f)
        {
            damage *= 1f - GearBalance.GetTypedDefenseReduction(rating);
        }
    }
}

internal static class PvpBlocker
{
    internal static bool Block()
    {
        if (!RandomizedModeState.Active)
        {
            return true;
        }

        if (PopupController.Instance != null && !PopupController.Instance.IsOpen)
        {
            PopupController.Instance.ShowMessage(
                "PvP Disabled",
                "Online battles are unavailable while Randomized Gear mode is active because generated equipment is not network-compatible.",
                null);
        }
        return false;
    }
}

[HarmonyPatch(typeof(OnlineArenaMenu), nameof(OnlineArenaMenu.Open))]
internal static class OnlineArenaOpenPatch
{
    private static bool Prefix() => PvpBlocker.Block();
}

[HarmonyPatch(typeof(OnlineHostMenu), nameof(OnlineHostMenu.Open))]
internal static class OnlineHostOpenPatch
{
    private static bool Prefix() => PvpBlocker.Block();
}

[HarmonyPatch(typeof(OnlineJoinMenu), nameof(OnlineJoinMenu.Open))]
internal static class OnlineJoinOpenPatch
{
    private static bool Prefix() => PvpBlocker.Block();
}

[HarmonyPatch(typeof(OnlineArenaMenu), nameof(OnlineArenaMenu.MatchStart))]
internal static class OnlineArenaMatchPatch
{
    private static bool Prefix() => PvpBlocker.Block();
}

[HarmonyPatch(typeof(OnlineHostMenu), nameof(OnlineHostMenu.MatchStart))]
internal static class OnlineHostMatchPatch
{
    private static bool Prefix() => PvpBlocker.Block();
}

[HarmonyPatch(typeof(OnlineJoinMenu), nameof(OnlineJoinMenu.MatchStart))]
internal static class OnlineJoinMatchPatch
{
    private static bool Prefix() => PvpBlocker.Block();
}

[HarmonyPatch(typeof(MonsterEncounter), nameof(MonsterEncounter.StartCombat))]
internal static class OnlineEncounterPatch
{
    private static bool Prefix(MonsterEncounter __instance) =>
        !__instance.IsOnlineBattle || PvpBlocker.Block();
}
