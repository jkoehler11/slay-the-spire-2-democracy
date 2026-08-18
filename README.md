# Democracy — Slay the Spire 2 Multiplayer Mod

> You didn't earn that relic alone. Now convince everyone you deserve it.

**Democracy** turns Slay the Spire 2 multiplayer into a competitive-cooperative
negotiation game. After each combat, every player picks their own loot first, then the
group's rewards are pooled and voted on together: anyone can claim any reward, and every
card, potion, relic, and coin is physically transferred to its winner.

Built on **BaseLib** + **Harmony**, targeting STS2's built-in deterministic multiplayer
simulation. Works with **2–4 players**.

## How It Works

1. **Pick your own loot** — After combat, each player selects their own rewards on the
   vanilla loot screen, exactly as in an unmodded run: the normal "pick 1 of 3" for cards,
   and the usual claims for potions, relics, and gold. Nothing is auto-picked and nothing
   is hidden. The vanilla **Proceed** button is held back while there is pooled loot so
   nobody can skip ahead before the vote finishes.
2. **Pool & vote** — Once every player has finished picking, the rewards are pooled and
   tagged with who earned them, and the group walks through up to four pages, one reward
   category at a time. Every page uses the game's native event-choice buttons (large title
   + description buttons), the group **only advances when every player has submitted the
   current page**, and **any page with nothing pooled is skipped automatically**:
   - **Gold.** A three-option vote: **Original amount** (keep what you earned),
     **Randomized** (pool and deal randomly), or **Distribute evenly** (pool and split
     evenly). Plurality wins; ties break deterministically.
   - **Potions.** Every pooled potion. Toggle **0 to N**, then **Next**.
   - **Relics.** Every pooled relic (including boss relics). Toggle **0 to N**, then
     **Next**.
   - **Cards.** The card(s) each player actually selected — a "pick 1 of 3" contributes
     exactly one card (the chosen one), not all three options. Toggle **0 to N**, then
     **Finish**.
   - Non-combat rewards (events, shops, ancients) are never pooled — you pick those
     normally.
3. **Distribute (host-authoritative)** — After the cards page, the host (the first player
   in the run) alone computes the outcome, applies the transfers, and broadcasts the
   decision. Clients apply the identical result rather than resolving on their own, so
   every machine ends up in the same state.
   - Uncontested rewards go to their sole claimant.
   - Contested rewards are tie-broken deterministically (a stable FNV-1a hash over sorted
     player IDs, weighted toward players who have won fewer rewards so far).
   - Unclaimed rewards resolve per **Reward Selection**: **Keep Own Rewards** (default)
     returns them to whoever earned them, **Select All Rewards** pre-checks every button
     for a one-click grab, and **Select No Rewards** discards unclaimed rewards.
4. **Transfer** — Cards move via the game's own `CardPileCmd.GiveToAnotherPlayer`; potions
   and relics via discard-then-procure with `PotionCmd`/`RelicCmd`. These are the game's
   own replicated commands, so the deterministic simulation applies them identically on
   every machine.
5. **Results** — After distribution, a summary panel shows what each player received
   (relics, cards, potions, gold), with a Continue button. On a normal fight Continue
   reopens the map for the whole group; on a boss/victory fight it drives the group's
   act-change transition.

## Requirements

