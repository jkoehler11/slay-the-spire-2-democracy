using System.Text.Json;
using BaseLib.Config;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// Democracy mod settings — stored and loaded automatically by BaseLib.
/// All properties appear in Mod Settings UI.
/// </summary>
public class DemocracyConfig : SimpleModConfig
{
    [ConfigSection("Gameplay")]
    [ConfigSlider(0, 120)] public static int VoteTimeoutSeconds { get; set; } = 45;
    public static bool ShopDemocracy { get; set; } = true;
    public static bool AutoPickRewards { get; set; } = true;
    public static bool ShopRedistribute { get; set; } = true;
    public static bool SelfishDefault { get; set; } = true;
    [ConfigSlider(0f, 1f, 0.05f)] public static float TieBreakFairness { get; set; } = 0.1f;
    public static bool OpenVoting { get; set; } = false;
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
