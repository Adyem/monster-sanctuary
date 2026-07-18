using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSanctuaryMod;

internal static class GearRegistry
{
    private const int FirstGeneratedId = 1_600_000_000;
    private const string FileVersion = "RGM3";

    private static readonly Dictionary<int, GearRecord> Records = new();
    private static readonly Dictionary<int, Equipment> RuntimeEquipment = new();
    private static int _nextId = FirstGeneratedId;
    private static int _loadedSlot = -1;

    internal static void Initialize()
    {
        Directory.CreateDirectory(GetRegistryDirectory());
    }

    internal static void Shutdown()
    {
        ClearRuntimeEquipment();
        Records.Clear();
    }

    internal static bool IsGenerated(Equipment? equipment) =>
        equipment != null && equipment.ID >= FirstGeneratedId && Records.ContainsKey(equipment.ID);

    internal static bool TryGetRecord(Equipment? equipment, out GearRecord record)
    {
        if (equipment != null && Records.TryGetValue(equipment.ID, out var found))
        {
            record = found;
            return true;
        }

        record = null!;
        return false;
    }

    internal static GearRecord AddRecord(
        int baseEquipmentId,
        int sourceEquipmentId,
        int itemLevel,
        GearRarity rarity,
        LegendaryEffect legendaryEffect,
        int relicEchoEquipmentId,
        IReadOnlyList<GearAffix> affixes,
        IReadOnlyList<int> rolls)
    {
        var record = new GearRecord
        {
            Id = _nextId++,
            BaseEquipmentId = baseEquipmentId,
            SourceEquipmentId = sourceEquipmentId > 0 ? sourceEquipmentId : baseEquipmentId,
            ItemLevel = GearBalance.ClampItemLevel(itemLevel),
            Rarity = rarity,
            LegendaryEffect = legendaryEffect,
            RelicEchoEquipmentId = relicEchoEquipmentId
        };

        record.Affixes.AddRange(affixes);
        record.RollBasisPoints.AddRange(rolls);
        Records.Add(record.Id, record);
        return record;
    }

    internal static Equipment? GetOrCreateEquipment(int id)
    {
        if (RuntimeEquipment.TryGetValue(id, out var existing) && existing != null)
        {
            return existing;
        }

        if (!Records.TryGetValue(id, out var record))
        {
            return null;
        }

        var controller = GameController.Instance;
        var world = controller?.WorldData;
        if (world == null)
        {
            return null;
        }

        var cache = GetWorldCache(world);
        if (!cache.TryGetValue(record.BaseEquipmentId, out var referenceable) ||
            referenceable is not Equipment template)
        {
            Plugin.ModLog.LogError($"Cannot rebuild randomized gear {record.Id}: base equipment {record.BaseEquipmentId} is unavailable.");
            return null;
        }

        var cloneObject = Object.Instantiate(template.gameObject);
        cloneObject.name = $"RandomizedGear_{record.Id}";
        Object.DontDestroyOnLoad(cloneObject);
        cloneObject.SetActive(false);

        var equipment = cloneObject.GetComponent<Equipment>();
        if (equipment == null)
        {
            Object.Destroy(cloneObject);
            return null;
        }

        equipment.ID = record.Id;
        NormalizeRecord(record, template.Type == Equipment.EquipmentType.Weapon);
        ApplyRecord(equipment, template, record);

        RuntimeEquipment[record.Id] = equipment;
        cache[record.Id] = equipment;
        if (!world.Referenceables.Contains(equipment))
        {
            world.Referenceables.Add(equipment);
        }

        return equipment;
    }

    internal static void PrepareUpgradeOptions(Equipment equipment)
    {
        if (!TryGetRecord(equipment, out var record))
        {
            return;
        }

        record.UpgradeToId = 0;
        equipment.UpgradesTo = equipment.gameObject;
        equipment.UpgradeMaterials = UpgradeEconomy.GetDisplayedMaterials(record);
    }

