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

namespace MechRewired;

/// <summary>Guides first-time players through importing their original DOS game files.</summary>
/// <remarks>Files stay in per-user storage and are validated before the game starts.</remarks>
public sealed partial class GameDataSetupScreen : Control
{
    private const string DownloadPage = "https://gamesnostalgia.net/game/mechwarrior-2-31st-century-combat/files#ms-dos";
    private readonly DirectoryInfo m_destination;
    private readonly string m_initialError;
    private readonly List<Button> m_buttons = [];
    private Label m_status;
    private bool m_installing;

    public GameDataSetupScreen(DirectoryInfo destination, string initialError)
    {
        m_destination = destination;
        m_initialError = initialError;
    }

    public event Action<FileInfo> DataInstalled;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var background = new ColorRect { Color = new Color("10191d"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var edge in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride("margin_" + edge, 36);
        AddChild(margin);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(scroll);
        var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.AddChild(center);
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(680, 0) };
        content.AddThemeConstantOverride("separation", 18);
        center.AddChild(content);
        AddLabel(content, "MECHREWIRED", 36, new Color("e3aa60"));
        AddLabel(content, "Bring your original game files", 25, Colors.White);
        AddLabel(content,
            "MechRewired needs MechWarrior 2: 31st Century Combat (DOS).\n" +
            "The original game data is supplied separately.", 18, new Color("c1cccf"));
        AddLabel(content,
            "Drop your ZIP or 7z, extracted game folder, or MW2.PRJ here.\n" +
            "You can also choose a file or folder below.", 20, Colors.White);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 12);
        content.AddChild(actions);
        AddButton(actions, "Choose archive or MW2.PRJ…", () => Browse(false));
        AddButton(actions, "Choose folder…", () => Browse(true));
        AddButton(content, "Open game download page ↗", () => OS.ShellOpen(DownloadPage));
        AddLabel(content,
            "Choose the PC / English DOS download on GamesNostalgia.\n" +
            "Use a copy you have the right to use. This also works on Mac and Linux. Demos and\n" +
            "other editions may lack the required campaign resources.", 16, new Color("9caeb3"));
        m_status = AddLabel(content, m_initialError, 16, new Color("edbb80"));
        AddLabel(content, "Imported files are saved for your next launch.", 15, new Color("9caeb3"));
        GetWindow().FilesDropped += OnFilesDropped;
        m_buttons[0].GrabFocus();
    }

    public override void _ExitTree()
    {
        GetWindow().FilesDropped -= OnFilesDropped;
    }

    private static Label AddLabel(Node parent, string text, int size, Color color)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(680, 0)
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    private void AddButton(Node parent, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 46) };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += action;
        parent.AddChild(button);
        m_buttons.Add(button);
    }

    private void Browse(bool folder)
    {
        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = folder ? FileDialog.FileModeEnum.OpenDir : FileDialog.FileModeEnum.OpenFile,
            Title = folder ? "Choose your MechWarrior 2 folder" : "Choose a ZIP or 7z package, or MW2.PRJ",
            UseNativeDialog = true
        };
        if (!folder)
            dialog.Filters = ["*.zip,*.ZIP,*.7z,*.7Z,*.prj,*.PRJ ; MechWarrior 2 data"];
        dialog.FileSelected += path => { dialog.QueueFree(); Import(path); };
        dialog.DirSelected += path => { dialog.QueueFree(); Import(path); };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCenteredRatio(0.8f);
    }

    private void OnFilesDropped(string[] paths)
    {
        if (m_installing)
            return;
        if (paths.Length != 1)
        {
            m_status.Text = "Drop one ZIP or 7z, game folder, or MW2.PRJ file at a time.";
            return;
        }
        Import(paths[0]);
    }

    private async void Import(string path)
    {
        if (m_installing)
            return;
        m_installing = true;
        foreach (var button in m_buttons)
            button.Disabled = true;
        m_status.Text = "Checking and importing game files… Large 7z packages can take a few minutes.";
        try
        {
            var file = await Task.Run(() => MechWarriorDataInstaller.Install(path, m_destination));
            if (IsInsideTree())
                DataInstalled?.Invoke(file);
        }
        catch (Exception exception)
        {
            if (IsInsideTree())
                m_status.Text = exception.Message;
            GD.PushWarning($"MechRewired import: {exception.Message}");
        }
        finally
        {
            m_installing = false;
            if (IsInsideTree())
                foreach (var button in m_buttons)
                    button.Disabled = false;
        }
    }
}
