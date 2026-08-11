# Democracy — Slay the Spire 2 Multiplayer Mod

> You didn't earn that relic alone. Now convince everyone you deserve it.

**Democracy** transforms Slay the Spire 2 multiplayer into a competitive-cooperative
negotiation game. All combat rewards are pooled together, and players vote on
who gets what. Anyone can vote for anyone (including themselves). Plurality wins.

Built on **BaseLib** + **Harmony** using STS2's built-in multiplayer system.
Clean build: 0 warnings, 0 errors against live `sts2.dll` (Jul 31 2026 build).

## How It Works

1. **Pool Rewards** — After combat, all gold, cards, potions, and relics from
   `CombatRoom.ExtraRewards` are collected into the shared Democracy pool.
   Individual reward screens (`Reward.OnSelect()`) are intercepted so nothing
   reaches a player directly.
2. **Negotiate** — Optional discussion phase (configurable timeout). Players
   express interest in specific rewards.
3. **Vote** — One vote per player per reward. Simultaneous, hidden until reveal.
   Timeout auto-casts remaining votes (selfish or random, configurable).
4. **Distribute** — Plurality wins. Ties broken by weighted random selection
   favoring players who've received fewer rewards so far.
5. **Shop Democracy** — All players' gold is pooled on shop entry. Purchases
   and card removals are routed through a vote. Remaining gold is redistributed
   equally on exit.

## Requirements

