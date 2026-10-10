// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Simulation;

/// <summary>Selects sparse missile smoke and lights in actual launch order.</summary>
/// <remarks>One sequence per firing mech prevents pool reuse or small salvos from restarting the cadence.</remarks>
public sealed class MissileVisualCadence
{
    public const int SmokeStride = 3;
    public const int LightStride = 8;
    private int m_phase;

    public (bool Smoke, bool Light) Next()
    {
        var selection = (m_phase % SmokeStride == 0, m_phase % LightStride == 0);
        m_phase = (m_phase + 1) % (SmokeStride * LightStride);
        return selection;
    }
}
