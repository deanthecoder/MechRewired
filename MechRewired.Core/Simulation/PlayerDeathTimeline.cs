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

public sealed record PlayerDeathFrame(
    double OrbitRadians,
    double AscentMeters,
    double FadeOpacity,
    bool ShouldRestart);

/// <summary>
/// Provides deterministic timing for the external death camera and automatic mission restart.
/// </summary>
public sealed class PlayerDeathTimeline
{
    private const double OrbitDegreesPerSecond = 10.0;
    private const double AscentMetersPerSecond = 6.0;
    private const double FadeStartSeconds = 3.0;
    private const double FadeDurationSeconds = 2.0;
    private const double RestartSeconds = 5.5;

    private double m_elapsed;

    public PlayerDeathFrame Advance(double seconds)
    {
        if (seconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        m_elapsed += seconds;
        var fadeProgress = Math.Clamp(
            (m_elapsed - FadeStartSeconds) / FadeDurationSeconds,
            0.0,
            1.0);
        var smoothFade = fadeProgress * fadeProgress * (3.0 - 2.0 * fadeProgress);
        return new PlayerDeathFrame(
            m_elapsed * OrbitDegreesPerSecond * Math.PI / 180.0,
            m_elapsed * AscentMetersPerSecond,
            smoothFade,
            m_elapsed >= RestartSeconds);
    }
}
