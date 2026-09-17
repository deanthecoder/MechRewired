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
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>Recreates the original shell's Battle Parameters and Combat Variables flow.</summary>
public sealed partial class BattleParametersScreen : Control
{
    private const int OriginalWidth = 640;
    private const int OriginalHeight = 480;
    private static readonly Color Backdrop = Color.FromHtml("05080b");
    private static readonly Color Panel = Color.FromHtml("121a22");
    private static readonly Color PanelEdge = Color.FromHtml("52616d");
    private static readonly Color Row = Color.FromHtml("29343d");
    private static readonly Color SelectedRow = Color.FromHtml("495761");
    private static readonly Color Heading = Color.FromHtml("c43f3f");
    private static readonly Color PrimaryText = Color.FromHtml("e0e5e8");
    private static readonly Color SecondaryText = Color.FromHtml("94a4ae");
    private static readonly Rect2 CombatVariablesRect = new(150, 190, 340, 44);
    private static readonly Rect2 ReturnRect = new(150, 246, 340, 44);
    private static readonly Rect2 DifficultyRect = new(150, 184, 340, 58);
    private static readonly Rect2 AcceptRect = new(150, 328, 160, 42);
    private static readonly Rect2 CancelRect = new(330, 328, 160, 42);

    private Rect2 m_compositionBounds;
    private Font m_font;
    private Page m_page;
    private CombatDifficulty m_pendingDifficulty;
    private int m_selectedMenuItem;

    public BattleParametersScreen(CombatDifficulty difficulty)
    {
        m_pendingDifficulty = difficulty;
        Name = "BattleParameters";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public event Action<CombatDifficulty> DifficultyAccepted;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        m_font = GD.Load<FontFile>("res://Assets/Fonts/Orbitron-Variable.ttf") ?? ThemeDB.FallbackFont;
        Resized += UpdateCompositionBounds;
        UpdateCompositionBounds();
    }

    public void Open(CombatDifficulty difficulty)
    {
        m_pendingDifficulty = difficulty;
        m_page = Page.BattleParameters;
        m_selectedMenuItem = 0;
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        QueueRedraw();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!Visible || inputEvent is not InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        if (m_page == Page.BattleParameters)
        {
            HandleBattleParametersKey(keyEvent.Keycode);
        }
        else
        {
            HandleCombatVariablesKey(keyEvent.Keycode);
        }
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (!Visible || inputEvent is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true
            } mouseButton)
        {
            return;
        }

        var position = ToOriginalPosition(mouseButton.Position);
        if (m_page == Page.BattleParameters)
        {
            if (CombatVariablesRect.HasPoint(position))
            {
                ShowCombatVariables();
            }
            else if (ReturnRect.HasPoint(position))
            {
                Close();
            }
        }
        else if (DifficultyRect.HasPoint(position))
        {
            CycleDifficulty(position.X < DifficultyRect.GetCenter().X ? -1 : 1);
        }
        else if (AcceptRect.HasPoint(position))
        {
            AcceptDifficulty();
        }
        else if (CancelRect.HasPoint(position))
        {
            ShowBattleParameters();
        }

