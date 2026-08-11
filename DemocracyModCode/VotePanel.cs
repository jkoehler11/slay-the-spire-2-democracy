using Godot;
using DemocracyMod.DemocracyModCode;
using DemocracyMod.DemocracyModCode.Networking;

namespace DemocracyMod.DemocracyModCode;

public partial class VotePanel : Control
{
    private Label? _titleLabel;
    private Label? _rewardNameLabel;
    private Label? _timerLabel;
    private VBoxContainer? _playerList;
    private string _activeRewardId = "";
    private readonly Dictionary<ulong, string> _playerNames = new();
    private readonly HashSet<ulong> _votedPlayers = new();
    private bool _isVisible;

    public override void _Ready()
    {
        _titleLabel = GetNodeOrNull<Label>("%TitleLabel");
        _rewardNameLabel = GetNodeOrNull<Label>("%RewardNameLabel");
        _timerLabel = GetNodeOrNull<Label>("%TimerLabel");
        _playerList = GetNodeOrNull<VBoxContainer>("%PlayerList");
        Hide();
    }

    public override void _Process(double delta)
    {
        if (!_isVisible) return;
        var remaining = VoteManager.GetRemainingTime();
        if (remaining >= 0 && _timerLabel != null) _timerLabel.Text = $"Time: {remaining:F0}s";
    }

    public void ShowVote(string rewardId, string rewardName, int timeout)
    {
        _activeRewardId = rewardId;
        _isVisible = true;
        _votedPlayers.Clear();
        if (_titleLabel != null) _titleLabel.Text = "🗳 DEMOCRACY VOTE";
        if (_rewardNameLabel != null) _rewardNameLabel.Text = $"Reward: {rewardName}";
        PopulatePlayerList();
        Show();
    }

    public void HidePanel() { _isVisible = false; Hide(); }

    private void PopulatePlayerList()
    {
        if (_playerList == null) return;
        foreach (var child in _playerList.GetChildren()) child.QueueFree();
        var players = MultiplayerCoordinator.GetPlayers();
        var localId = MultiplayerCoordinator.LocalPlayerId;
        foreach (var player in players)
        {
            var name = _playerNames.GetValueOrDefault(player.NetId, $"Player {player.NetId}");
            var isLocal = player.NetId == localId;
            var hasVoted = _votedPlayers.Contains(player.NetId);
            var text = isLocal ? $"{name} (you)" : name;
            if (hasVoted) text += " ✓";
            var btn = new Button { Text = text, Disabled = hasVoted, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var capturedId = player.NetId;
            btn.Pressed += () => OnPlayerClicked(capturedId);
            _playerList.AddChild(btn);
        }
    }

    private void OnPlayerClicked(ulong targetId)
    {
        if (!_votedPlayers.Contains(MultiplayerCoordinator.LocalPlayerId))
        {
            MultiplayerCoordinator.SendVote(_activeRewardId, targetId);
            _votedPlayers.Add(MultiplayerCoordinator.LocalPlayerId);
            PopulatePlayerList();
        }
    }
}