    internal static bool IncreaseRarity(Equipment equipment, out GearRarity newRarity)
    {
        newRarity = GearRarity.Common;
        if (!TryGetRecord(equipment, out var record) || record.Rarity >= GearRarity.Legendary)
        {
            return false;
        }

        record.Rarity++;
        GearGenerator.AddRarityAffix(record, equipment.Type == Equipment.EquipmentType.Weapon);
        if (record.Rarity == GearRarity.Legendary)
        {
            record.LegendaryEffect = GearGenerator.RollLegendaryEffect();
            record.RelicEchoEquipmentId = RelicEchoCatalog.Roll(
                equipment.Type == Equipment.EquipmentType.Weapon);
        }
        NormalizeRecord(record, equipment.Type == Equipment.EquipmentType.Weapon);
        RefreshEquipment(equipment, record);
        newRarity = record.Rarity;
        return true;
    }

    internal static bool RerollModifiers(Equipment equipment)
    {
        if (!TryGetRecord(equipment, out var record))
        {
            return false;
        }

        GearGenerator.RerollAffixes(record, equipment.Type == Equipment.EquipmentType.Weapon);
        RefreshEquipment(equipment, record);
        return true;
    }

    internal static IEnumerable<LegendaryEffect> GetLegendaryEffects(Monster monster)
    {
        var equipped = monster?.Equipment?.Equipment;
        if (equipped == null)
        {
            yield break;
        }

        foreach (var equipment in equipped)
        {
            if (TryGetRecord(equipment, out var record) &&
                record.Rarity == GearRarity.Legendary &&
                record.LegendaryEffect != LegendaryEffect.None)
            {
                yield return record.LegendaryEffect;
            }
        }
    }

    internal static float GetTypedDefense(Monster monster, EDamageType damageType)
    {
        var affix = damageType == EDamageType.Physical
            ? GearAffix.PhysicalDefense
            : GearAffix.MagicalDefense;
        var total = 0f;

        var manager = monster?.Equipment;
        if (manager?.Equipment == null)
        {
            return total;
        }

        foreach (var equipment in manager.Equipment)
        {
            if (!TryGetRecord(equipment, out var record))
            {
                continue;
            }

            var weapon = equipment.Type == Equipment.EquipmentType.Weapon;
            for (var i = 0; i < record.Affixes.Count; i++)
            {
                if (record.Affixes[i] == affix)
                {
                    total += GearBalance.GetMagnitude(affix, record.ItemLevel, record.RollBasisPoints[i], weapon);
                }
            }
        }

        return total;
    }

    internal static float GetAffixMagnitude(Equipment? equipment, GearAffix soughtAffix)
    {
        if (!TryGetRecord(equipment, out var record) || equipment == null)
        {
            return 0f;
        }

        var total = 0f;
        var weapon = equipment.Type == Equipment.EquipmentType.Weapon;
        for (var i = 0; i < record.Affixes.Count; i++)
        {
            if (record.Affixes[i] == soughtAffix)
            {
                total += GearBalance.GetMagnitude(
                    soughtAffix,
                    record.ItemLevel,
                    record.RollBasisPoints[i],
                    weapon);
            }
        }
        return total;
    }

    internal static string GetDisplayName(Equipment equipment, GearRecord record)
    {
        var source = GetSourceEquipment(record);
        var baseName = source?.GetOriginalName() ?? equipment.GetOriginalName();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = equipment.Type == Equipment.EquipmentType.Weapon ? "Weapon" : "Accessory";
        }