        AcceptEvent();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.94f));
        if (m_compositionBounds.Size.X <= 0.0f)
        {
            return;
        }

        var scale = m_compositionBounds.Size.X / OriginalWidth;
        DrawSetTransform(m_compositionBounds.Position, 0.0f, new Vector2(scale, scale));
        DrawRect(new Rect2(95, 48, 450, 384), Backdrop);
        DrawRect(new Rect2(103, 56, 434, 368), Panel);
        DrawRect(new Rect2(103, 56, 434, 2), PanelEdge);
        DrawRect(new Rect2(103, 422, 434, 2), PanelEdge);

        if (m_page == Page.BattleParameters)
        {
            DrawBattleParameters();
        }
        else
        {
            DrawCombatVariables();
        }

        DrawSetTransform(Vector2.Zero);
    }

    private void DrawBattleParameters()
    {
        DrawHeading("BATTLE PARAMETERS");
        DrawButton(CombatVariablesRect, "COMBAT VARIABLES", m_selectedMenuItem == 0);
        DrawButton(ReturnRect, "RETURN", m_selectedMenuItem == 1);
        DrawCentered("Configure combat assistance before deployment.", 315, 14, SecondaryText);
    }

    private void DrawCombatVariables()
    {
        DrawHeading("COMBAT VARIABLES");
        DrawRect(DifficultyRect, Row);
        DrawString(m_font, new Vector2(166, 219), "DIFFICULTY", HorizontalAlignment.Left, 140, 18, PrimaryText);
        DrawString(m_font, new Vector2(310, 219), "<", HorizontalAlignment.Center, 24, 20, PrimaryText);
        DrawString(
            m_font,
            new Vector2(334, 219),
            m_pendingDifficulty.ToString().ToUpperInvariant(),
            HorizontalAlignment.Center,
            116,
            19,
            PrimaryText);
        DrawString(m_font, new Vector2(450, 219), ">", HorizontalAlignment.Center, 24, 20, PrimaryText);
        DrawCentered(GetDifficultyDescription(m_pendingDifficulty), 276, 14, SecondaryText);
        DrawButton(AcceptRect, "ACCEPT", true);
        DrawButton(CancelRect, "CANCEL", false);
    }

    private void DrawHeading(string heading)
    {
        DrawCentered(heading, 118, 25, Heading);
        DrawRect(new Rect2(132, 135, 376, 2), Heading);
    }

    private void DrawButton(Rect2 rect, string text, bool selected)
    {
        DrawRect(rect, selected ? SelectedRow : Row);
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 2)), PanelEdge);
        DrawString(
            m_font,
            new Vector2(rect.Position.X, rect.Position.Y + 29),
            text,
            HorizontalAlignment.Center,
            rect.Size.X,
            18,
            PrimaryText);
    }

    private void DrawCentered(string text, float baseline, int fontSize, Color color) =>
        DrawString(m_font, new Vector2(115, baseline), text, HorizontalAlignment.Center, 410, fontSize, color);

    private void HandleBattleParametersKey(Key key)
    {
        switch (key)
        {
            case Key.Up:
            case Key.Down:
                m_selectedMenuItem = 1 - m_selectedMenuItem;
                QueueRedraw();
                break;

            case Key.Enter:
            case Key.Space:
                if (m_selectedMenuItem == 0)
                {
                    ShowCombatVariables();
                }
                else
                {
                    Close();
                }
                break;

            case Key.Escape:
                Close();
                break;
        }
    }

    private void HandleCombatVariablesKey(Key key)
    {
        switch (key)
        {
            case Key.Left:
                CycleDifficulty(-1);
                break;

            case Key.Right:
                CycleDifficulty(1);
                break;

            case Key.Enter:
            case Key.Space:
                AcceptDifficulty();
                break;

            case Key.Escape:
                ShowBattleParameters();
                break;
        }
    }

    private void CycleDifficulty(int direction)
    {
        var values = Enum.GetValues<CombatDifficulty>();
        var index = Array.IndexOf(values, m_pendingDifficulty);
        m_pendingDifficulty = values[(index + direction + values.Length) % values.Length];
        QueueRedraw();
    }

    private void AcceptDifficulty()
    {
        DifficultyAccepted?.Invoke(m_pendingDifficulty);
        ShowBattleParameters();
    }

    private void ShowCombatVariables()
    {
        m_page = Page.CombatVariables;
        QueueRedraw();
    }

    private void ShowBattleParameters()
    {
        m_page = Page.BattleParameters;
        m_selectedMenuItem = 0;
        QueueRedraw();
    }

    private void Close()
    {
        Visible = false;
    }

    private void UpdateCompositionBounds()
    {
        var scale = Math.Min(Size.X / OriginalWidth, Size.Y / OriginalHeight);
        var compositionSize = new Vector2(OriginalWidth, OriginalHeight) * scale;
        m_compositionBounds = new Rect2((Size - compositionSize) * 0.5f, compositionSize);
        QueueRedraw();
    }

    private Vector2 ToOriginalPosition(Vector2 screenPosition) =>
        (screenPosition - m_compositionBounds.Position) * new Vector2(
            OriginalWidth / m_compositionBounds.Size.X,
            OriginalHeight / m_compositionBounds.Size.Y);

    private static string GetDifficultyDescription(CombatDifficulty difficulty) => difficulty switch
    {
        CombatDifficulty.Easy => "Enhanced targeting assistance",
        CombatDifficulty.Medium => "Standard targeting assistance",
        CombatDifficulty.Hard => "Minimal targeting assistance",
        _ => string.Empty
    };

    private enum Page
    {
        BattleParameters,
        CombatVariables
    }
}
