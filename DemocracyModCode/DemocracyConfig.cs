using System.Text.Json;
using BaseLib.Config;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// How a player's combat rewards are selected, and what happens to rewards nobody
/// claims. BaseLib renders enum-typed config properties as a dropdown.
/// In the event-style claim flow, this also controls the DEFAULT toggle state of
/// the reward choice buttons (SelectAllRewards pre-checks every button).
/// </summary>
public enum RewardSelectionMode
{
    /// <summary>Claim nothing by default. The claim screens start empty; unclaimed
    /// rewards are discarded. "Use it or lose it."</summary>
    SelectNoRewards,
    /// <summary>Keep what you earned (default). Unclaimed rewards return to whoever
    /// earned them instead of being discarded.</summary>
    KeepOwnRewards,
    /// <summary>Grab everything. Every reward button starts pre-checked for a
    /// one-click take-all.</summary>
    SelectAllRewards,
}

/// <summary>
/// How the shared gold pool is distributed. Each player votes for one of three
/// modes; the mode with the most votes wins (deterministically tie-broken).
/// </summary>
public enum GoldVoteMode
{
    /// <summary>Everyone keeps exactly the gold they earned. No pooling.</summary>
    OriginalAmount,
    /// <summary>Pool all gold and distribute it randomly among all players.</summary>
    Randomized,
    /// <summary>Pool all gold and split it evenly among all players.</summary>
    DistributeEvenly,
}

/// <summary>
/// Democracy mod settings — stored and loaded automatically by BaseLib.
/// All properties appear in Mod Settings UI.
/// </summary>
public class DemocracyConfig : SimpleModConfig
{
    [ConfigSection("Gameplay")]
    public static bool ShopDemocracy { get; set; } = false;
    public static bool ShopRedistribute { get; set; } = false;
    /// <summary>How combat rewards are selected and what happens to unclaimed ones.
    /// BaseLib renders enum-typed config properties as a dropdown.</summary>
    [ConfigHoverTip]
    public static RewardSelectionMode RewardSelection { get; set; } = RewardSelectionMode.KeepOwnRewards;
    [ConfigSlider(0f, 1f, 0.05f)] public static float TieBreakFairness { get; set; } = 0.1f;
    public static bool OpenVoting { get; set; } = true;
    public static bool DeadCanVote { get; set; } = true;

    [ConfigSection("Logging")]
    public static bool DebugLogging { get; set; }
    public static bool LogAllRewards { get; set; } = true;
    public static bool LogAllVotes { get; set; } = true;
    public static bool LogShopActivity { get; set; } = true;

    [ConfigButton("OpenLogFile")]
    public static void OpenLogFile(ModConfig _)
    {
        var path = Godot.OS.GetUserDataDir() + "/logs/godot.log";
        Godot.OS.ShellOpen(path);
    }
}
