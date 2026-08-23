// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Diagnostics;
using Godot;
using MechRewired.Resources;

namespace MechRewired;

/// <summary>Composes the original 31st Century Combat title-screen layers and handles clan selection.</summary>
/// <remarks>
/// All visible artwork comes from the player's locally installed game files or the supplied original
/// screen reference. This control supplies only placement and invisible selection regions; it
/// intentionally draws no substitute text, frames, flames, or insignia.
/// </remarks>
public sealed partial class ClanSelectionScreen : Control
{
    private const int OriginalWidth = 640;
    private const int OriginalHeight = 480;
    private const double TitleFramesPerSecond = 11.25;
    private const string JadeFalconInsigniaPath = "CEL/L2JADEFN.XEL";
    private const string WolfInsigniaPath = "CEL/L2WOLFCL.XEL";
    private const string InsigniaPalettePath = "PAL/CIND_DA.COL";
    private const string FireSoundPath = "SNDS/MECFIRE1.WAV";

    private static readonly Rect2 FalconHitArea = new(10.0f, 195.0f, 225.0f, 220.0f);
    private static readonly Rect2 WolfHitArea = new(405.0f, 195.0f, 225.0f, 220.0f);

    private readonly MechWarriorProjectArchive m_archive;
    private readonly List<Texture2D> m_titleFrames = [];
    private Texture2D m_titlePlate;
    private Texture2D m_falconInsignia;
    private Texture2D m_wolfInsignia;
    private Texture2D m_mech;
    private AudioStreamPlayer m_firePlayer;
    private Rect2 m_compositionBounds;
    private double m_elapsedSeconds;

    public ClanSelectionScreen(MechWarriorProjectArchive archive)
    {
        m_archive = archive ?? throw new ArgumentNullException(nameof(archive));
    }

