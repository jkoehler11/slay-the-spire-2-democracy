using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using DemocracyMod.DemocracyModCode.Networking;
using DemocracyMod.DemocracyModCode.Patches;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Drives the synchronized post-combat reward flow as four sequential stages, each
/// presented on the game's NATIVE event screen (NEventRoom + NEventLayout — the same
/// title/description/option-buttons UI the game uses for map events). Every player
/// must submit the current stage before the group advances:
///   Stage 0 — Gold:      a 3-option vote (Original amount / Randomized / Distribute evenly).
///   Stage 1 — Potions:   every potion in the pool; pick 0..N, then Next.
///   Stage 2 — Relics:    every relic in the pool; pick 0..N, then Next.
///   Stage 3 — Cards:     every card in the pool; pick 0..N, then Finish.
/// </summary>
public static class DemocracyFlow
{
    public const int StageGold = 0;
    public const int StagePotions = 1;
    public const int StageRelics = 2;
    public const int StageCards = 3;

    public enum Mode { Multi, Single }

    public class Option
    {
        public string Id = "";
        public string Title = "";
        public string Description = "";
        public bool InitiallySelected = false;
        public RewardPool.PoolEntry? Entry = null;
    }

    private static bool _started;
    private static NEventRoom? _room;
    private static EventModel? _event;
    private static WaitPanel? _waitPanel;

    private static Mode _mode;
    private static string _title = "";
    private static string _subtitle = "";
    private static string _nextLabel = "Next";
    private static List<Option> _options = new();
    private static Action<List<string>>? _onNext;
    private static readonly List<string> _selected = new();
    private static bool _done;

    // Live per-player selections for the CURRENT stage: option id -> set of player
    // NetIds who currently have it selected. Drives the per-button player icons.
    private static readonly Dictionary<string, HashSet<ulong>> _selectionByOption = new();

    // Buffered REMOTE selections keyed by (playerId, stage). A peer's selection
    // broadcast can arrive before this machine's Start()/ShowScreen has run (or before
    // the stage screen is set up); we keep the latest per stage so the icon always
    // shows once the screen renders, instead of dropping the early broadcast.
    private static readonly object _remoteSelLock = new();
    private static readonly Dictionary<ulong, Dictionary<int, List<string>>> _remoteSelections = new();

    /// <summary>True while the native claim screens are shown (drives the get_Instance
    /// patch so the event buttons route to OUR room).</summary>
    public static bool IsClaimActive => _started && !VoteManager.ResolutionDone;

    /// <summary>The live NEventRoom presenting the claim stages (null before the flow starts).</summary>
    public static NEventRoom? Room => _room;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        RewardPool.IsDemocracyFlowActive = true;
        MultiplayerCoordinator.InitializeForRun();
        VoteManager.BeginFlow();

        // Begin at the FIRST stage that actually has loot AND is enabled in the config.
        // Stages with nothing pooled (e.g. a fight that dropped no gold, or no potions),
        // or with their screen disabled in the config, are skipped entirely — their
        // rewards stay with whoever earned them.
        int first = NextStageWithLoot(StageGold - 1);
        if (first < 0)
        {
            // No enabled stage has loot, but there may still be pooled rewards. Resolve
            // them keep-own: gold stays where it was granted and every non-gold reward
            // returns to its source (no vote). The host computes and broadcasts the
            // trivial decision; clients apply it verbatim.
            MainFile.LogVote("Democracy: no enabled stage has loot — resolving keep-own.");
            VoteManager.ResolveKeepOwn();
            return;
        }

