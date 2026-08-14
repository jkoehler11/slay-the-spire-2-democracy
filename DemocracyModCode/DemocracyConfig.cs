using System.Text.Json;
using BaseLib.Config;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// How a player's combat rewards are selected, and what happens to rewards nobody
/// claims. BaseLib renders enum-typed config properties as a dropdown.
/// </summary>
public enum RewardSelectionMode
{
    /// <summary>Claim nothing by default. The claim panel starts empty; unclaimed
    /// rewards are discarded and unclaimed gold is even-split. "Use it or lose it."</summary>
    SelectNoRewards,
    /// <summary>Keep what you earned (default). Each player keeps their own card and
    /// exact gold unless someone claims them; unclaimed rewards return to their owner.</summary>
    KeepOwnRewards,
    /// <summary>Grab everything. The claim panel pre-checks every reward and pre-fills
    /// the full gold pool, and cards are auto-selected — a one-click take-all.</summary>
    SelectAllRewards,
}

/// <summary>
/// Democracy mod settings — stored and loaded automatically by BaseLib.
/// All properties appear in Mod Settings UI.
/// </summary>
public class DemocracyConfig : SimpleModConfig
{
    [ConfigSection("Gameplay")]
    [ConfigSlider(0, 120)] public static int VoteTimeoutSeconds { get; set; } = 45;
    public static bool ShopDemocracy { get; set; } = false;
    public static bool AutoPickRewards { get; set; } = false;
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
