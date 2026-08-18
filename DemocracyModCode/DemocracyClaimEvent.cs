using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace DemocracyMod.DemocracyModCode;

/// <summary>
/// A minimal EventModel used ONLY so the mod can instantiate the game's native
/// event-choice buttons (NEventOptionButton.Create requires an EventModel for its
/// button color / owner / dynamic-var plumbing). It is never driven through the
/// game's EventSynchronizer or shown as a real room event.
/// </summary>
public class DemocracyClaimEvent : EventModel
{
    public static readonly Color ClaimGold = new(0.95f, 0.8f, 0.2f);

    public override Color ButtonColor => ClaimGold;

    protected override IEnumerable<DynamicVar> CanonicalVars => Enumerable.Empty<DynamicVar>();

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
        System.Array.Empty<EventOption>();
}