- Slay the Spire 2 (Steam, Early Access, >= 0.110.0)
- BaseLib 3.4.5+ (subscribe on Steam Workshop #3737335127)
- .NET SDK 9.0+ (to build)

## Build & Install

Code-only changes need just:

```
dotnet build
```

`Sts2PathDiscovery.props` auto-finds your Slay the Spire 2 install — Steam registry on
Windows, `~/.local/share/Steam` on Linux, `~/Library/Application Support/Steam` on macOS —
and copies the built `.dll`/`.pdb`/`.json` into `<Sts2Path>/mods/DemocracyMod/`
automatically. Enable the mod in Settings → Mod Settings.

Asset changes (localization) additionally need the `.pck` regenerated:

```
dotnet publish
```

Publish packs `DemocracyMod/localization/**` into `DemocracyMod.pck` using the MegaDot
editor (which must match the game's engine version — see "Localization"). `dotnet build`
does **not** need a Godot editor installed; only `dotnet publish` does.

If the auto-detected paths don't match your machine, override them without editing any
committed file (so your checkout stays clean for everyone else):

- `dotnet build -p:Sts2Path="/abs/path/to/Slay the Spire 2"`
- `dotnet publish -p:GodotPath="/abs/path/to/MegaDot" -p:Sts2Path="/abs/path/to/Slay the Spire 2"`
- or drop a gitignored `local.props` next to `Directory.Build.props`:

  ```xml
  <Project><PropertyGroup>
    <GodotPath>/abs/path/to/MegaDot</GodotPath>
    <Sts2Path>/abs/path/to/Slay the Spire 2</Sts2Path>
  </PropertyGroup></Project>
  ```

`Directory.Build.props` ships per-OS MegaDot defaults (Windows `.exe`, Linux `.x86_64`,
macOS `.app/Contents/MacOS/Godot`), so the only thing a new contributor has to set is where
they unzipped the editor.

## Configuration

Editable in-game via BaseLib `SimpleModConfig`:

| Setting              | Default          | Description                                                       |
|----------------------|------------------|-------------------------------------------------------------------|
| Reward Selection     | Keep Own Rewards | Dropdown: Keep Own (default) / Select All / Select No Rewards     |
| Tie-Break Fairness   | 0.10             | Weight bonus for players who have won fewer rewards               |
| Dead Can Vote        | ON               | Dead players may still vote for rewards                           |
| Show Results Summary | ON               | Show what everyone received after each combat (OFF skips it)      |

Logging toggles: **Log All Rewards**, **Log All Votes**, **Log Shop Activity** (all ON),
and **Debug Logging** (OFF — enables the verbose DECKVIEW/XFER diagnostics).

## Localization

All user-facing text — the in-game choice/wait/results screens and every Mod Settings label
(including the dropdown option names) — is localizable, and English is the fallback if a
key is missing. Strings live in `DemocracyMod/localization/<lang>/` as flat JSON, packed
into `DemocracyMod.pck` at publish time:

- `settings_ui.json` — Mod Settings labels + dropdown option labels. Keys are
  `DEMOCRACYMOD-<PROPERTY_NAME>.title` and `DEMOCRACYMOD-REWARD_SELECTION.<EnumValue>`
  (BaseLib looks these up automatically via the mod prefix + slugified property name).
- `ui.json` — in-game panel text, looked up by the mod as `LocString("ui", key)`. The
  `MainFile.Loc(key, fallback)` helper wraps this so missing keys degrade gracefully.

To add a language: copy `eng/` to a new ISO 639-2 code (e.g. `deu/`, `jpn/`), translate
the values, and republish. The game loads `res://DemocracyMod/localization/<lang>/<table>.json`
automatically; `.pck` compatibility requires the MegaDot editor version to match the game's
engine (currently `4.5.1.m.14` — check the game binary's `--version` after an update).

## Architecture

- **Harmony** postfixes intercept STS2 internals without modifying game code (17 patch
  targets, all verified against the live `sts2.dll`).
- **BaseLib `SpireField`** attaches per-player win counts to `Player` objects.
- **BaseLib `ICustomMessage`** carries the per-stage votes, the host's advance signal, and
  the host's final decision across the network (`DemocracyStageMessage`,
  `DemocracyAdvanceMessage`, `DemocracyResolvedMessage`).
- **Host-authoritative resolution** — the host (first player in the run) is the single
  resolver. It collects each player's per-stage votes, advances the group when every
  player has voted, and (after the cards stage) picks winners and broadcasts them; clients
  apply the same transfers rather than resolving independently. The tie-breaks stay fully
  deterministic — sorted player IDs + a stable FNV-1a hash, no `Random`, no `Guid`, no
  unordered collection iteration — so the host's decision is reproducible and verifiable.
- **Native Godot UI** — reward choices use the game's own `NEventOptionButton` (the large
  event-choice buttons) hosted in a synthetic `NEventRoom`; a patched `NEventRoom
  .get_Instance` routes the native buttons' click handler to the mod's toggle/submit
  callback without a real event room. The wait and results screens are small code-built
  overlays.

## Harmony Patch Targets

| Target class                        | Method                              | Purpose                                    |
|-------------------------------------|-------------------------------------|--------------------------------------------|
| `RewardsSetSynchronizer`            | `BeginRewardsSet`                   | Gate reward-grant capture to the reward phase |
| `RewardsSetSynchronizer`            | `CompleteRewardsSetIfNecessary`     | Detect all-players-done                    |
| `NRewardsScreen`                    | `TryEnableProceedButton`            | Hold the vanilla Proceed while loot is pooled |
| `Hook`                              | `AfterRewardTaken`                  | Pool rewards (selected card only)          |
| `Hook`                              | `AfterItemPurchased`                | Log shop purchases                         |
| `CardPileCmd`                       | `Add`                               | Capture granted card models                |
| `PotionCmd`                         | `TryToProcure`                      | Capture granted potion models              |
| `RelicCmd`                          | `Obtain`                            | Capture granted relic models               |
| `CombatRoom`                        | `OnCombatEnded`                     | Reset per-combat state                     |
| `NEventRoom`                        | `get_Instance`                      | Route native buttons without a room        |
| `NEventRoom`                        | `SetOptions`                        | Crash-diagnosis probe                      |
| `NEventLayout`                      | `OnSetupComplete`                   | Crash-diagnosis probe                      |
| `EventModel`                        | `CreateInitialPortrait`             | Skip portrait for the synthetic claim event |
| `EventModel`                        | `CreateInitialPhobiaModePortrait`   | Skip portrait for the synthetic claim event |
| `NDeckViewScreen`                   | `ShowScreen`                        | Conditional stale-snapshot refresh         |
| `NTopBarDeckButton`                 | `Initialize`                        | Cache the deck-count button                |
| `NTopBarDeckButton`                 | `OnPileContentsChanged`             | Refresh count after transfer               |

## Project Structure

```
slay-the-spire-2-democracy/
    DemocracyModCode/
        MainFile.cs                    - ModInitializer entry point + logging helpers
        DemocracyConfig.cs             - BaseLib SimpleModConfig settings
        RewardPool.cs                  - Pooled rewards, capture, transfer/discard
        VoteManager.cs                 - Stage coordinator + host-authoritative resolution
        DemocracyFlow.cs               - Drives the four synchronized claim pages
        DemocracyClaimEvent.cs         - Minimal EventModel backing the native buttons
        WaitPanel.cs                   - "waiting for players" overlay
        ResultsPanel.cs                - Post-distribution results summary
        Patches/
            RewardPhasePatch.cs        - Reward-grant capture gate (BeginRewardsSet)
            CombatRewardPatch.cs       - Pooling, grant capture, deck fixes
            PostCombatPatch.cs         - Completion detection + flow orchestration
            NativeUiPatch.cs           - Route native buttons + hold the vanilla Proceed
            ShopPatch.cs               - Shop purchase logging
        Networking/
            DemocracyMessages.cs       - ICustomMessage types (stage + advance + resolution)
            MultiplayerCoordinator.cs  - Stage/advance/resolution send + receive, host detection
    DemocracyMod.csproj
    DemocracyMod.json                  - Mod manifest (has_pck: true)
    Directory.Build.props / Sts2PathDiscovery.props
    DESIGN.md                          - Game design document
```

`DemocracyMod/localization/` holds the mod's localizable strings (see "Localization").

## Known Gaps

1. **Shop voting** — not implemented. Shop purchases still go through the vanilla flow;
   the only shop code logs purchases via `AfterItemPurchased`.
2. **Shop redistribution** — not implemented. Pooled gold is not redistributed on shop
   exit.
3. **Dead-player gold** — `DeadCanVote` blocks a dead player's *votes*, but a dead
   player's pooled gold is still reclaimed and split.
4. **No vote timeout** — the claim screens stay open until every player submits, so if a
   player goes AFK mid-flow the group waits indefinitely (the host won't force-resolve).
   Intended; a host-side force-resolve timeout can be added if it becomes a pain point.

## License

MIT
