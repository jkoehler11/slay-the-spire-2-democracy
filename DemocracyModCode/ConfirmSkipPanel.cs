using Godot;
using System;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Blocking confirmation dialog. Two uses:
///   1. Skip rewards — shown when a player presses the loot screen's "Skip" button while
///      they still have unclaimed rewards. Confirming declines the remaining rewards (they
///      are not pooled) and completes the player's reward set so the group can advance.
///   2. Finish shopping — shown when a player presses the shop's Proceed button while the
///      shared shop phase is active. Confirming marks the player done and (once everyone
///      confirms) triggers the pooled-purchase vote.
/// Cancelling returns to the underlying screen unchanged in both cases.
/// </summary>
public partial class ConfirmSkipPanel : CanvasLayer
{
    private string _title = "";
    private string _body = "";
    private string _confirmLabel = "";
    private Action? _onConfirm;

    /// <summary>Skip-remaining-rewards confirmation.</summary>
    public static void Show(int remaining, Action onConfirm)
    {
        var title = MainFile.Loc("DemocracyMod.ConfirmSkip.Title", "Skip Remaining Rewards?");
        var body = string.Format(
            MainFile.Loc("DemocracyMod.ConfirmSkip.Body",
                "You still have {0} reward(s) to claim. Skipping will discard them."),
            remaining);
        var confirm = MainFile.Loc("DemocracyMod.ConfirmSkip.Confirm", "Skip Rewards");
        ShowDialog(title, body, confirm, onConfirm);
    }

    /// <summary>Finish-shopping confirmation (shop Proceed button).</summary>
    public static void ShowShop(Action onConfirm)
    {
        var title = MainFile.Loc("DemocracyMod.ConfirmShop.Title", "Finish Shopping?");
        var body = MainFile.Loc("DemocracyMod.ConfirmShop.Body",
            "You won't be able to buy anything else.\nYour purchases will be pooled and voted on.");
        var confirm = MainFile.Loc("DemocracyMod.ConfirmShop.Confirm", "Finish Shopping");
        ShowDialog(title, body, confirm, onConfirm);
    }

    private static void ShowDialog(string title, string body, string confirmLabel, Action onConfirm)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;
        var panel = new ConfirmSkipPanel
        {
            _title = title,
            _body = body,
            _confirmLabel = confirmLabel,
            _onConfirm = onConfirm,
        };
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

        const float W = 520f;
        const float H = 240f;

        var panel = new Panel
        {
            Size = new Vector2(W, H),
            Position = new Vector2(
                (DisplayServer.WindowGetSize().X - W) / 2,
                (DisplayServer.WindowGetSize().Y - H) / 2),
        };
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.08f, 0.15f, 0.97f);
        style.SetBorderWidthAll(2);
        style.BorderColor = new Color(0.9f, 0.5f, 0.1f);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        const float padX = 24f;

        var title = new Label
        {
            Text = _title,
            Position = new Vector2(padX, 18),
            Size = new Vector2(W - padX * 2, 40),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.6f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 22);
        panel.AddChild(title);

        var body = new Label
        {
            Text = _body,
            Position = new Vector2(padX, 66),
            Size = new Vector2(W - padX * 2, 92),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        body.AddThemeFontSizeOverride("font_size", 16);
        panel.AddChild(body);

        var cancel = new Button
        {
            Text = MainFile.Loc("DemocracyMod.ConfirmSkip.Cancel", "Cancel"),
            Position = new Vector2(80, 178),
            Size = new Vector2(160, 42),
        };
        cancel.Pressed += QueueFree;
        panel.AddChild(cancel);

        var confirm = new Button
        {
            Text = _confirmLabel,
            Position = new Vector2(280, 178),
            Size = new Vector2(160, 42),
        };
        confirm.Pressed += () =>
        {
            var cb = _onConfirm;
            QueueFree();
            cb?.Invoke();
        };
        panel.AddChild(confirm);

        MainFile.LogDebug("Democracy: confirmation panel shown.");
    }
}
