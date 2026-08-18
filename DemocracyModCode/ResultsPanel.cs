using Godot;
using System;
using System.Collections.Generic;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Post-distribution summary: shows what every player received after the vote resolves.
/// Blocking modal with a Continue button — the reward flow can't advance until dismissed.
/// When onContinue is provided it is invoked after Continue is pressed (used to drive the
/// game's own "proceed past rewards" step, since the vanilla loot screen is suppressed).
/// </summary>
public partial class ResultsPanel : CanvasLayer
{
    private List<string> _lines = new();
    private Action? _onContinue;

    public void SetLines(List<string> lines, Action? onContinue = null)
    {
        _lines = lines;
        _onContinue = onContinue;
    }

    public override void _Ready()
    {
        Layer = 100;

        var bg = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.75f),
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(bg);

        var panel = new Panel
        {
            Size = new Vector2(700, 620),
            Position = new Vector2(
                (DisplayServer.WindowGetSize().X - 700) / 2,
                (DisplayServer.WindowGetSize().Y - 620) / 2),
        };
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.08f, 0.15f, 0.96f);
        style.SetBorderWidthAll(2);
        style.BorderColor = new Color(0.4f, 1.0f, 0.4f);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var title = new Label
        {
            Text = MainFile.Loc("DemocracyMod.ResultsPanel.Title", "\U0001f3c6 DISTRIBUTION RESULTS"),
            Position = new Vector2(20, 12),
            Size = new Vector2(660, 36),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", new Color(0.4f, 1.0f, 0.4f));
        title.AddThemeFontSizeOverride("font_size", 26);
        panel.AddChild(title);

        var scroll = new ScrollContainer
        {
            Position = new Vector2(20, 56),
            Size = new Vector2(660, 500),
        };
        panel.AddChild(scroll);

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);

        foreach (var line in _lines)
        {
            var lbl = new Label { Text = line, CustomMinimumSize = new Vector2(0, 26) };
            lbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            lbl.AddThemeFontSizeOverride("font_size", 16);
            list.AddChild(lbl);
        }

        var cont = new Button
        {
            Text = MainFile.Loc("DemocracyMod.ResultsPanel.Continue", "Continue"),
            Position = new Vector2(250, 570),
            Size = new Vector2(200, 38),
        };
        cont.Pressed += () =>
        {
            var cb = _onContinue;
            QueueFree();
            cb?.Invoke();
        };
        panel.AddChild(cont);

        MainFile.LogVote(string.Format("Democracy: ResultsPanel shown ({0} lines).", _lines.Count));
    }
}
