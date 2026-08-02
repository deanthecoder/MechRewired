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

/// <summary>
/// Describes the movement characteristics needed by the deterministic drive simulation.
/// </summary>
public sealed record MechDriveProfile(
    double MaximumForwardSpeedKph,
    double ReverseSpeedFactor = 0.5,
    double AccelerationKphPerSecond = 18.0,
    double BrakingKphPerSecond = 30.0,
    double StationaryTurnRateDegreesPerSecond = 45.0,
    double FullSpeedTurnRateDegreesPerSecond = 18.0);
