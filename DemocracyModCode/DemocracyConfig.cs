using BaseLib.Config;

namespace DemocracyMod.DemocracyModCode;

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
    [ConfigSection("Combat")]
    /// <summary>Show the gold-distribution vote screen. When disabled, gold stays with
    /// whoever earned it.</summary>
    [ConfigHoverTip]
    public static bool ShowGoldScreen { get; set; } = true;
    /// <summary>Show the potion-claim vote screen. When disabled, potions stay with
    /// whoever earned them.</summary>
    [ConfigHoverTip]
    public static bool ShowPotionsScreen { get; set; } = true;
    /// <summary>Show the relic-claim vote screen. When disabled, relics stay with
    /// whoever earned them.</summary>
    [ConfigHoverTip]
    public static bool ShowRelicsScreen { get; set; } = true;
    /// <summary>Show the card-claim vote screen. When disabled, cards stay with
    /// whoever earned them.</summary>
    [ConfigHoverTip]
    public static bool ShowCardsScreen { get; set; } = true;
    /// <summary>Show the post-combat results summary (what everyone received). When
    /// disabled the group advances straight past it after the distribution resolves.</summary>
    [ConfigHoverTip]
    public static bool ShowResultsPanel { get; set; } = true;

    [ConfigSection("Gameplay")]
    [ConfigSlider(0f, 1f, 0.05f)] public static float TieBreakFairness { get; set; } = 0.1f;

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
