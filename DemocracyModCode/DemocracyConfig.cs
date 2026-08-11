using System.Text.Json;
using BaseLib.Config;

namespace DemocracyMod.DemocracyModCode;

public class DemocracyConfig : SimpleModConfig
{
    private static readonly string ConfigPath = "user://mods_config/DemocracyMod.json";

    [ConfigSlider(0, 120)] public static int VoteTimeoutSeconds { get; set; } = 45;
    [ConfigSlider(0, 60)] public static int NegotiationTimeoutSeconds { get; set; } = 30;
    public static bool ShopDemocracy { get; set; } = true;
    public static bool ShopRedistribute { get; set; } = true;
    public static bool SelfishDefault { get; set; } = true;
    [ConfigSlider(0f, 1f, 0.05f)] public static float TieBreakFairness { get; set; } = 0.1f;
    public static bool OpenVoting { get; set; } = false;
    public static bool DeadCanVote { get; set; } = true;

    internal static new void Load()
    {
        try
        {
            if (Godot.FileAccess.FileExists(ConfigPath))
            {
                using var file = Godot.FileAccess.Open(ConfigPath, Godot.FileAccess.ModeFlags.Read);
                var data = JsonSerializer.Deserialize<ConfigData>(file.GetAsText());
                if (data != null)
                {
                    VoteTimeoutSeconds = data.VoteTimeoutSeconds;
                    NegotiationTimeoutSeconds = data.NegotiationTimeoutSeconds;
                    ShopDemocracy = data.ShopDemocracy;
                    ShopRedistribute = data.ShopRedistribute;
                    SelfishDefault = data.SelfishDefault;
                    TieBreakFairness = data.TieBreakFairness;
                    OpenVoting = data.OpenVoting;
                    DeadCanVote = data.DeadCanVote;
                }
            }
        }
        catch (Exception) { }
    }

    private class ConfigData
    {
        public int VoteTimeoutSeconds { get; set; } = 45;
        public int NegotiationTimeoutSeconds { get; set; } = 30;
        public bool ShopDemocracy { get; set; } = true;
        public bool ShopRedistribute { get; set; } = true;
        public bool SelfishDefault { get; set; } = true;
        public float TieBreakFairness { get; set; } = 0.1f;
        public bool OpenVoting { get; set; } = false;
        public bool DeadCanVote { get; set; } = true;
    }
}
