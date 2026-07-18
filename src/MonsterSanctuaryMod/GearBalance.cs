using System;
using UnityEngine;

namespace MonsterSanctuaryMod;

internal static class GearBalance
{
    internal const int MinimumItemLevel = 1;
    internal const int MaximumItemLevel = 42;

    internal static int GetAffixCount(GearRarity rarity) => rarity switch
    {
        GearRarity.Common => 1,
        GearRarity.Magic => 2,
        GearRarity.Rare => 3,
        GearRarity.Epic => 4,
        GearRarity.Legendary => 5,
        _ => 1
    };

    internal static GearRarity RollRarity(int itemLevel, System.Random random)
    {
        var progress = Mathf.InverseLerp(MinimumItemLevel, MaximumItemLevel, itemLevel);

        // Early areas mostly produce foundational gear. Endgame encounters shift
        // meaningful probability into Rare and Epic while Legendary remains scarce.
        var legendary = Mathf.Lerp(0.001f, 0.02f, progress);
        var epic = Mathf.Lerp(0.009f, 0.10f, progress);
        var rare = Mathf.Lerp(0.05f, 0.25f, progress);
        var magic = Mathf.Lerp(0.24f, 0.38f, progress);
        var roll = random.NextDouble();

        if (roll < legendary) return GearRarity.Legendary;
        if (roll < legendary + epic) return GearRarity.Epic;
        if (roll < legendary + epic + rare) return GearRarity.Rare;
        if (roll < legendary + epic + rare + magic) return GearRarity.Magic;
        return GearRarity.Common;
    }

    internal static float GetMagnitude(GearAffix affix, int itemLevel, int rollBasisPoints, bool weapon)
    {
        var progress = Mathf.InverseLerp(MinimumItemLevel, MaximumItemLevel, itemLevel);
        var roll = Mathf.Clamp(rollBasisPoints / 10000f, 0.85f, 1.15f);
        var value = affix switch
        {
            GearAffix.Attack => Mathf.Lerp(25f, weapon ? 120f : 90f, progress),
            GearAffix.Magic => Mathf.Lerp(25f, weapon ? 120f : 90f, progress),
            GearAffix.HybridOffense => Mathf.Lerp(25f, 120f, progress) * 0.70f,
            GearAffix.PhysicalDefense => Mathf.Lerp(12f, 65f, progress),
            GearAffix.MagicalDefense => Mathf.Lerp(12f, 65f, progress),
            GearAffix.Health => Mathf.Lerp(120f, 650f, progress),
            GearAffix.CritDamage => Mathf.Lerp(0.06f, 0.20f, progress),
            GearAffix.CritChance => Mathf.Lerp(0.025f, 0.09f, progress),
            GearAffix.Mana => Mathf.Lerp(20f, 80f, progress),
            GearAffix.ManaRegeneration => Mathf.Lerp(8f, 32f, progress),
            _ => 0f
        };

        return value * roll;
    }

    internal static float GetTypedDefenseReduction(float defenseRating)
    {
        // Typed defense is intentionally capped so it complements the game's
        // existing Defense stat instead of replacing it.
        return Mathf.Clamp(defenseRating / 550f, 0f, 0.20f);
    }

    internal static int ClampItemLevel(int level) =>
        Math.Max(MinimumItemLevel, Math.Min(MaximumItemLevel, level));
}
