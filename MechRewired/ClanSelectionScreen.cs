// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Resources;
using SmackerSharp;
using StbImageSharp;

namespace MechRewired;

/// <summary>Composes the original 31st Century Combat title-screen layers and handles clan selection.</summary>
/// <remarks>
/// All visible artwork comes from the player's locally installed game files or the supplied original
/// screen reference. The original title media is decoded in memory without external tools.
/// </remarks>
public sealed partial class ClanSelectionScreen : Control
{
    private const int OriginalWidth = 640;
    private const int OriginalHeight = 480;
    private const string JadeFalconInsigniaPath = "CEL/L2JADEFN.XEL";
    private const string WolfInsigniaPath = "CEL/L2WOLFCL.XEL";
    private const string InsigniaPalettePath = "PAL/CIND_DA.COL";
    private const string FireSoundPath = "SNDS/MECFIRE1.WAV";

    private static readonly Rect2 FalconHitArea = new(10.0f, 195.0f, 225.0f, 220.0f);
    private static readonly Rect2 WolfHitArea = new(405.0f, 195.0f, 225.0f, 220.0f);

    private readonly MechWarriorProjectArchive m_archive;
    private readonly DirectoryInfo m_dataDirectory;
    private readonly List<Texture2D> m_titleFrames = [];
    private Texture2D m_titlePlate;
    private Texture2D m_falconInsignia;
    private Texture2D m_wolfInsignia;
    private Texture2D m_mech;
    private Font m_font;
    private AudioStreamPlayer m_firePlayer;
    private Rect2 m_compositionBounds;
    private double m_elapsedSeconds;
    private double m_titleFramesPerSecond;
    private ClanCampaignSelection m_hoveredCampaign;

    public ClanSelectionScreen(MechWarriorProjectArchive archive, DirectoryInfo dataDirectory)
    {
        m_archive = archive ?? throw new ArgumentNullException(nameof(archive));
        m_dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
    }

    public event Action<ClanCampaignSelection> CampaignSelected;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        m_font = GD.Load<FontFile>("res://Assets/Fonts/Orbitron-Variable.ttf") ?? ThemeDB.FallbackFont;
        m_falconInsignia = LoadIndexedTexture(JadeFalconInsigniaPath);
        m_wolfInsignia = LoadIndexedTexture(WolfInsigniaPath);
        // Presentation extras must not prevent a valid DOS archive from reaching a campaign.
        try
        {
            LoadOriginalTitleMedia();
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Optional original title media could not be decoded: {exception.Message}");
        }
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
        MouseExited += () => SetHoveredCampaign(ClanCampaignSelection.None);
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
        if (m_titlePlate != null)
            DrawTextureRectRegion(m_titlePlate, new Rect2(116.0f, 20.0f, 408.0f, 104.0f), new Rect2(116.0f, 180.0f, 408.0f, 104.0f));
        if (m_titleFrames.Count > 0)
        {
            var frameIndex = (int)(m_elapsedSeconds * m_titleFramesPerSecond) % m_titleFrames.Count;
            DrawTextureRect(m_titleFrames[frameIndex], new Rect2(116.0f, 20.0f, 408.0f, 76.0f), false);
        }

