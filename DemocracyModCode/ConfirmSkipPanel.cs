using Godot;
using System;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Blocking confirmation shown when a player presses the loot screen's "Skip" button
/// while they still have unclaimed rewards. Confirming declines the remaining rewards
/// (they are not pooled) and completes the player's reward set so the group can advance;
/// cancelling returns to the loot screen unchanged.
/// </summary>
public partial class ConfirmSkipPanel : CanvasLayer
{
    private int _remaining;
    private Action? _onConfirm;

    public static void Show(int remaining, Action onConfirm)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;
        var panel = new ConfirmSkipPanel { _remaining = remaining, _onConfirm = onConfirm };
        tree.Root.AddChild(panel);
    }

    public override void _Ready()
    {
        Layer = 110;

        var bg = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.6f),
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(bg);

        var panel = new Panel
        {
            Size = new Vector2(480, 210),
            Position = new Vector2(
                (DisplayServer.WindowGetSize().X - 480) / 2,
                (DisplayServer.WindowGetSize().Y - 210) / 2),
        };
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.08f, 0.15f, 0.97f);
        style.SetBorderWidthAll(2);
        style.BorderColor = new Color(0.9f, 0.5f, 0.1f);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var title = new Label
        {
            Text = MainFile.Loc("DemocracyMod.ConfirmSkip.Title", "Skip Remaining Rewards?"),
            Position = new Vector2(20, 16),
            Size = new Vector2(440, 34),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.6f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 22);
        panel.AddChild(title);

        var body = new Label
        {
            Text = string.Format(
                MainFile.Loc("DemocracyMod.ConfirmSkip.Body",
                    "You still have {0} reward(s) to claim. Skipping will discard them."),
                _remaining),
            Position = new Vector2(20, 62),
            Size = new Vector2(440, 56),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        body.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        body.AddThemeFontSizeOverride("font_size", 16);
        panel.AddChild(body);

        var cancel = new Button
        {
            Text = MainFile.Loc("DemocracyMod.ConfirmSkip.Cancel", "Cancel"),
            Position = new Vector2(70, 150),
            Size = new Vector2(150, 40),
        };
        cancel.Pressed += QueueFree;
        panel.AddChild(cancel);

        var confirm = new Button
        {
            Text = MainFile.Loc("DemocracyMod.ConfirmSkip.Confirm", "Skip Rewards"),
            Position = new Vector2(260, 150),
            Size = new Vector2(150, 40),
        };
        confirm.Pressed += () =>
        {
            var cb = _onConfirm;
            QueueFree();
            cb?.Invoke();
        };
        panel.AddChild(confirm);

        MainFile.LogDebug(string.Format("Democracy: confirm-skip panel shown ({0} remaining).", _remaining));
    }
}
