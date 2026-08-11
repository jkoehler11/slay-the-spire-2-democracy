using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using BaseLib.Utils;
using BaseLib.Abstracts;
using DemocracyMod.DemocracyModCode.Patches;
using DemocracyMod.DemocracyModCode.Networking;

namespace DemocracyMod.DemocracyModCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "DemocracyMod";
    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Logger.Info("=== Democracy Mod v0.1.0 Initializing ===");
        RewardPool.RegisterSpireFields();
        DemocracyConfig.Load();
        Harmony harmony = new(ModId);
        harmony.PatchAll();
        Logger.Info($"Harmony patches applied: {harmony.GetPatchedMethods().Count()} methods");
        Logger.Info("Democracy Mod initialized. Awaiting multiplayer session.");
    }
}