        DrawTextureRect(m_falconInsignia, new Rect2(0.0f, 185.0f, 255.0f, 255.0f), false);
        DrawTextureRect(m_wolfInsignia, new Rect2(385.0f, 190.0f, 255.0f, 255.0f), false);
        if (m_mech != null)
            DrawTextureRect(m_mech, new Rect2(217.0f, 190.0f, 169.0f, 290.0f), false);
        DrawHoveredClanName();
        DrawSetTransform(Vector2.Zero);
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseMotion mouseMotion)
        {
            SetHoveredCampaign(GetCampaignAt(mouseMotion.Position));
            return;
        }

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
        var titlePath = Path.Combine(m_dataDirectory.FullName, "DEMODATA", "FIRELOGO.MW2");
        var animationPath = Path.Combine(m_dataDirectory.FullName, "DEMODATA", "AMWLOGO1.SMK");
        if (File.Exists(titlePath))
            m_titlePlate = DecodeEmbeddedGif(titlePath);
        if (File.Exists(animationPath))
            m_titleFrames.AddRange(DecodeSmackerFrames(animationPath, out m_titleFramesPerSecond));
    }

    private Texture2D LoadReferenceCenterpiece()
    {
        var filePath = Path.Combine(m_dataDirectory.FullName, "DEMODATA", "CLANSELECT_CENTER.png");
#if DEBUG
        if (!File.Exists(filePath))
        {
            var projectDirectory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
            var repositoryDirectory = projectDirectory.Parent;
            if (repositoryDirectory != null)
            {
                filePath = Path.Combine(repositoryDirectory.FullName, "local", "game-data", "DEMODATA",
                    "CLANSELECT_CENTER.png");
            }
        }
#endif
        try
        {
            return File.Exists(filePath) ? LoadTexture(filePath) : null;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Optional clan-selection reference could not be loaded: {exception.Message}");
            return null;
        }
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

    private static Texture2D DecodeEmbeddedGif(string sourcePath)
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

        var decoded = ImageResult.FromMemory(sourceData[gifStart..], ColorComponents.RedGreenBlueAlpha);
        return CreateTexture(decoded.Width, decoded.Height, decoded.Data);
    }

    private static IReadOnlyList<Texture2D> DecodeSmackerFrames(string sourcePath, out double framesPerSecond)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The original animated title artwork is missing.", sourcePath);
        }

        using var reader = SmackerReader.Open(sourcePath);
        reader.SetEnabled(SmackerTrackMask.Video);
        framesPerSecond = 1_000_000.0 / reader.Info.MicrosecondsPerFrame;
        var width = checked((int)reader.VideoInfo.Width);
        var height = checked((int)reader.VideoInfo.Height);
        var frames = new List<Texture2D>(checked((int)reader.Info.FrameCount));
        for (var result = reader.First(); result != SmackerFrameResult.Done; result = reader.Next())
        {
            var indices = reader.VideoFrame8;
            var palette = reader.PaletteRgb;
            var pixels = new byte[checked(width * height * 4)];
            for (var pixelIndex = 0; pixelIndex < indices.Length; pixelIndex++)
            {
                var paletteOffset = indices[pixelIndex] * 3;
                var destinationOffset = pixelIndex * 4;
                pixels[destinationOffset] = palette[paletteOffset];
                pixels[destinationOffset + 1] = palette[paletteOffset + 1];
                pixels[destinationOffset + 2] = palette[paletteOffset + 2];
                pixels[destinationOffset + 3] = byte.MaxValue;
            }

            frames.Add(CreateTexture(width, height, pixels));
        }

        return frames;
    }

    private static Texture2D CreateTexture(int width, int height, byte[] pixels)
    {
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
        return ImageTexture.CreateFromImage(image);
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

    private void DrawHoveredClanName()
    {
        var (name, area) = m_hoveredCampaign switch
        {
            ClanCampaignSelection.JadeFalcon => ("CLAN JADE FALCON", new Rect2(0.0f, 0.0f, 255.0f, 0.0f)),
            ClanCampaignSelection.Wolf => ("CLAN WOLF", new Rect2(385.0f, 0.0f, 255.0f, 0.0f)),
            _ => (null, default)
        };
        if (name == null)
            return;

        const int maximumFontSize = 20;
        var measuredWidth = m_font.GetStringSize(name, HorizontalAlignment.Left, -1.0f, maximumFontSize).X;
        var fontSize = measuredWidth <= area.Size.X
            ? maximumFontSize
            : Math.Max(12, Mathf.FloorToInt(maximumFontSize * area.Size.X / measuredWidth));
        var position = new Vector2(area.Position.X, 466.0f);
        DrawString(m_font, position + Vector2.One * 2.0f, name, HorizontalAlignment.Center, area.Size.X, fontSize, Colors.Black);
        DrawString(m_font, position, name, HorizontalAlignment.Center, area.Size.X, fontSize, Colors.White);
    }

    private void SetHoveredCampaign(ClanCampaignSelection campaign)
    {
        if (m_hoveredCampaign == campaign)
            return;

        m_hoveredCampaign = campaign;
        MouseDefaultCursorShape = campaign == ClanCampaignSelection.None
            ? CursorShape.Arrow
            : CursorShape.PointingHand;
        QueueRedraw();
    }

    private void UpdateCompositionBounds()
    {
        var scale = Math.Min(Size.X / OriginalWidth, Size.Y / OriginalHeight);
        var compositionSize = new Vector2(OriginalWidth, OriginalHeight) * scale;
        m_compositionBounds = new Rect2((Size - compositionSize) * 0.5f, compositionSize);
    }

    private ClanCampaignSelection GetCampaignAt(Vector2 screenPosition)
    {
        if (m_compositionBounds.Size.X <= 0.0f || !m_compositionBounds.HasPoint(screenPosition))
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