    public event Action<ClanCampaignSelection> CampaignSelected;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        m_falconInsignia = LoadIndexedTexture(JadeFalconInsigniaPath);
        m_wolfInsignia = LoadIndexedTexture(WolfInsigniaPath);
        LoadOriginalTitleMedia();
        m_mech = LoadReferenceCenterpiece();
        m_firePlayer = new AudioStreamPlayer
        {
            Name = "ClanSelectionFire",
            Stream = PlayerMechSounds.LoadWaveResource(m_archive, FireSoundPath, true, "clan-selection fire"),
            VolumeDb = -18.0f
        };
        AddChild(m_firePlayer);
        m_firePlayer.Play();
        Resized += UpdateCompositionBounds;
        UpdateCompositionBounds();
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        m_firePlayer?.Stop();
    }

    public override void _Process(double delta)
    {
        if (m_titleFrames.Count < 2)
        {
            return;
        }

        m_elapsedSeconds += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
        if (m_compositionBounds.Size.X <= 0.0f || m_compositionBounds.Size.Y <= 0.0f)
        {
            return;
        }

        var scale = m_compositionBounds.Size.X / OriginalWidth;
        DrawSetTransform(m_compositionBounds.Position, 0.0f, new Vector2(scale, scale));

        // The static title plate supplies the original subtitle; the SMK supplies the animated title itself.
        DrawTextureRectRegion(m_titlePlate, new Rect2(116.0f, 20.0f, 408.0f, 104.0f), new Rect2(116.0f, 180.0f, 408.0f, 104.0f));
        if (m_titleFrames.Count > 0)
        {
            var frameIndex = (int)(m_elapsedSeconds * TitleFramesPerSecond) % m_titleFrames.Count;
            DrawTextureRect(m_titleFrames[frameIndex], new Rect2(116.0f, 20.0f, 408.0f, 76.0f), false);
        }

        DrawTextureRect(m_falconInsignia, new Rect2(0.0f, 185.0f, 255.0f, 255.0f), false);
        DrawTextureRect(m_wolfInsignia, new Rect2(385.0f, 190.0f, 255.0f, 255.0f), false);
        DrawTextureRect(m_mech, new Rect2(217.0f, 190.0f, 169.0f, 290.0f), false);
        DrawSetTransform(Vector2.Zero);
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true
            } mouseButton)
        {
            return;
        }

        var campaign = GetCampaignAt(mouseButton.Position);
        if (campaign == ClanCampaignSelection.None)
        {
            return;
        }

        CampaignSelected?.Invoke(campaign);
        AcceptEvent();
    }

    private void LoadOriginalTitleMedia()
    {
        var dataDirectory = ResolveGameDataDirectory();
        m_titlePlate = DecodeEmbeddedGif(Path.Combine(dataDirectory, "DEMODATA", "FIRELOGO.MW2"), "firelogo");
        m_titleFrames.AddRange(DecodeSmackerFrames(Path.Combine(dataDirectory, "DEMODATA", "AMWLOGO1.SMK")));
    }

    private static Texture2D LoadReferenceCenterpiece()
    {
        var filePath = Path.Combine(ResolveGameDataDirectory(), "DEMODATA", "CLANSELECT_CENTER.png");
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The supplied original clan-selection centerpiece is missing.", filePath);
        }

        return LoadTexture(filePath);
    }

    private Texture2D LoadIndexedTexture(string resourcePath)
    {
        var palette = MechWarriorPalette.Load(m_archive.ReadEntry(m_archive.GetEntry(InsigniaPalettePath)));
        var source = MechWarriorIndexedImage.Load(m_archive.ReadEntry(m_archive.GetEntry(resourcePath)));
        var pixels = new byte[source.Width * source.Height * 4];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var sourceIndex = source.GetPixel(x, y);
                if (sourceIndex is 177 or byte.MaxValue)
                {
                    continue;
                }

                var destinationOffset = (y * source.Width + x) * 4;
                var color = palette[sourceIndex];
                pixels[destinationOffset] = color.R;
                pixels[destinationOffset + 1] = color.G;
                pixels[destinationOffset + 2] = color.B;
                pixels[destinationOffset + 3] = byte.MaxValue;
            }
        }

        var image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, pixels);
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D DecodeEmbeddedGif(string sourcePath, string cacheName)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The original title-screen artwork is missing.", sourcePath);
        }

        var sourceData = File.ReadAllBytes(sourcePath);
        var gifStart = FindSequence(sourceData, "GIF87a"u8);
        if (gifStart < 0)
        {
            throw new InvalidDataException($"{sourcePath} does not contain a GIF payload.");
        }

        var cacheDirectory = CreateCacheDirectory();
        var gifPath = Path.Combine(cacheDirectory, $"{cacheName}.gif");
        var outputPath = Path.Combine(cacheDirectory, $"{cacheName}.png");
        if (!File.Exists(outputPath))
        {
            File.WriteAllBytes(gifPath, sourceData[gifStart..]);
            RunFfmpeg(gifPath, outputPath, true);
        }

        return LoadTexture(outputPath);
    }

    private static IReadOnlyList<Texture2D> DecodeSmackerFrames(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The original animated title artwork is missing.", sourcePath);
        }

        var cacheDirectory = CreateCacheDirectory();
        var cachedFrames = Directory.GetFiles(cacheDirectory, "title-*.png").OrderBy(path => path).ToArray();
        if (cachedFrames.Length == 0)
        {
            RunFfmpeg(sourcePath, Path.Combine(cacheDirectory, "title-%04d.png"), false);
            cachedFrames = Directory.GetFiles(cacheDirectory, "title-*.png").OrderBy(path => path).ToArray();
        }

        return cachedFrames.Select(LoadTexture).ToArray();
    }

    private static void RunFfmpeg(string inputPath, string outputPath, bool oneFrame)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);
        if (oneFrame)
        {
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
        }
        else
        {
            startInfo.ArgumentList.Add("-vsync");
            startInfo.ArgumentList.Add("0");
        }

        startInfo.ArgumentList.Add(outputPath);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start ffmpeg.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg could not decode original title media: {error}");
        }
    }

    private static Texture2D LoadTexture(string filePath)
    {
        var image = Image.LoadFromFile(filePath);
        if (image is null || image.IsEmpty())
        {
            throw new InvalidDataException($"Could not load decoded original artwork at {filePath}.");
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static int FindSequence(ReadOnlySpan<byte> source, ReadOnlySpan<byte> sequence)
    {
        for (var offset = 0; offset <= source.Length - sequence.Length; offset++)
        {
            if (source.Slice(offset, sequence.Length).SequenceEqual(sequence))
            {
                return offset;
            }
        }

        return -1;
    }

    private static string ResolveGameDataDirectory()
    {
        var projectDirectory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        var repositoryDirectory = projectDirectory.Parent ?? throw new DirectoryNotFoundException("Could not resolve the repository directory.");
        return Path.Combine(repositoryDirectory.FullName, "local", "game-data");
    }

    private static string CreateCacheDirectory()
    {
        var cacheDirectory = Path.Combine(ProjectSettings.GlobalizePath("user://"), "original-title-media");
        Directory.CreateDirectory(cacheDirectory);
        return cacheDirectory;
    }

    private void UpdateCompositionBounds()
    {
        var scale = Math.Min(Size.X / OriginalWidth, Size.Y / OriginalHeight);
        var compositionSize = new Vector2(OriginalWidth, OriginalHeight) * scale;
        m_compositionBounds = new Rect2((Size - compositionSize) * 0.5f, compositionSize);
    }

    private ClanCampaignSelection GetCampaignAt(Vector2 screenPosition)
    {
        if (!m_compositionBounds.HasPoint(screenPosition))
        {
            return ClanCampaignSelection.None;
        }

        var originalPosition = (screenPosition - m_compositionBounds.Position) * new Vector2(
            OriginalWidth / m_compositionBounds.Size.X,
            OriginalHeight / m_compositionBounds.Size.Y);
        return FalconHitArea.HasPoint(originalPosition)
            ? ClanCampaignSelection.JadeFalcon
            : WolfHitArea.HasPoint(originalPosition) ? ClanCampaignSelection.Wolf : ClanCampaignSelection.None;
    }
}

public enum ClanCampaignSelection
{
    None,
    JadeFalcon,
    Wolf
}
