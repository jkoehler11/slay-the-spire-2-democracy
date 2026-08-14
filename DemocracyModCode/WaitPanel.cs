using Godot;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Non-blocking overlay shown while waiting for other players to finish.
/// All controls use MouseFilter.Ignore so clicks pass through to the
/// reward screen underneath — purely informational.
/// </summary>
public partial class WaitPanel : CanvasLayer
{
    private int _ticks;
    private Label _dots = null!;

    public override void _Ready()
    {
        Layer = 99;

        // Non-blocking backdrop — MouseFilter.Ignore lets clicks through
        var bg = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.35f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(bg);

        // Top-center banner (not full-screen center, so it doesn't cover the
        // reward cards in the middle of the screen)
        var banner = new PanelContainer
        {
            Position = new Vector2(
                (DisplayServer.WindowGetSize().X - 480) / 2, 40),
            Size = new Vector2(480, 60),
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
            Text = "WAITING FOR PLAYERS",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(title);

        _dots = new Label
        {
            Text = "Waiting for all players to finish selecting rewards",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _dots.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        _dots.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_dots);

        banner.AddChild(box);

        MainFile.LogVote("Democracy: WaitPanel shown (non-blocking).");
    }

    public override void _Process(double delta)
    {
        _ticks++;
        if (_ticks % 30 == 0)
        {
            var dots = (_ticks / 30) % 4;
            _dots.Text = "Waiting for all players to finish selecting rewards"
                + new string('.', dots);
        }
    }
}