        var displayName = $"[{record.Rarity}] {baseName}  iLvl {record.ItemLevel}";
        return record.Rarity switch
        {
            GearRarity.Magic => GameDefines.FormatTextRGB(
                displayName,
                new Color(0.20f, 1.00f, 0.20f, 1.00f)),
            GearRarity.Rare => GameDefines.FormatTextRGB(
                displayName,
                new Color(0.25f, 0.55f, 1.00f, 1.00f)),
            GearRarity.Epic => GameDefines.FormatTextRGB(
                displayName,
                new Color(0.75f, 0.30f, 1.00f, 1.00f)),
            GearRarity.Legendary => GameDefines.FormatTextRGB(
                displayName,
                new Color(1.00f, 0.55f, 0.10f, 1.00f)),
            _ => displayName
        };
    }

    internal static string GetAdditionalTooltip(Equipment equipment, GearRecord record)
    {
        var weapon = equipment.Type == Equipment.EquipmentType.Weapon;
        var lines = new List<string>
        {
            $"Item Level: {record.ItemLevel}",
            $"Rarity: {record.Rarity}"
        };

        for (var i = 0; i < record.Affixes.Count; i++)
        {
            var affix = record.Affixes[i];
            if (affix != GearAffix.PhysicalDefense &&
                affix != GearAffix.MagicalDefense &&
                affix != GearAffix.HealthPercent &&
                affix != GearAffix.DamageOverTime &&
                affix != GearAffix.BuffEffect &&
                affix != GearAffix.ShieldEffect)
            {
                continue;
            }

            var magnitude = GearBalance.GetMagnitude(
                affix,
                record.ItemLevel,
                record.RollBasisPoints[i],
                weapon);
            if (affix == GearAffix.PhysicalDefense || affix == GearAffix.MagicalDefense)
            {
                lines.Add($"+{Mathf.RoundToInt(magnitude)} {GetAffixName(affix)}");
            }
            else
            {
                lines.Add($"+{Mathf.RoundToInt(magnitude * 100f)}% {GetAffixName(affix)}");
            }
        }

        if (record.LegendaryEffect != LegendaryEffect.None)
        {
            lines.Add($"Legendary: {GetLegendaryDescription(record.LegendaryEffect)}");
        }
        if (RelicEchoCatalog.TryGet(record, out var relic))
        {
            lines.Add($"Relic Echo — {relic.GetOriginalName()}: {RelicEchoCatalog.GetDescription(relic)}");
        }

        return string.Join("\n", lines);
    }

    internal static void BeginNewModeSession()
    {
        ClearRuntimeEquipment();
        Records.Clear();
        _nextId = FirstGeneratedId;
        _loadedSlot = -1;
        RandomizedModeState.PendingPopupReveals.Clear();
        RandomizedModeState.PendingInventoryGrants.Clear();
        RandomizedModeState.Active = true;
    }

    internal static void LoadSlot(int slot)
    {
        if (slot < 0 || (_loadedSlot == slot && Records.Count > 0))
        {
            return;
        }

        ClearRuntimeEquipment();
        Records.Clear();
        _nextId = FirstGeneratedId;
        _loadedSlot = slot;
        RandomizedModeState.PendingPopupReveals.Clear();
        RandomizedModeState.PendingInventoryGrants.Clear();

        var path = GetSlotPath(slot);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0)
            {
                return;
            }

            var header = lines[0].Split('|');
            if (header.Length < 3 ||
                (header[0] != FileVersion && header[0] != "RGM2" && header[0] != "RGM1"))
            {
                Plugin.ModLog.LogWarning($"Ignoring unsupported randomized gear registry: {path}");
                return;
            }

            RandomizedModeState.Active = header[1] == "1";
            _nextId = Math.Max(FirstGeneratedId, ParseInt(header[2], FirstGeneratedId));

            for (var i = 1; i < lines.Length; i++)
            {
                var record = ParseRecord(lines[i], header[0]);
                if (record != null)
                {
                    Records[record.Id] = record;
                    _nextId = Math.Max(_nextId, record.Id + 1);
                }
            }

            Plugin.Debug($"Loaded {Records.Count} randomized gear records for save slot {slot}.");
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogError($"Failed to load randomized gear registry for slot {slot}: {ex}");
        }
    }

    internal static void SaveSlot(int slot)
    {
        if (slot < 0 || !RandomizedModeState.Active)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(GetRegistryDirectory());
            var lines = new List<string>
            {
                $"{FileVersion}|1|{_nextId.ToString(CultureInfo.InvariantCulture)}"
            };
            lines.AddRange(Records.Values.OrderBy(record => record.Id).Select(SerializeRecord));

            var path = GetSlotPath(slot);
            var temporaryPath = path + ".tmp";
            File.WriteAllLines(temporaryPath, lines);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporaryPath, path);
            _loadedSlot = slot;
            Plugin.Debug($"Saved {Records.Count} randomized gear records for save slot {slot}.");
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogError($"Failed to save randomized gear registry for slot {slot}: {ex}");
        }
    }

    internal static void CopySlot(int sourceSlot, int destinationSlot)
    {
        var source = GetSlotPath(sourceSlot);
        var destination = GetSlotPath(destinationSlot);
        if (File.Exists(source))
        {
            File.Copy(source, destination, true);
        }
    }

    internal static void DeleteSlot(int slot)
    {
        var path = GetSlotPath(slot);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    internal static int TryParseSlot(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var match = Regex.Match(name, @"^Savegame[^0-9]*(\d+)", RegexOptions.IgnoreCase);
        return match.Success &&
               int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileSlot) &&
               fileSlot > 0
            ? fileSlot - 1
            : -1;
    }

    private static void ApplyRecord(Equipment equipment, Equipment template, GearRecord record)
    {
        equipment.Attack = 0;
        equipment.Magic = 0;
        equipment.Defense = 0;
        equipment.Health = 0;
        equipment.Mana = 0;
        equipment.ManaRegeneration = 0;
        equipment.CritChance = 0f;
        equipment.CritDamage = 0f;
        equipment.DamageBonus = 0f;
        equipment.DamageReduction = 0f;
        equipment.DodgeChance = 0f;
        equipment.FirstHitDamageIncrease = 0f;
        equipment.HealBonus = 0f;
        equipment.ShieldBonus = 0f;
        equipment.LifestealPercent = 0f;
        equipment.NonCritDamage = 0f;
        equipment.Unique = false;
        equipment.IsRelic = false;
        equipment.FullDefenseOffHand = false;
        equipment.IsInstrument = false;
        equipment.OnlyFamiliars = false;
        equipment.OnlyUnshifted = false;
        equipment.MonsterTypeRestriction = EMonsterType.None;
        equipment.UpgradesTo = null;

        var source = GetSourceEquipment(record);
        if (source != null)
        {
            equipment.Icon = source.Icon;
        }

        var weapon = equipment.Type == Equipment.EquipmentType.Weapon;
        for (var i = 0; i < record.Affixes.Count; i++)
        {
            var affix = record.Affixes[i];
            var amount = GearBalance.GetMagnitude(
                affix,
                record.ItemLevel,
                record.RollBasisPoints[i],
                weapon);
            switch (affix)
            {
                case GearAffix.Attack: equipment.Attack += Mathf.RoundToInt(amount); break;
                case GearAffix.Magic: equipment.Magic += Mathf.RoundToInt(amount); break;
                case GearAffix.HybridOffense:
                    equipment.Attack += Mathf.RoundToInt(amount);
                    equipment.Magic += Mathf.RoundToInt(amount);
                    break;
                case GearAffix.Health: equipment.Health += Mathf.RoundToInt(amount); break;
                case GearAffix.CritDamage: equipment.CritDamage += amount; break;
                case GearAffix.CritChance: equipment.CritChance += amount; break;
                case GearAffix.Mana: equipment.Mana += Mathf.RoundToInt(amount); break;
                case GearAffix.ManaRegeneration: equipment.ManaRegeneration += Mathf.RoundToInt(amount); break;
                case GearAffix.DamageReduction: equipment.DamageReduction += amount; break;
                case GearAffix.ShieldEffect: equipment.ShieldBonus += amount; break;
            }
        }

        RelicEchoCatalog.Apply(equipment, record);

        equipment.Price = GetSellValue(record);
        equipment.CouponPrice = equipment.Price;
        equipment.UpgradesTo = equipment.gameObject;
        equipment.UpgradeMaterials = UpgradeEconomy.GetDisplayedMaterials(record);
    }

    private static int GetSellValue(GearRecord record)
    {
        var rarityMultiplier = record.Rarity switch
        {
            GearRarity.Common => 1f,
            GearRarity.Magic => 1.25f,
            GearRarity.Rare => 1.7f,
            GearRarity.Epic => 2.4f,
            GearRarity.Legendary => 4f,
            _ => 1f
        };
        return Mathf.RoundToInt((100f + record.ItemLevel * 45f) * rarityMultiplier);
    }

    private static void RefreshEquipment(Equipment equipment, GearRecord record)
    {
        var world = GameController.Instance?.WorldData;
        if (world == null ||
            !GetWorldCache(world).TryGetValue(record.BaseEquipmentId, out var referenceable) ||
            referenceable is not Equipment template)
        {
            return;
        }

        ApplyRecord(equipment, template, record);
    }

    private static void NormalizeRecord(GearRecord record, bool weapon)
    {
        if (record.SourceEquipmentId <= 0)
        {
            record.SourceEquipmentId = record.BaseEquipmentId;
        }

        record.Rarity = (GearRarity)Mathf.Clamp(
            (int)record.Rarity,
            (int)GearRarity.Common,
            (int)GearRarity.Legendary);
        while (record.RollBasisPoints.Count < record.Affixes.Count)
        {
            record.RollBasisPoints.Add(10000);
        }
        if (record.RollBasisPoints.Count > record.Affixes.Count)
        {
            record.RollBasisPoints.RemoveRange(
                record.Affixes.Count,
                record.RollBasisPoints.Count - record.Affixes.Count);
        }

        GearGenerator.NormalizeAffixes(record, weapon);
        if (record.Rarity == GearRarity.Legendary)
        {
            if (record.LegendaryEffect <= LegendaryEffect.None ||
                !Enum.IsDefined(typeof(LegendaryEffect), record.LegendaryEffect))
            {
                record.LegendaryEffect = GearGenerator.RollLegendaryEffect();
            }
            if (record.RelicEchoEquipmentId <= 0)
            {
                record.RelicEchoEquipmentId = RelicEchoCatalog.Roll(weapon);
            }
        }
        else
        {
            record.LegendaryEffect = LegendaryEffect.None;
            record.RelicEchoEquipmentId = 0;
        }
    }

    private static Equipment? GetSourceEquipment(GearRecord record)
    {
        var world = GameController.Instance?.WorldData;
        if (world == null)
        {
            return null;
        }

        return GetWorldCache(world).TryGetValue(record.SourceEquipmentId, out var source)
            ? source as Equipment
            : null;
    }

    private static string GetAffixName(GearAffix affix) => affix switch
    {
        GearAffix.HybridOffense => "Attack & Magic (70%)",
        GearAffix.PhysicalDefense => "Physical Defense",
        GearAffix.MagicalDefense => "Magical Defense",
        GearAffix.HealthPercent => "Maximum Health",
        GearAffix.DamageReduction => "Damage Reduction",
        GearAffix.DamageOverTime => "Poison, Burn & Congeal Damage",
        GearAffix.BuffEffect => "Buff Effect",
        GearAffix.ShieldEffect => "Shield Effect",
        _ => affix.ToString()
    };

    internal static string GetLegendaryDescription(LegendaryEffect effect) => effect switch
    {
        LegendaryEffect.BarrierBloom => "Barrier Bloom — healing an ally also shields them for 25% of the heal",
        LegendaryEffect.PurifyingTouch => "Purifying Touch — the first heal on each target removes one debuff",
        LegendaryEffect.RallyingLight => "Rallying Light — the first heal on each target grants a random buff",
        LegendaryEffect.CleansingWard => "Cleansing Ward — the first shield on each target removes one debuff",
        LegendaryEffect.EmpoweringWard => "Empowering Ward — the first shield on each target grants a random buff",
        LegendaryEffect.Spellbreaker => "Spellbreaker — the first damaging hit on each target removes one buff",
        LegendaryEffect.InspiringAegis => "Inspiring Aegis — receiving a buff grants a shield equal to 8% of maximum health",
        LegendaryEffect.HexingEdge => "Hexing Edge — the first damaging hit on each target applies one random debuff",
        LegendaryEffect.VampiricPulse => "Vampiric Pulse — the first damaging hit on each target heals for 10% of damage dealt",
        LegendaryEffect.CriticalMomentum => "Critical Momentum — the first critical result of an action grants a random buff",
        LegendaryEffect.ManaBattery => "Mana Battery — the first completed action each turn restores 8% of maximum mana",
        LegendaryEffect.MendingWard => "Mending Ward — the first shield on each target also heals for 15% of the shield",
        LegendaryEffect.ArcaneShelter => "Arcane Shelter — the first heal on each target restores 5% of their maximum mana",
        LegendaryEffect.RetaliatoryWard => "Retaliatory Ward — the first hit taken from an action grants shield equal to 25% of its damage",
        LegendaryEffect.LastBastion => "Last Bastion — once per combat, dropping to 30% health or less grants 20% maximum-health shield",
        LegendaryEffect.PhoenixOath => "Phoenix Oath — once per combat, survive fatal damage with 10% health",
        LegendaryEffect.OpeningGambit => "Opening Gambit — enter combat with two random buffs",
        _ => string.Empty
    };

    private static string SerializeRecord(GearRecord record)
    {
        var affixes = string.Join(",", record.Affixes.Select(value => ((int)value).ToString(CultureInfo.InvariantCulture)));
        var rolls = string.Join(",", record.RollBasisPoints.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        return string.Join("|", new[]
        {
            record.Id.ToString(CultureInfo.InvariantCulture),
            record.BaseEquipmentId.ToString(CultureInfo.InvariantCulture),
            record.SourceEquipmentId.ToString(CultureInfo.InvariantCulture),
            record.ItemLevel.ToString(CultureInfo.InvariantCulture),
            ((int)record.Rarity).ToString(CultureInfo.InvariantCulture),
            ((int)record.LegendaryEffect).ToString(CultureInfo.InvariantCulture),
            record.RelicEchoEquipmentId.ToString(CultureInfo.InvariantCulture),
            record.UpgradeToId.ToString(CultureInfo.InvariantCulture),
            record.RerollCount.ToString(CultureInfo.InvariantCulture),
            affixes,
            rolls
        });
    }

    private static GearRecord? ParseRecord(string line, string version)
    {
        var parts = line.Split('|');
        if ((version == "RGM1" && parts.Length != 8) ||
            (version == "RGM2" && parts.Length != 10) ||
            (version == FileVersion && parts.Length != 11))
        {
            return null;
        }

        var modern = version != "RGM1";
        var echoOffset = version == FileVersion ? 1 : 0;

        var record = new GearRecord
        {
            Id = ParseInt(parts[0], 0),
            BaseEquipmentId = ParseInt(parts[1], 0),
            SourceEquipmentId = modern ? ParseInt(parts[2], 0) : ParseInt(parts[1], 0),
            ItemLevel = GearBalance.ClampItemLevel(ParseInt(parts[modern ? 3 : 2], 1)),
            Rarity = (GearRarity)ParseInt(parts[modern ? 4 : 3], 0),
            LegendaryEffect = (LegendaryEffect)ParseInt(parts[modern ? 5 : 4], 0),
            RelicEchoEquipmentId = version == FileVersion ? ParseInt(parts[6], 0) : 0,
            UpgradeToId = 0,
            RerollCount = modern ? ParseInt(parts[7 + echoOffset], 0) : 0
        };
        if (record.Id < FirstGeneratedId || record.BaseEquipmentId <= 0)
        {
            return null;
        }

        var affixIndex = modern ? 8 + echoOffset : 6;
        var rollIndex = modern ? 9 + echoOffset : 7;
        foreach (var value in parts[affixIndex].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parsed = ParseInt(value, 0);
            if (version == "RGM1" && parsed >= 2)
            {
                parsed++;
            }
            record.Affixes.Add((GearAffix)parsed);
        }
        foreach (var value in parts[rollIndex].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            record.RollBasisPoints.Add(ParseInt(value, 10000));
        }

        return record.Affixes.Count == record.RollBasisPoints.Count ? record : null;
    }

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static string GetRegistryDirectory() =>
        Path.Combine(Paths.ConfigPath, "RandomizedGearMode");

    private static string GetSlotPath(int slot) =>
        Path.Combine(GetRegistryDirectory(), $"slot-{slot}.gear");

    private static void ClearRuntimeEquipment()
    {
        var world = GameController.Instance?.WorldData;
        foreach (var pair in RuntimeEquipment)
        {
            if (world != null)
            {
                GetWorldCache(world).Remove(pair.Key);
                world.Referenceables.Remove(pair.Value);
            }
            if (pair.Value != null)
            {
                Object.Destroy(pair.Value.gameObject);
            }
        }
        RuntimeEquipment.Clear();
    }

    private static Dictionary<int, Referenceable> GetWorldCache(WorldData world) =>
        (Dictionary<int, Referenceable>)AccessTools.Field(typeof(WorldData), "ReferenceableCache").GetValue(world);
}
