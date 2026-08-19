using DemocracyMod.DemocracyModCode.Networking;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// The EFFECTIVE gameplay config. In multiplayer everyone follows the HOST's settings:
/// the host reads its own live DemocracyConfig; clients read a snapshot broadcast by the
/// host at run launch (DemocracyConfigMessage) and applied here. This replaces direct
/// reads of DemocracyConfig.* for every value that shapes the synchronized reward flow,
/// so a host and client with different local settings no longer diverge. Logging flags
/// (LogAllRewards / LogAllVotes / LogShopActivity / DebugLogging) stay LOCAL — they only
/// control per-machine verbosity, never gameplay.
/// </summary>
public static class HostConfig
{
    private static bool _received;
    private static bool _showGoldScreen = true;
    private static bool _showPotionsScreen = true;
    private static bool _showRelicsScreen = true;
    private static bool _showCardsScreen = true;
    private static bool _showResultsPanel = true;
    private static bool _enableAncients = true;
    private static float _tieBreakFairness = 0.1f;

    /// <summary>True once this machine has an authoritative host snapshot (host: after
    /// CaptureHostValues; client: after the first config broadcast arrives).</summary>
    public static bool Received => _received;

    public static bool ShowGoldScreen => Effective(DemocracyConfig.ShowGoldScreen, _showGoldScreen);
    public static bool ShowPotionsScreen => Effective(DemocracyConfig.ShowPotionsScreen, _showPotionsScreen);
    public static bool ShowRelicsScreen => Effective(DemocracyConfig.ShowRelicsScreen, _showRelicsScreen);
    public static bool ShowCardsScreen => Effective(DemocracyConfig.ShowCardsScreen, _showCardsScreen);
    public static bool ShowResultsPanel => Effective(DemocracyConfig.ShowResultsPanel, _showResultsPanel);
    public static bool EnableAncients => Effective(DemocracyConfig.EnableAncients, _enableAncients);
    public static float TieBreakFairness => Effective(DemocracyConfig.TieBreakFairness, _tieBreakFairness);

    /// <summary>The host always uses its own live settings; a client uses the received
    /// host snapshot once available, else its local settings (pre-sync fallback).</summary>
    private static bool Effective(bool local, bool remote) =>
        MultiplayerCoordinator.IsHost || !_received ? local : remote;

    private static float Effective(float local, float remote) =>
        MultiplayerCoordinator.IsHost || !_received ? local : remote;

    /// <summary>Snapshot the host's current settings into the broadcast payload.</summary>
    public static void CaptureHostValues()
    {
        _showGoldScreen = DemocracyConfig.ShowGoldScreen;
        _showPotionsScreen = DemocracyConfig.ShowPotionsScreen;
        _showRelicsScreen = DemocracyConfig.ShowRelicsScreen;
        _showCardsScreen = DemocracyConfig.ShowCardsScreen;
        _showResultsPanel = DemocracyConfig.ShowResultsPanel;
        _enableAncients = DemocracyConfig.EnableAncients;
        _tieBreakFairness = DemocracyConfig.TieBreakFairness;
        _received = true;
    }

    /// <summary>Apply a host's broadcast config on a client.</summary>
    public static void ApplyRemote(bool showGold, bool showPotions, bool showRelics, bool showCards,
        bool showResults, bool enableAncients, float tieBreakFairness)
    {
        _showGoldScreen = showGold;
        _showPotionsScreen = showPotions;
        _showRelicsScreen = showRelics;
        _showCardsScreen = showCards;
        _showResultsPanel = showResults;
        _enableAncients = enableAncients;
        _tieBreakFairness = tieBreakFairness;
        _received = true;
    }

    /// <summary>Forget any received snapshot (called at run launch before re-sync).</summary>
    public static void Reset() => _received = false;
}