- Slay the Spire 2 (Steam, Early Access, >= 0.107.0)
- BaseLib (subscribe on Steam Workshop #3737335127)
- .NET SDK 9.0+

## Build & Install

```
dotnet build
```

Output auto-copies to STS2 mods/DemocracyMod/. Enable in Settings -> Mod Settings.

## Harmony Patch Targets

All targets use string-based `[HarmonyPatch(typeof(X), "Method")]` because
STS2's game methods are `internal`/`protected` (not accessible to `nameof()`).
Every target verified against live `sts2.dll` via monodis:

| Patch                          | Target Class             | Method                    | monodis # |
|--------------------------------|--------------------------|---------------------------|-----------|
| TriggerDemocracyAfterCombat    | `RewardSynchronizer`     | `OnCombatEnded`           | 5510      |
| SuppressVanillaRewardScreen    | `NCombatUi`              | `ShowRewards`             | 34971     |
| DemocracyUpdateTicker          | `NCombatUi`              | `_Process`                | -         |
| OnRunStartReset                | `RunManager`             | `InitializeShared`        | 3269      |
| GoldRewardRedirect             | `GoldReward`             | `OnSelect`                | 4065      |
| CardRewardRedirect             | `CardReward`             | `OnSelect`                | 4047      |
| PotionRewardRedirect           | `PotionReward`           | `OnSelect`                | 4096      |
| RelicRewardRedirect            | `RelicReward`            | `OnSelect`                | 4113      |
| SpecialCardRewardRedirect      | `SpecialCardReward`      | `OnSelect`                | -         |
| VoteOnPurchase                 | `NMerchantCard`          | `OnTryPurchase`           | 27451     |
| VoteOnCardRemoval              | `NMerchantCardRemoval`   | `OnTryPurchase`           | 27472     |
| TrackShopGoldAfterPurchase     | `Hook`                   | `AfterItemPurchased`      | 19692     |
| DetectShopLeave                | `RewardsSetSynchronizer` | `BeforeLeavingRoom`       | 5489      |

## Key STS2 Internals

Learned through decompilation with `monodis`:

- `Reward` base class: `Player`, `RewardType`, `IsPopulated`, `OnSelect()`
- `GoldReward`: `Amount` (int32), constructor `(int, Player, [opt] bool)`
- `CardReward`: `OptionCount`, `Cards`, constructor `(CardCreationOptions, int, Player)`
- `PotionReward`: `Populate()` generates random; `Potion` property
- `RelicReward`: `Populate()` generates random; `Relic` property
- `CombatRoom.ExtraRewards`: `IReadOnlyDictionary<Player, List<Reward>>`
- `RewardsSet.GenerateRewardsFor(Player, AbstractRoom)` -> `List<Reward>`
- `Player.Gold` (get_Gold/set_Gold)
- `MerchantEntry.Cost`: int32; `MerchantInventory.Player`: Player
- `NCombatRoom.Instance` singleton; `NCombatUi` reached via `.Ui`
- `RunManager.Instance.NetService` is `INetGameService` for MP messages
- `RunManager.Instance.RewardSynchronizer` handles reward sync
- Local player: `PlatformUtil.GetLocalPlayerId(PlatformType)`
- Models use `.Title` (returns `LocString`), not `.Name`

## Network Messages

All use BaseLib `ICustomMessage` with binary `PacketWriter`/`PacketReader`:

| Message                   | Direction      | Purpose                                  |
|---------------------------|----------------|------------------------------------------|
| `DemocracyPoolUpdate`     | Host -> All    | Current pool contents                    |
| `DemocracyVoteStart`      | Host -> All    | Start voting on a reward                 |
| `DemocracyVoteCast`       | Client -> Host | Player's vote                            |
| `DemocracyVoteResult`     | Host -> All    | Vote tally + winner                      |
| `DemocracyInterest`       | Any -> All     | Negotiation phase interest               |
| `DemocracyPoolDistributed`| Host -> All    | All rewards resolved, resume gameplay    |

Messages are auto-discovered by BaseLib and registered at runtime.

## Configuration

Editable in-game via BaseLib `SimpleModConfig`:

| Setting              | Default | Description                                   |
|----------------------|---------|-----------------------------------------------|
| Vote Timeout         | 45s     | Time to vote per reward (0 = no timeout)      |
| Negotiation Timeout  | 30s     | Discussion time before voting (0 = skip)      |
| Shop Democracy       | ON      | Pool gold in shops, vote on purchases         |
| Shop Redistribute    | ON      | Redistribute leftover shop gold equally       |
| Selfish Default      | ON      | Auto-vote for self on timeout                 |
| Tie-Break Fairness   | 0.10    | Weight bonus for players with fewer wins      |
| Open Voting          | OFF     | Show live vote counts (OFF = blind)           |
| Dead Can Vote        | ON      | Dead players can still vote                   |

## Project Structure

```
slay-the-spire-2-democracy/
    DemocracyModCode/                 (813 lines C#)
        MainFile.cs                    - ModInitializer entry point
        DemocracyConfig.cs             - BaseLib SimpleModConfig (8 settings)
        RewardPool.cs                  - Pooled rewards + SpireField
        VoteManager.cs                 - Voting state machine + timer + tie-break
        VotePanel.cs                   - Godot vote UI
        Patches/
            CombatRewardPatch.cs       - 5 OnSelect interceptors
            PostCombatPatch.cs         - OnCombatEnded + ShowRewards + InitializeShared
            ShopPatch.cs               - OnTryPurchase + AfterItemPurchased + BeforeLeavingRoom
        Networking/
            DemocracyMessages.cs       - 6 ICustomMessage types
            MultiplayerCoordinator.cs  - Host/client orchestration + player tracking
    DemocracyMod/
        scenes/VotePanel.tscn          - Godot UI scene
        localization/eng/ui.json       - Localized strings
        images/                        - Mod badge
    DemocracyMod.csproj                - .NET 9 + Godot SDK
    DemocracyMod.json                  - Mod manifest
    Directory.Build.props              - Godot/STS2 path config
    DESIGN.md                          - Full game design document
    README.md                          - This file
```

## Architecture

- **Harmony** `[HarmonyPrefix]` / `[HarmonyPostfix]` with string-based method names
  patches internal STS2 methods without modifying game code.
- **BaseLib `SpireField<T>`** attaches per-player Democracy state (win counts)
  to `Player` objects without base class changes.
- **BaseLib `ICustomMessage`** with binary serialization for network sync.
  Auto-discovered and registered by BaseLib at runtime.
- **BaseLib `SimpleModConfig`** with `[ConfigSlider]` attributes for in-game settings UI.

## Remaining Work

1. **Test in multiplayer** — verify messages route through STS2's `INetGameService`,
   synced between host and clients. Reward collection via `CombatRoom.ExtraRewards`
   and shop leave detection via `BeforeLeavingRoom` are wired but untested live.

2. **Godot UI polish** — `VotePanel.tscn` exists as a scene template. It needs
   instantiation during the vote phase and remaining-time display wiring.

3. **Shop purchase votes** — `VoteOnPurchase` currently blocks all purchases.
   The purchase vote flow (propose item, quick vote, deduct from pooled gold)
   is not yet implemented.

## License

MIT