        VoteManager.SetCurrentStage(first);
        ShowStage(first);
    }

    /// <summary>True if the given stage should be shown: it has at least one pooled reward
    /// to vote on AND its screen is enabled in the config. A disabled screen's rewards stay
    /// with whoever earned them (the stage is skipped like an empty one).</summary>
    public static bool HasLootForStage(int stage) => stage switch
    {
        StageGold => HostConfig.ShowGoldScreen && RewardPool.TotalGoldPooled > 0,
        StagePotions => HostConfig.ShowPotionsScreen && RewardPool.GetNonGoldPending().Any(e => e.Type == RewardPool.PoolEntry.RewardType.Potion),
        StageRelics => HostConfig.ShowRelicsScreen && RewardPool.GetNonGoldPending().Any(e => e.Type is RewardPool.PoolEntry.RewardType.Relic or RewardPool.PoolEntry.RewardType.BossRelic),
        StageCards => HostConfig.ShowCardsScreen && RewardPool.GetNonGoldPending().Any(e => e.Type == RewardPool.PoolEntry.RewardType.CardReward),
        _ => false,
    };

    /// <summary>The first stage strictly after <paramref name="afterStage"/> that has loot,
    /// or -1 if none remain. Used to skip empty stages deterministically on every machine.</summary>
    public static int NextStageWithLoot(int afterStage)
    {
        for (int s = afterStage + 1; s <= StageCards; s++)
            if (HasLootForStage(s))
                return s;
        return -1;
    }

    public static void ShowStage(int stage)
    {
        CloseWait();
        switch (stage)
        {
            case StageGold:
                ShowGold();
                break;
            case StagePotions:
                ShowRewardType(
                    e => e.Type == RewardPool.PoolEntry.RewardType.Potion,
                    StagePotions,
                    MainFile.Loc("DemocracyMod.Choice.PotionsTitle", "Claim Potions"),
                    MainFile.Loc("DemocracyMod.Choice.PotionsSubtitle", "Select the potions you want to claim."),
                    MainFile.Loc("DemocracyMod.Choice.Next", "Next"));
                break;
            case StageRelics:
                ShowRewardType(
                    e => e.Type is RewardPool.PoolEntry.RewardType.Relic or RewardPool.PoolEntry.RewardType.BossRelic,
                    StageRelics,
                    MainFile.Loc("DemocracyMod.Choice.RelicsTitle", "Claim Relics"),
                    MainFile.Loc("DemocracyMod.Choice.RelicsSubtitle", "Select the relics you want to claim."),
                    MainFile.Loc("DemocracyMod.Choice.Next", "Next"));
                break;
            case StageCards:
                ShowRewardType(
                    e => e.Type == RewardPool.PoolEntry.RewardType.CardReward,
                    StageCards,
                    MainFile.Loc("DemocracyMod.Choice.CardsTitle", "Claim Cards"),
                    MainFile.Loc("DemocracyMod.Choice.CardsSubtitle", "Select the cards you want to claim."),
                    MainFile.Loc("DemocracyMod.Choice.Finish", "Finish"));
                break;
        }
    }

    private static void ShowRewardType(
        Func<RewardPool.PoolEntry, bool> filter,
        int stage,
        string title,
        string subtitle,
        string nextLabel)
    {
        var entries = RewardPool.GetNonGoldPending().Where(filter).OrderBy(e => e.Id).ToList();

        var options = entries.Select(e => new Option
        {
            Id = e.Id,
            Title = e.DisplayName,
            Description = string.Format(
                MainFile.Loc("DemocracyMod.Choice.EarnedBy", "Earned by {0}"),
                VoteManager.PlayerLabel(e.SourcePlayerId)),
            InitiallySelected = false,
            Entry = e,
        }).ToList();

        ShowScreen(Mode.Multi, title, subtitle, nextLabel, options, ids =>
            SubmitStage(stage, -1, ids));
    }

    private static void ShowGold()
    {
        int totalGold = RewardPool.TotalGoldPooled;
        var options = new List<Option>
        {
            new() { Id = "gold_original", Title = MainFile.Loc("DemocracyMod.Gold.OriginalAmount.Title", "Original amount"),
                Description = MainFile.Loc("DemocracyMod.Gold.OriginalAmount.Desc", "Everyone keeps exactly what they earned."),
                InitiallySelected = true },
            new() { Id = "gold_random", Title = MainFile.Loc("DemocracyMod.Gold.Randomized.Title", "Randomized"),
                Description = MainFile.Loc("DemocracyMod.Gold.Randomized.Desc", "Pool all gold and hand it out randomly.") },
            new() { Id = "gold_even", Title = MainFile.Loc("DemocracyMod.Gold.Even.Title", "Distribute evenly"),
                Description = MainFile.Loc("DemocracyMod.Gold.Even.Desc", "Pool all gold and split it evenly.") },
        };

        ShowScreen(Mode.Single,
            MainFile.Loc("DemocracyMod.Gold.Title", "GOLD DISTRIBUTION"),
            string.Format(MainFile.Loc("DemocracyMod.Gold.Subtitle", "The group earned {0} gold. Vote on how to split it."), totalGold),
            MainFile.Loc("DemocracyMod.Choice.Next", "Next"),
            options,
            ids =>
            {
                int mode = ids.Count > 0 ? ModeFromId(ids[0]) : (int)GoldVoteMode.OriginalAmount;
                SubmitStage(StageGold, mode, new List<string>());
            });
    }

    private static int ModeFromId(string id) => id switch
    {
        "gold_random" => (int)GoldVoteMode.Randomized,
        "gold_even" => (int)GoldVoteMode.DistributeEvenly,
        _ => (int)GoldVoteMode.OriginalAmount,
    };

    private static void ShowScreen(
        Mode mode,
        string title,
        string subtitle,
        string nextLabel,
        List<Option> options,
        Action<List<string>> onNext)
    {
        _mode = mode;
        _title = title;
        _subtitle = subtitle;
        _nextLabel = nextLabel;
        _options = options;
        _onNext = onNext;
        _done = false;
        _selected.Clear();
        foreach (var o in options)
            if (o.InitiallySelected) _selected.Add(o.Id);

        // New stage: rebuild the live per-player selection map from the local player's
        // default selection plus any remote selections buffered for this stage, then
        // broadcast our own so peers show our icon from the start.
        RebuildSelectionMap();
        BroadcastSelection();

        if (_room == null || !GodotObject.IsInstanceValid(_room))
        {
            CreateRoom();
            return;
        }
        Render();
    }

    private static void CreateRoom()
    {
        try
        {
            MainFile.Logger.Info("[CRASHDBG] CreateRoom: start");
            var canonical = ModelDb.Get<DemocracyClaimEvent>();
            _event = canonical.ToMutable();
            _event.Owner = CombatRewardPatch.GetPlayer(MultiplayerCoordinator.LocalPlayerId);
            MainFile.Logger.Info("[CRASHDBG] CreateRoom: owner = " + (_event.Owner != null));
            var runState = RunManager.Instance?.State as IRunState;
            _room = NEventRoom.Create(_event, runState, false);
            MainFile.Logger.Info("[CRASHDBG] CreateRoom: room created = " + (_room != null));
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null || _room == null)
            {
                MainFile.LogDebug("Democracy: no SceneTree/Root to host the event screen.");
                return;
            }
            tree.Root.AddChild(_room);
            MainFile.Logger.Info("[CRASHDBG] CreateRoom: AddChild done");

            // _Ready fires SetupLayout (async, ~0.8s). Render our stage content after it settles.
            var timer = tree.CreateTimer(1.0);
            timer.Timeout += () =>
            {
                MainFile.Logger.Info("[CRASHDBG] CreateRoom: timer fired");
                if (_room != null && GodotObject.IsInstanceValid(_room))
                    Render();
            };
            MainFile.Logger.Info("[CRASHDBG] CreateRoom: timer scheduled");
        }
        catch (Exception e)
        {
            MainFile.Logger.Info("[CRASHDBG] CreateRoom error: " + e);
        }
    }

    private static void RegisterOptionLocStrings()
    {
        try
        {
            MainFile.Logger.Info("[CRASHDBG] RegisterOptionLocStrings: entry");
            var lm = LocManager.Instance;
            if (lm == null)
            {
                MainFile.Logger.Info("[CRASHDBG] RegisterOptionLocStrings: LocManager.Instance null");
                return;
            }
            var table = lm.GetTable("events");
            if (table == null)
            {
                MainFile.Logger.Info("[CRASHDBG] RegisterOptionLocStrings: 'events' table null");
                return;
            }
            var dict = new Dictionary<string, string>();
            foreach (var o in _options)
            {
                dict[o.Id + ".title"] = o.Title;
                dict[o.Id + ".description"] = o.Description;
            }
            dict["democracy_next.title"] = _nextLabel;
            dict["democracy_next.description"] = "";
            table.MergeWith(dict);
            MainFile.Logger.Info("[CRASHDBG] RegisterOptionLocStrings: merged " + dict.Count + " keys");
        }
        catch (Exception e)
        {
            MainFile.Logger.Info("[CRASHDBG] RegisterOptionLocStrings error: " + e);
        }
    }

    private static void Render()
    {
        try
        {
            MainFile.Logger.Info("[CRASHDBG] Render: entry");
            if (_room == null || !GodotObject.IsInstanceValid(_room)) { MainFile.Logger.Info("[CRASHDBG] Render: room invalid"); return; }
            var layout = _room.Layout;
            if (layout == null) { MainFile.Logger.Info("[CRASHDBG] Render: layout null"); return; }

            layout.SetTitle(_title);
            layout.SetDescription(_subtitle);
            layout.ClearOptions();
            MainFile.Logger.Info("[CRASHDBG] Render: title/desc/clear done");

            // The 5-param EventOption ctor resolves Title/Description via
            // eventModel.GetOptionTitle/Description(id) -> LocString.GetIfExists("events",
            // id + ".title"/".description"). Those keys don't exist for our synthetic
            // options, so Title/Description come back NULL, and NEventOptionButton._Ready
            // dereferences them (option.Title.GetFormattedText()) during AddOptions ->
            // native SIGSEGV. Fix: register our option text under those exact keys in the
            // "events" table before building options so the lookup resolves to real text.
            RegisterOptionLocStrings();

            // CRITICAL: EventOption's ctor ends by calling AddLocVars, which (with a
            // non-null Owner) calls Owner.Character.AddDetailsTo(Description). For a
            // network-synced player Owner.Character can be NULL, and AddDetailsTo is a
            // callvirt on that null Character -> native SIGSEGV (the Mono JIT can elide
            // the null check, so it is NOT a catchable managed exception). Nulling the
            // Owner makes AddLocVars skip AddDetailsTo entirely (only a trivial
            // Add("IsMultiplayer", false) remains). We restore the real Owner before
            // AddOptions because NEventOptionButton._Ready dereferences
            // Owner.RunState.Players for the vote container.
            var savedOwner = _event?.Owner;
            if (_event != null) _event.Owner = null!;
            MainFile.Logger.Info("[CRASHDBG] Render: owner nulled for build");
            var evOptions = new List<EventOption>();
            try
            {
                var ev = _event ?? ModelDb.Get<DemocracyClaimEvent>();
                foreach (var opt in _options)
                {
                    var o = opt;
                    try
                    {
                        MainFile.Logger.Info("[CRASHDBG] Render: building option " + o.Id);
                        var eo = new EventOption(ev, () => HandleChoice(o.Id), o.Id,
                            disableOnChosen: false, isProceed: true, hoverTips: BuildHoverTips(o));
                        evOptions.Add(eo);
                        MainFile.Logger.Info("[CRASHDBG] Render: built option " + o.Id);
                    }
                    catch (Exception e)
                    {
                        MainFile.Logger.Info("[CRASHDBG] Render: option build failed for " + o.Id + ": " + e);
                    }
                }
                try
                {
                    MainFile.Logger.Info("[CRASHDBG] Render: building next button");
                    evOptions.Add(new EventOption(ev, () => { OnNext(); return Task.CompletedTask; },
                        "democracy_next", disableOnChosen: true, isProceed: true, hoverTips: Array.Empty<IHoverTip>()));
                }
                catch (Exception e)
                {
                    MainFile.Logger.Info("[CRASHDBG] Render: next-button build failed: " + e);
                }
                MainFile.Logger.Info("[CRASHDBG] Render: built " + evOptions.Count + " options");
            }
            finally
            {
                if (_event != null) _event.Owner = savedOwner;
                MainFile.Logger.Info("[CRASHDBG] Render: owner restored");
            }

            layout.AddOptions(evOptions);
            MainFile.Logger.Info("[CRASHDBG] Render: AddOptions done");

            // The native buttons resolve their text from the event's loc table, which won't
            // have our dynamic titles/descriptions — stamp our text onto the labels directly.
            RefreshAllLabels();
            RefreshAllVoteIcons();
            MainFile.Logger.Info("[CRASHDBG] Render: labels refreshed");

            MainFile.LogVote(string.Format("Democracy: native event stage shown - {0} ({1} options, {2})",
                _title, _options.Count, _mode));
        }
        catch (Exception e)
        {
            MainFile.Logger.Info("[CRASHDBG] Render top-level error: " + e);
        }
    }

    private static Task HandleChoice(string id)
    {
        if (_done) return Task.CompletedTask;
        if (_mode == Mode.Single)
        {
            _selected.Clear();
            _selected.Add(id);
            RefreshAllLabels();
        }
        else
        {
            if (_selected.Contains(id)) _selected.Remove(id);
            else _selected.Add(id);
            RefreshLabel(id);
        }
        SetPlayerSelection(MultiplayerCoordinator.LocalPlayerId, _selected);
        BroadcastSelection();
        RefreshAllVoteIcons();
        return Task.CompletedTask;
    }

    /// <summary>True if <paramref name="playerId"/> currently has <paramref name="optionId"/>
    /// selected on the current stage (used by the player-icon vote display).</summary>
    public static bool HasSelected(ulong playerId, string optionId) =>
        _selectionByOption.TryGetValue(optionId, out var set) && set.Contains(playerId);

    /// <summary>Replaces a player's selections on the current stage with the given ids.</summary>
    private static void SetPlayerSelection(ulong playerId, List<string> selectedIds)
    {
        foreach (var set in _selectionByOption.Values)
            set.Remove(playerId);
        foreach (var id in selectedIds)
        {
            if (!_selectionByOption.TryGetValue(id, out var set))
            {
                set = new HashSet<ulong>();
                _selectionByOption[id] = set;
            }
            set.Add(playerId);
        }
    }

    /// <summary>Applies a peer's live selection broadcast (icon display only). The
    /// selection is ALWAYS buffered (keyed by player + stage) so an early broadcast that
    /// arrives before Start()/ShowScreen has run isn't lost — RebuildSelectionMap merges
    /// it in when the stage renders. If we're already on that stage we apply it live.</summary>
    public static void ApplyRemoteSelection(ulong playerId, int stage, List<string> selectedIds)
    {
        if (playerId == MultiplayerCoordinator.LocalPlayerId) return;   // own echo
        lock (_remoteSelLock)
        {
            if (!_remoteSelections.TryGetValue(playerId, out var byStage))
            { byStage = new(); _remoteSelections[playerId] = byStage; }
            byStage[stage] = new List<string>(selectedIds);
        }
        if (!_started || _done) return;
        if (stage != VoteManager.CurrentStage) return;
        SetPlayerSelection(playerId, selectedIds);
        RefreshAllVoteIcons();
    }

    /// <summary>Rebuilds _selectionByOption for the current stage from the local player's
    /// selections plus every buffered remote selection for that stage.</summary>
    private static void RebuildSelectionMap()
    {
        _selectionByOption.Clear();
        SetPlayerSelection(MultiplayerCoordinator.LocalPlayerId, _selected);
        int stage = VoteManager.CurrentStage;
        lock (_remoteSelLock)
        {
            foreach (var kv in _remoteSelections)
            {
                if (kv.Value.TryGetValue(stage, out var ids))
                    SetPlayerSelection(kv.Key, ids);
            }
        }
    }

    private static void BroadcastSelection()
    {
        if (_done) return;
        MultiplayerCoordinator.SendSelection(VoteManager.CurrentStage, new List<string>(_selected));
    }

    private static void RefreshAllVoteIcons()
    {
        try
        {
            if (_room == null || !GodotObject.IsInstanceValid(_room)) return;
            var layout = _room.Layout;
            if (layout == null) return;
            foreach (var btn in layout.OptionButtons)
                btn.RefreshVotes();
        }
        catch (Exception e)
        {
            MainFile.LogDebug("Democracy: refresh vote icons error: " + e.Message);
        }
    }

    /// <summary>Hover tips for an option so hovering shows the underlying card/potion/relic.</summary>
    private static IHoverTip[] BuildHoverTips(Option opt)
    {
        var tips = new List<IHoverTip>();
        var e = opt.Entry;
        if (e == null) return tips.ToArray();
        try
        {
            switch (e.Type)
            {
                case RewardPool.PoolEntry.RewardType.CardReward:
                    if (e.Card != null) tips.Add(HoverTipFactory.FromCard(e.Card));
                    break;
                case RewardPool.PoolEntry.RewardType.Relic:
                case RewardPool.PoolEntry.RewardType.BossRelic:
                    if (e.Relic != null) tips.AddRange(HoverTipFactory.FromRelic(e.Relic));
                    break;
                case RewardPool.PoolEntry.RewardType.Potion:
                    if (e.Potion != null) tips.Add(HoverTipFactory.FromPotion(e.Potion));
                    break;
            }
        }
        catch (Exception ex)
        {
            MainFile.LogDebug("Democracy: hover tip error for " + opt.Id + ": " + ex.Message);
        }
        return tips.ToArray();
    }

    private static void RefreshAllLabels()
    {
        if (_room == null || !GodotObject.IsInstanceValid(_room)) return;
        var layout = _room.Layout;
        if (layout == null) return;
        var buttons = layout.OptionButtons.ToList();
        for (int i = 0; i < _options.Count && i < buttons.Count; i++)
            ApplyButtonLabel(buttons[i], _options[i]);
    }

    private static void RefreshLabel(string id)
    {
        if (_room == null || !GodotObject.IsInstanceValid(_room)) return;
        var layout = _room.Layout;
        if (layout == null) return;
        var idx = _options.FindIndex(o => o.Id == id);
        if (idx < 0) return;
        var buttons = layout.OptionButtons.ToList();
        if (idx < buttons.Count)
            ApplyButtonLabel(buttons[idx], _options[idx]);
    }

    private static void ApplyButtonLabel(NEventOptionButton btn, Option opt)
    {
        if (btn == null) return;
        bool sel = _selected.Contains(opt.Id);
        string mark = sel ? "\u2713 " : "";
        if (btn._label != null)
            btn._label.Text = "[gold][b]" + mark + opt.Title + "[/b][/gold]\n" + opt.Description;
        btn.Modulate = sel ? new Color(1.35f, 1.2f, 0.7f) : Colors.White;
    }

    private static void OnNext()
    {
        if (_done) return;
        _done = true;
        var cb = _onNext;
        _onNext = null;
        var result = new List<string>(_selected);
        MainFile.LogVote(string.Format("Democracy: {0} next -> {1} selected", _title, result.Count));
        cb?.Invoke(result);
    }

    private static void SubmitStage(int stage, int goldMode, List<string> rewardIds)
    {
        MainFile.LogVote(string.Format("Democracy: submitting stage {0} — goldMode={1}, {2} reward(s)",
            stage, goldMode, rewardIds.Count));

        // Show the "waiting" overlay BEFORE submitting. If this machine is the host and
        // the last to vote, VoteManager.SubmitStage -> CheckAdvance advances immediately,
        // and AdvanceTo -> ShowStage -> CloseWait will close the overlay we just created.
        // Showing it AFTER SubmitStage (the previous order) re-created the overlay on top
        // of the freshly-advanced next stage; the host's own Advance broadcast is
        // idempotency-guarded (AdvanceTo returns early when the stage already matches), so
        // nothing ever cleared it and "WAITING FOR PLAYERS" stayed stuck on the host.
        ShowWaiting();

        VoteManager.SubmitStage(MultiplayerCoordinator.LocalPlayerId, stage, goldMode, rewardIds);
        MultiplayerCoordinator.SendStage(stage, goldMode, rewardIds);
    }

    private static void ShowWaiting()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;
        if (_waitPanel != null && GodotObject.IsInstanceValid(_waitPanel)) return;

        _waitPanel = new WaitPanel();
        _waitPanel.Configure(
            MainFile.Loc("DemocracyMod.WaitPanel.Title", "WAITING FOR PLAYERS"),
            MainFile.Loc("DemocracyMod.WaitPanel.Subtitle", "Waiting for all players to finish selecting rewards"));
        tree.Root.AddChild(_waitPanel);
    }

    private static void CloseWait()
    {
        if (_waitPanel != null && GodotObject.IsInstanceValid(_waitPanel))
            _waitPanel.QueueFree();
        _waitPanel = null;
    }

    public static void CloseAll()
    {
        if (_room != null && GodotObject.IsInstanceValid(_room))
            _room.QueueFree();
        _room = null;
        _event = null;
        CloseWait();
    }

    public static void Reset()
    {
        CloseAll();
        _started = false;
        _done = false;
        _onNext = null;
        _selected.Clear();
        _options.Clear();
        _selectionByOption.Clear();
        lock (_remoteSelLock) _remoteSelections.Clear();
    }
}
