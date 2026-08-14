# Democracy — Slay the Spire 2 Multiplayer Mod

> You didn't earn that relic alone. Now convince everyone you deserve it.

**Democracy** turns Slay the Spire 2 multiplayer into a competitive-cooperative
negotiation game. After each combat, every player's rewards are pooled together, and the
group claims what it wants: anyone can vote for anyone (including themselves), and every
card, potion, relic, and coin is physically transferred to its winner.

Built on **BaseLib** + **Harmony**, targeting STS2's built-in deterministic multiplayer
simulation. Works with **2–4 players**.

## How It Works

1. **Auto-pick** — When combat ends, the mod auto-selects each player's combat rewards
   (gold, card, potion, relic) so the vanilla reward screen never lingers. Non-combat
   rewards (events, shops, ancients) are left untouched — you pick those normally.
2. **Pool** — Every reward taken by any player is captured into a shared pool, tagged with
   the player who earned it.
3. **Claim** — Once every player has finished picking, a claim panel opens. Each player
   checks the rewards they want and how much gold they want from the pool, then submits.
4. **Distribute** — The resolution is deterministic and identical on every machine:
   - Uncontested rewards go to their sole claimant.
   - Contested rewards are tie-broken deterministically, weighted toward players who've
     won fewer rewards so far.
   - Unclaimed rewards resolve per the **Reward Selection** setting: **Keep Own Rewards**
     (default) returns them to whoever earned them, **Select All Rewards** pre-checks
     everything for a one-click grab, and **Select No Rewards** discards unclaimed rewards
     and even-splits unclaimed gold.
5. **Transfer** — Cards move via the game's own `CardPileCmd.GiveToAnotherPlayer`;
   potions and relics via discard-then-procure with `PotionCmd`/`RelicCmd`. Because STS2
   multiplayer is a deterministic simulation, both machines run the same commands against
   their own copy of the state — no host authority, no drift.

## Requirements

