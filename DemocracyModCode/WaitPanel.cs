using Godot;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Non-blocking overlay shown while waiting for other players (either still picking,
/// or casting their vote). All controls use MouseFilter.Ignore so clicks pass through
/// to whatever is underneath — purely informational. Optionally shows what the local
/// player selected and who has submitted (ready) so far.
/// </summary>
public partial class WaitPanel : CanvasLayer
{
    private int _ticks;
    private Label _dots = null!;
    private Label _selectionLabel = null!;
    private Label _readyLabel = null!;
    private string _titleText = "";
    private string _subtitleText = "";
    private string _selectionText = "";
    private string _readyText = "";

    /// <summary>Optional override of the banner text. Pass null to keep a default.</summary>
    public void Configure(string? title, string? subtitle, string? selection = null, string? ready = null)
    {
        if (title != null) _titleText = title;
        if (subtitle != null) _subtitleText = subtitle;
        if (selection != null) _selectionText = selection;
        if (ready != null) _readyText = ready;
    }

    /// <summary>Update the "you selected" line (empty hides it).</summary>
    public void SetSelection(string text)
    {
        _selectionText = text;
        if (_selectionLabel != null)
        {
            _selectionLabel.Text = text;
            _selectionLabel.Visible = !string.IsNullOrEmpty(text);
        }
    }

    /// <summary>Update the "ready/waiting" line (empty hides it).</summary>
    public void SetReady(string text)
    {
        _readyText = text;
        if (_readyLabel != null)
        {
            _readyLabel.Text = text;
            _readyLabel.Visible = !string.IsNullOrEmpty(text);
        }
    }

    private string TitleText => string.IsNullOrEmpty(_titleText)
        ? MainFile.Loc("DemocracyMod.WaitPanel.Title", "WAITING FOR PLAYERS")
        : _titleText;

    private string SubtitleBase => string.IsNullOrEmpty(_subtitleText)
        ? MainFile.Loc("DemocracyMod.WaitPanel.Subtitle", "Waiting for all players to finish selecting rewards")
        : _subtitleText;

    public override void _Ready()
    {
        Layer = 99;

        var bg = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.35f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(bg);

        var banner = new PanelContainer
        {
            Position = new Vector2(
                (DisplayServer.WindowGetSize().X - 560) / 2, 40),
            Size = new Vector2(560, 120),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var bannerStyle = new StyleBoxFlat();
        bannerStyle.BgColor = new Color(0.08f, 0.06f, 0.12f, 0.92f);
        bannerStyle.SetBorderWidthAll(1);
        bannerStyle.BorderColor = new Color(0.8f, 0.6f, 0.1f);
        banner.AddThemeStyleboxOverride("panel", bannerStyle);
        AddChild(banner);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);

        var title = new Label
        {
            Text = TitleText,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(title);

        _dots = new Label
        {
            Text = SubtitleBase,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _dots.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        _dots.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_dots);

        _selectionLabel = new Label
        {
            Text = _selectionText,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = !string.IsNullOrEmpty(_selectionText),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _selectionLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
        _selectionLabel.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_selectionLabel);

        _readyLabel = new Label
        {
            Text = _readyText,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = !string.IsNullOrEmpty(_readyText),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _readyLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.95f, 0.5f));
        _readyLabel.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_readyLabel);

        banner.AddChild(box);
    }

    public override void _Process(double delta)
    {
        _ticks++;
        if (_ticks % 30 == 0)
        {
            var dots = (_ticks / 30) % 4;
            _dots.Text = SubtitleBase + new string('.', dots);
        }
    }
}
