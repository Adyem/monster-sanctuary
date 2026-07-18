using System;
using System.Collections.Generic;

namespace MonsterSanctuaryMod;

internal enum GearRarity
{
    Common,
    Magic,
    Rare,
    Epic,
    Legendary
}

internal enum GearAffix
{
    Attack,
    Magic,
    HybridOffense,
    PhysicalDefense,
    MagicalDefense,
    Health,
    CritDamage,
    CritChance,
    Mana,
    ManaRegeneration
}

internal enum LegendaryEffect
{
    None,
    BarrierBloom,
    PurifyingTouch,
    RallyingLight,
    CleansingWard,
    EmpoweringWard,
    Spellbreaker,
    InspiringAegis
}

internal enum GearSource
{
    Unknown,
    Combat,
    Chest,
    Vendor,
    Migration
}

internal sealed class GearRecord
{
    internal int Id;
    internal int BaseEquipmentId;
    internal int SourceEquipmentId;
    internal int ItemLevel;
    internal GearRarity Rarity;
    internal LegendaryEffect LegendaryEffect;
    internal int RelicEchoEquipmentId;
    internal int UpgradeToId;
    internal int RerollCount;
    internal readonly List<GearAffix> Affixes = new();
    internal readonly List<int> RollBasisPoints = new();
}

internal sealed class PendingGearTransfer
{
    internal int OriginalEquipmentId;
    internal int Frame;
    internal Equipment Generated = null!;
}

internal static class RandomizedModeState
{
    internal const string SaveMarker = "Adyem.RandomizedGearMode";

    internal static bool Active { get; set; }
    internal static bool PendingNewGameSelection { get; set; }
    internal static bool BypassInventoryRandomization { get; set; }
    internal static bool BypassRewardRandomization { get; set; }
    internal static bool VendorPurchaseInProgress { get; set; }
    internal static bool SaveInProgress { get; set; }
    internal static readonly List<string> VendorReveals = new();
    internal static readonly List<PendingGearTransfer> PendingPopupReveals = new();
    internal static readonly List<PendingGearTransfer> PendingInventoryGrants = new();
}
