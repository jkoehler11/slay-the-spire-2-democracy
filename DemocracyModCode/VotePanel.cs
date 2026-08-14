using Godot;
using System.Collections.Generic;
using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;
using DemocracyMod.DemocracyModCode.Patches;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Claim-based reward distribution UI.
/// - Checkbox per indivisible reward (card/potion/relic).
/// - SpinBox for the amount of gold you want from the shared pool.
/// - Submit broadcasts your claim; then shows "waiting for others".
/// </summary>
public partial class VotePanel : CanvasLayer
{
    private readonly Dictionary<string, CheckBox> _claimBoxes = new();
    private SpinBox _goldSpin = null!;
    private Label _status = null!;
    private Button _submitBtn = null!;
    private bool _submitted;
    private int _timeout = DemocracyConfig.VoteTimeoutSeconds;
    private int _elapsed;

    public override void _Ready()
    {
        Layer = 100;

        // Blocking backdrop (this is a modal)
        var bg = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.75f),
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(bg);

        var panel = new Panel
        {
            Size = new Vector2(760, 600),
            Position = new Vector2(
                (DisplayServer.WindowGetSize().X - 760) / 2,
                (DisplayServer.WindowGetSize().Y - 600) / 2),
        };
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.08f, 0.15f, 0.96f);
        style.SetBorderWidthAll(2);
        style.BorderColor = new Color(0.8f, 0.6f, 0.1f);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        // Title
        var title = new Label
        {
            Text = MainFile.Loc("DemocracyMod.VotePanel.Title", "\U0001f5f3 CLAIM REWARDS")
                + (DemocracyConfig.OpenVoting ? MainFile.Loc("DemocracyMod.VotePanel.VotesPublic", " - VOTES PUBLIC") : ""),
            Position = new Vector2(20, 12),
            Size = new Vector2(720, 36),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 26);
        panel.AddChild(title);

        var subtitle = new Label
        {
            Text = string.Format(
                MainFile.Loc("DemocracyMod.VotePanel.Subtitle", "Gold pool: {0}g   |   Cards: {1}   Potions: {2}   Relics: {3}"),
                RewardPool.TotalGoldPooled, RewardPool.TotalCardsPooled,
                RewardPool.TotalPotionsPooled, RewardPool.TotalRelicsPooled),
            Position = new Vector2(20, 48),
            Size = new Vector2(720, 22),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        subtitle.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        subtitle.AddThemeFontSizeOverride("font_size", 15);
        panel.AddChild(subtitle);

        // Scrollable list
        var scroll = new ScrollContainer
        {
            Position = new Vector2(20, 78),
            Size = new Vector2(720, 430),
        };
        panel.AddChild(scroll);

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);

        // --- Gold row with SpinBox ---
        if (RewardPool.TotalGoldPooled > 0)
        {
            var goldRow = new HBoxContainer();
            goldRow.AddThemeConstantOverride("separation", 10);

            var goldLabel = new Label
            {
                Text = string.Format(
                    MainFile.Loc("DemocracyMod.VotePanel.GoldLabel", "Gold (from shared pool of {0}g):"),
                    RewardPool.TotalGoldPooled),
                CustomMinimumSize = new Vector2(360, 32),
            };
            goldLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.3f));
            goldLabel.AddThemeFontSizeOverride("font_size", 16);
            goldRow.AddChild(goldLabel);

            _goldSpin = new SpinBox
            {
                MinValue = 0,
                MaxValue = RewardPool.TotalGoldPooled,
                Step = 1,
                Value = DemocracyConfig.RewardSelection == RewardSelectionMode.SelectAllRewards ? RewardPool.TotalGoldPooled : 0,
                CustomMinimumSize = new Vector2(120, 32),
            };
            _goldSpin.AddThemeFontSizeOverride("font_size", 16);
            goldRow.AddChild(_goldSpin);

            var goldSuffix = new Label { Text = MainFile.Loc("DemocracyMod.VotePanel.GoldSuffix", "g"), CustomMinimumSize = new Vector2(20, 32) };
            goldSuffix.AddThemeFontSizeOverride("font_size", 16);
            goldRow.AddChild(goldSuffix);

            list.AddChild(goldRow);

            // Divider
            var hs = new HSeparator();
            list.AddChild(hs);
        }

        // --- Non-gold reward checkboxes ---
        foreach (var entry in RewardPool.GetNonGoldPending())
        {
            var cb = new CheckBox
            {
                Text = entry.DisplayName,
                ButtonPressed = DemocracyConfig.RewardSelection == RewardSelectionMode.SelectAllRewards,
                CustomMinimumSize = new Vector2(0, 34),
            };
            cb.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            cb.AddThemeFontSizeOverride("font_size", 17);
            _claimBoxes[entry.Id] = cb;
            list.AddChild(cb);
        }

        // --- Status label (hidden until submit) ---
        _status = new Label
        {
            Text = "",
            Position = new Vector2(20, 516),
            Size = new Vector2(720, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _status.AddThemeFontSizeOverride("font_size", 16);
        panel.AddChild(_status);

        // --- Submit button ---
        _submitBtn = new Button
        {
            Text = MainFile.Loc("DemocracyMod.VotePanel.Submit", "Submit Claims"),
            Position = new Vector2(230, 550),
            Size = new Vector2(300, 38),
        };
        _submitBtn.Pressed += OnSubmit;
        panel.AddChild(_submitBtn);

        // Initialize local player ID
        MultiplayerCoordinator.InitializeForRun();

        MainFile.LogVote(string.Format("Democracy: ClaimPanel shown — {0} rewards, {1}g",
            _claimBoxes.Count, RewardPool.TotalGoldPooled));
    }

    private void OnSubmit()
    {
        if (_submitted) return;
        _submitted = true;

        var claimedIds = new List<string>();
        foreach (var kv in _claimBoxes)
            if (kv.Value.ButtonPressed)
                claimedIds.Add(kv.Key);

        int goldAmount = _goldSpin != null ? (int)_goldSpin.Value : 0;

        MainFile.LogVote(string.Format("Democracy: submitting claim — {0}g + {1} rewards",
            goldAmount, claimedIds.Count));

        MultiplayerCoordinator.SendClaim(goldAmount, claimedIds);
        VoteManager.SubmitClaim(MultiplayerCoordinator.LocalPlayerId, goldAmount, claimedIds);

        // Disable all inputs, show waiting state
        _submitBtn.Disabled = true;
        _goldSpin?.SetEditable(false);
        foreach (var cb in _claimBoxes.Values) cb.Disabled = true;

        _status.Text = MainFile.Loc("DemocracyMod.VotePanel.Waiting", "Waiting for other players to submit...");
        _status.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 1.0f));
    }

    public override void _Process(double delta)
    {
        _elapsed++;

        if (_submitted && VoteManager.ResolutionDone)
        {
            _status.Text = MainFile.Loc("DemocracyMod.VotePanel.Done", "Distribution complete!");
            _status.AddThemeColorOverride("font_color", new Color(0.4f, 1.0f, 0.4f));
            // Auto-close after a couple seconds
            if (_elapsed > 180)
                QueueFree();
            return;
        }

        // Timeout
        if (_elapsed % 60 == 0)
        {
            var remaining = _timeout - _elapsed / 60;
            if (remaining <= 0)
            {
                MainFile.LogVote("Democracy: claim timeout — auto-distributing.");
                if (!_submitted) OnSubmit();
                else if (!VoteManager.ResolutionDone)
                {
                    // force resolve
                    RewardPool.DistributeEvenly();
                    QueueFree();
                }
            }
        }
    }
}
