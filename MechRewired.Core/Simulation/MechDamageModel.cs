// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Simulation;

public enum MechDamageSection
{
    Head,
    CenterTorso,
    LeftTorso,
    RightTorso,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg
}

public sealed record MechSectionArmor(
    int FrontArmor,
    int RearArmor,
    int InternalStructure);

public sealed record MechDamageResult(
    MechDamageSection Section,
    int DamageApplied,
    bool RearArmorHit,
    bool SectionDestroyed,
    bool SectionNewlyDestroyed,
    bool MechDestroyed);

/// <summary>
/// Tracks authored armor and internal structure independently of rendering and input.
/// </summary>
public sealed class MechDamageModel
{
    private readonly Dictionary<MechDamageSection, SectionState> m_sections;

    public MechDamageModel(IReadOnlyDictionary<MechDamageSection, MechSectionArmor> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var missing = Enum.GetValues<MechDamageSection>().Where(section => !sections.ContainsKey(section)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Missing mech damage sections: {string.Join(", ", missing)}.", nameof(sections));
        }

        m_sections = sections.ToDictionary(
            entry => entry.Key,
            entry => new SectionState(entry.Value));
    }

    public int MaximumHealth => m_sections.Values.Sum(section => section.MaximumHealth);

    public int Health => m_sections.Values.Sum(section => section.Health);

    public bool IsDestroyed =>
        IsSectionDestroyed(MechDamageSection.Head) ||
        IsSectionDestroyed(MechDamageSection.CenterTorso) ||
        IsSectionDestroyed(MechDamageSection.LeftLeg) && IsSectionDestroyed(MechDamageSection.RightLeg);

    public bool IsSectionDestroyed(MechDamageSection section) =>
        m_sections[section].InternalStructure == 0;

    public MechSectionArmor GetMaximum(MechDamageSection section) => m_sections[section].Maximum;

    public MechSectionArmor GetRemaining(MechDamageSection section)
    {
        var state = m_sections[section];
        return new MechSectionArmor(state.FrontArmor, state.RearArmor, state.InternalStructure);
    }

    public float GetHealthFraction(MechDamageSection section)
    {
        var state = m_sections[section];
        return state.MaximumHealth == 0 ? 0.0f : (float)state.Health / state.MaximumHealth;
    }

    public MechDamageResult ApplyDamage(MechDamageSection section, int damage, bool fromRear = false)
    {
        if (damage <= 0 || IsDestroyed)
        {
            return new MechDamageResult(section, 0, false, IsSectionDestroyed(section), false, IsDestroyed);
        }

        var state = m_sections[section];
        var wasDestroyed = state.InternalStructure == 0;
        if (wasDestroyed)
        {
            return new MechDamageResult(section, 0, false, true, false, IsDestroyed);
        }

        var remainingDamage = damage;
        var rearArmorHit = fromRear && state.Maximum.RearArmor > 0;
        if (rearArmorHit)
        {
            remainingDamage -= Absorb(ref state.RearArmor, remainingDamage);
        }
        else
        {
            remainingDamage -= Absorb(ref state.FrontArmor, remainingDamage);
        }

        if (remainingDamage > 0)
        {
            Absorb(ref state.InternalStructure, remainingDamage);
        }

        var isDestroyed = state.InternalStructure == 0;
        return new MechDamageResult(
            section,
            damage,
            rearArmorHit,
            isDestroyed,
            !wasDestroyed && isDestroyed,
            IsDestroyed);
    }

    private static int Absorb(ref int points, int damage)
    {
        var absorbed = Math.Min(points, damage);
        points -= absorbed;
        return absorbed;
    }

    private sealed class SectionState(MechSectionArmor maximum)
    {
        public MechSectionArmor Maximum { get; } = maximum;
        public int FrontArmor = maximum.FrontArmor;
        public int RearArmor = maximum.RearArmor;
        public int InternalStructure = maximum.InternalStructure;
        public int MaximumHealth => Maximum.FrontArmor + Maximum.RearArmor + Maximum.InternalStructure;
        public int Health => FrontArmor + RearArmor + InternalStructure;
    }
}