- Slay the Spire 2 (Steam, Early Access, >= 0.110.0)
- BaseLib 3.4.5+ (subscribe on Steam Workshop #3737335127)
- .NET SDK 9.0+ (to build)

## Build & Install

```
dotnet build -c Debug -p:Sts2Path="/path/to/Slay the Spire 2"
```

Code-only changes need just `dotnet build` — the `.dll`/`.pdb`/`.json` are copied into
`<Sts2Path>/mods/DemocracyMod/` automatically. Asset changes (localization) need `dotnet
publish`, which additionally packs `DemocracyMod/localization/**` into `DemocracyMod.pck`
via MegaDot. Set `<GodotPath>` in `Directory.Build.props` to your MegaDot editor binary
first (matching the game's engine version — see "Localization"). Enable the mod in
Settings → Mod Settings.

## Configuration

Editable in-game via BaseLib `SimpleModConfig`:

| Setting              | Default | Description                                                       |
|----------------------|---------|-------------------------------------------------------------------|
| Auto-Pick Rewards    | OFF     | Auto-select combat rewards so flow skips to the claim UI          |
| Reward Selection     | Keep Own Rewards | Dropdown: Keep Own (default) / Select All / Select No Rewards     |
| Vote Timeout         | 45s     | Auto-submit + resolve if someone never claims                     |
| Tie-Break Fairness   | 0.10    | Weight bonus for players who've won fewer rewards                 |
| Open Voting          | ON      | Reveal each player's full claim (OFF = private)                   |
| Dead Can Vote        | ON      | Dead players may still claim rewards                              |
| Shop Democracy       | OFF     | *(declared, not yet implemented — see below)*                     |
| Shop Redistribute    | OFF     | *(declared, not yet implemented — see below)*                     |

Logging toggles: **Log All Rewards**, **Log All Votes**, **Log Shop Activity** (all ON),
and **Debug Logging** (OFF — enables the verbose DECKVIEW/XFER/OWN-CHECK diagnostics).

## Localization

All user-facing text — the in-game claim/wait panels and every Mod Settings label
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

- **Harmony** postfixes intercept STS2 internals without modifying game code (13 patch
  targets, all verified against the live `sts2.dll`).
- **BaseLib `SpireField`** attaches per-player win counts to `Player` objects.
- **BaseLib `ICustomMessage`** carries claims across the network (`DemocracyClaimMessage`,
  `DemocracyPoolDistributedMessage`).
- **Deterministic resolution** — no `Random`, no `Guid`, no unordered collection iteration,
  no host-local state. Every choice (tie-breaks, gold remainder) is derived from sorted
  player IDs and a stable FNV-1a hash, so host and clients always agree.

## Harmony Patch Targets

| Target class                        | Method                              | Purpose                              |
|-------------------------------------|-------------------------------------|--------------------------------------|
| `RewardsSetSynchronizer`            | `BeginRewardsSet`                   | Auto-pick local combat rewards       |
| `RewardsSetSynchronizer`            | `CompleteRewardsSetIfNecessary`     | Detect all-players-done              |
| `NRewardsScreen`                    | `ShowScreen`                        | Track + dismiss the vanilla screen   |
| `NCardRewardSelectionScreen`        | `RefreshOptions`                    | Auto-select the card (combat only)   |
| `Hook`                              | `AfterRewardTaken`                  | Pool rewards into the shared pool    |
| `Hook`                              | `AfterItemPurchased`                | Log shop purchases                   |
| `CardPileCmd`                       | `Add`                               | Capture granted card models          |
| `PotionCmd`                         | `TryToProcure`                      | Capture granted potion models        |
| `RelicCmd`                          | `Obtain`                            | Capture granted relic models         |
| `CombatRoom`                        | `OnCombatEnded`                     | Reset per-combat state               |
| `NDeckViewScreen`                   | `ShowScreen`                        | Conditional stale-snapshot refresh   |
| `NTopBarDeckButton`                 | `Initialize`                        | Cache the deck-count button          |
| `NTopBarDeckButton`                 | `OnPileContentsChanged`             | Refresh count after transfer         |

## Project Structure

```
slay-the-spire-2-democracy/
    DemocracyModCode/
        MainFile.cs                    - ModInitializer entry point + logging helpers
        DemocracyConfig.cs             - BaseLib SimpleModConfig settings
        RewardPool.cs                  - Pooled rewards, capture, transfer/discard
        VoteManager.cs                 - Claim resolution, deterministic tie-break
        VotePanel.cs                   - Claim UI (built in code)
        WaitPanel.cs                   - "waiting for players" overlay
        Patches/
            AutoPickPatch.cs           - Auto-pick + card auto-select (combat only)
            CombatRewardPatch.cs       - Pooling, grant capture, deck-view/count fixes
            PostCombatPatch.cs         - Completion detection + claim orchestration
            ShopPatch.cs               - Shop purchase logging
        Networking/
            DemocracyMessages.cs       - ICustomMessage types
            MultiplayerCoordinator.cs  - Claim/pool-distributed send + receive
    DemocracyMod.csproj
    DemocracyMod.json                  - Mod manifest (has_pck: true)
    Directory.Build.props / Sts2PathDiscovery.props
    DESIGN.md                          - Game design document
```

`DemocracyMod/localization/` holds the mod's localizable strings (see "Localization"
below); `DemocracyMod/scenes/` is an unused template leftover.

## Known Gaps

1. **Shop voting (`ShopDemocracy`)** — not implemented. Shop purchases still go through
   the vanilla flow; the only shop code logs purchases via `AfterItemPurchased`.
2. **Shop redistribution (`ShopRedistribute`)** — not implemented. Pooled gold is not
   redistributed on shop exit.
3. **Dead-player gold** — `DeadCanVote` blocks a dead player's *claims*, but a dead
   player's pooled gold is still reclaimed and split.

## License

MIT
