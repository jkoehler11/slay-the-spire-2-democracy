using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Localization;
using BaseLib.Utils;
using BaseLib.Abstracts;
using BaseLib.Config;
using DemocracyMod.DemocracyModCode.Patches;

namespace DemocracyMod.DemocracyModCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "DemocracyMod";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    // Category-gated logging. Route all gameplay/diagnostic logs through these so the
    // "Logging" config section actually controls verbosity (DebugLogging defaults OFF).
    public static void LogReward(string msg) { if (DemocracyConfig.LogAllRewards) Logger.Info(msg); }
    public static void LogVote(string msg) { if (DemocracyConfig.LogAllVotes) Logger.Info(msg); }
    public static void LogShop(string msg) { if (DemocracyConfig.LogShopActivity) Logger.Info(msg); }
    public static void LogDebug(string msg) { if (DemocracyConfig.DebugLogging) Logger.Info(msg); }

    /// <summary>Localized UI string from the mod's "ui" loc table, with an English
    /// fallback so the UI still works even when the localization .pck isn't loaded.</summary>
    public static string Loc(string key, string fallback) =>
        LocString.GetIfExists("ui", key)?.GetRawText() ?? fallback;

    public static void Initialize()
    {
        Logger.Info("=== Democracy Mod v0.1.0 Initializing ===");

        // Register the mod's in-game UI localization table (shipped in DemocracyMod.pck).
        CustomLocTableManager.Register("ui");

        ModConfigRegistry.Register(ModId, new DemocracyConfig());
        
        Harmony harmony = new(ModId);
        harmony.PatchAll();
        var patched = harmony.GetPatchedMethods().ToList();
        Logger.Info(string.Format("Harmony patches applied: {0} methods", patched.Count));
        foreach (var m in patched)
            Logger.Info(string.Format("  patched: {0}.{1}", m.DeclaringType?.FullName, m.Name));

        CombatRewardPatch.IsDemocracyActive = true;
        Logger.Info("Democracy pooling active. Vote triggers when 2+ players contribute.");

        Logger.Info("Democracy Mod initialized. Awaiting multiplayer session.");
    }
}
