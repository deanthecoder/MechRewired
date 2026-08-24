// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace MechRewired;

public enum TerrainGroundReliefKind
{
    None,
    Desert,
    Rocky
}

/// <summary>
/// Supplies deterministic, biome-scaled height fields shared by ground, landform bases,
/// collision representation and terrain queries.
/// </summary>
public static class TerrainGroundRelief
{
    public const float DesertMaximumAmplitudeMetres = 0.85f;
    public const float DesertBaseFadeEndMetres = 10.0f;
    public const float RockyMaximumAmplitudeMetres = 1.25f;
    public const float RockyBaseFadeEndMetres = 18.0f;

    public static float MaximumAmplitude(TerrainGroundReliefKind kind) => kind switch
    {
        TerrainGroundReliefKind.Desert => DesertMaximumAmplitudeMetres,
        TerrainGroundReliefKind.Rocky => RockyMaximumAmplitudeMetres,
        _ => 0.0f
    };

    public static float BaseFadeEnd(TerrainGroundReliefKind kind) => kind switch
    {
        TerrainGroundReliefKind.Desert => DesertBaseFadeEndMetres,
        TerrainGroundReliefKind.Rocky => RockyBaseFadeEndMetres,
        _ => 0.0f
    };

    public static float SampleOffset(NumericsVector2 position, TerrainGroundReliefKind kind)
    {
        var (broadScale, mediumScale, offsetX, offsetY) = kind switch
        {
            // Long, quiet swells beneath the desert's material-level dunes.
            TerrainGroundReliefKind.Desert => (0.0028f, 0.0062f, -17.6f, 26.4f),
            // Still broader formations for Jade; avoid reading as repeated surface ripples.
            TerrainGroundReliefKind.Rocky => (0.0018f, 0.0041f, 11.3f, -7.9f),
            _ => (0.0f, 0.0f, 0.0f, 0.0f)
        };
        if (kind == TerrainGroundReliefKind.None)
        {
            return 0.0f;
        }

        var broad = ValueNoise(
            position.X * broadScale + offsetX,
            position.Y * broadScale + offsetY);
        var medium = ValueNoise(
            position.X * mediumScale - offsetY,
            position.Y * mediumScale + offsetX);
        var height = broad * 0.76f + medium * 0.24f;
        return (height - 0.5f) * 2.0f * MaximumAmplitude(kind);
    }

    public static NumericsVector3 ApplyAtLandformBase(
        NumericsVector3 position,
        TerrainGroundReliefKind kind)
    {
        var fadeEnd = BaseFadeEnd(kind);
        if (fadeEnd <= 0.0f)
        {
            return position;
        }

        var baseFade = 1.0f - SmoothStep(0.5f, fadeEnd, position.Y);
        if (baseFade <= 0.0f)
        {
            return position;
        }

        return position with
        {
            Y = position.Y + SampleOffset(
                new NumericsVector2(position.X, position.Z),
                kind) * baseFade
        };
    }

    private static float ValueNoise(float x, float y)
    {
        var cellX = MathF.Floor(x);
        var cellY = MathF.Floor(y);
        var localX = SmoothInterpolation(x - cellX);
        var localY = SmoothInterpolation(y - cellY);
        return Lerp(
            Lerp(Hash(cellX, cellY), Hash(cellX + 1.0f, cellY), localX),
            Lerp(Hash(cellX, cellY + 1.0f), Hash(cellX + 1.0f, cellY + 1.0f), localX),
            localY);
    }

    private static float Hash(float x, float y)
    {
        var value = MathF.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
        return value - MathF.Floor(value);
    }

    private static float SmoothInterpolation(float value) =>
        value * value * (3.0f - 2.0f * value);

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var amount = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return amount * amount * (3.0f - 2.0f * amount);
    }

    private static float Lerp(float first, float second, float amount) =>
        first + (second - first) * amount;
}
