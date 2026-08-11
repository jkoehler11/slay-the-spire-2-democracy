# Democracy — Slay the Spire 2 Multiplayer Mod

> You didn't earn that relic alone. Now convince everyone you deserve it.

**Democracy** transforms Slay the Spire 2 multiplayer into a competitive-cooperative
negotiation game. All combat rewards are pooled together, and players vote on
who gets what.

Built on **BaseLib** + **Harmony** using STS2's built-in multiplayer system.
All Harmony patch targets verified against live sts2.dll via monodis.

## How It Works

1. **Pool Rewards** — After combat, all gold, cards, potions, and relics are
   redirected from `Reward.OnSelect()` to the Democracy pool.
2. **Negotiate** — Optional discussion phase (configurable timeout).
3. **Vote** — One vote per player per reward. Simultaneous, hidden until reveal.
4. **Distribute** — Winner gets the reward. Ties broken by weighted random.

## Harmony Patch Targets (Verified)

All targets confirmed against STS2 `sts2.dll` (9.5 MB, build from Jul 31 2026):

| Patch                          | Target Class                           | Method                    | monodis # |
|--------------------------------|----------------------------------------|---------------------------|-----------|
| TriggerDemocracyAfterCombat    | `RewardSynchronizer`                   | `OnCombatEnded(CombatRoom)` | 5510     |
| SuppressVanillaRewardScreen    | `NCombatUi`                            | `ShowRewards(CombatRoom)` | 34971     |
| DemocracyUpdateTicker          | `NCombatUi`                            | `_Process(double)`        | —         |
| OnRunStartReset                | `RunManager`                           | `InitializeShared()`      | —         |
| GoldRewardRedirect             | `GoldReward`                           | `OnSelect()`              | 4065      |
| CardRewardRedirect             | `CardReward`                           | `OnSelect()`              | 4047      |
| PotionRewardRedirect           | `PotionReward`                         | `OnSelect()`              | 4096      |
| RelicRewardRedirect            | `RelicReward`                          | `OnSelect()`              | 4113      |
| SpecialCardRewardRedirect      | `SpecialCardReward`                    | `OnSelect()`              | —         |
| VoteOnPurchase                 | `NMerchantCard`                        | `OnTryPurchase(MerchantInventory)` | 27451 |
| VoteOnCardRemoval              | `NMerchantCardRemoval`                 | `OnTryPurchase(MerchantInventory)` | 27472 |
| TrackShopGoldAfterPurchase     | `Hook`                                 | `AfterItemPurchased(IRunState, Player, MerchantEntry, int32)` | 19692 |

## Key STS2 Internals Discovered

- `Reward` base class: `MegaCrit.Sts2.Core.Rewards.Reward` (Player property, OnSelect)
- `GoldReward` constructors: `(int amount, Player, [opt] bool)` or `(int min, int max, Player, [opt] bool)`
- `CardReward` constructor: `(CardCreationOptions, int cardCount, Player, [opt] PlayerChoiceSynchronizer)`
- `PotionReward`: `Populate()` generates random potion; `get_Potion()` returns result
- `RelicReward`: `Populate()` generates random relic; `get_Relic()` returns result
- `RewardsSet.GenerateRewardsFor(Player, AbstractRoom)` → `List<Reward>`
- `Player.Gold` property (get_Gold/set_Gold confirmed in strings)
- `CombatRoom.ExtraRewards`: `IReadOnlyDictionary<Player, List<Reward>>`
- `MerchantEntry.Cost`: int32; `MerchantInventory.Player`: Player
- STS2 MP: `RunManager.Instance.NetService` → `INetGameService`
- Local player: `PlatformUtil.GetLocalPlayerId(PlatformType)`

## Network Messages

All use BaseLib `ICustomMessage` with `PacketWriter`/`PacketReader`:

| Message                   | Type     | Purpose                                  |
|---------------------------|----------|------------------------------------------|
| DemocracyPoolUpdate       | Broadcast| Pool state to all clients                |
| DemocracyVoteStart        | Broadcast| Start voting on a reward                 |
| DemocracyVoteCast         | Broadcast| Player's vote (relayed by host)          |
| DemocracyVoteResult       | Broadcast| Vote tally + winner                      |
| DemocracyInterest         | Broadcast| Negotiation phase interest               |
| DemocracyPoolDistributed  | Broadcast| All rewards distributed, resume gameplay |

## Configuration

Editable in-game via BaseLib `SimpleModConfig`:

| Setting             | Default | Description                                   |
|---------------------|---------|-----------------------------------------------|
| Vote Timeout        | 45s     | Time to vote per reward (0 = no timeout)      |
| Negotiation Timeout | 30s     | Discussion time before voting (0 = skip)      |
| Shop Democracy      | ON      | Pool gold in shops, vote on purchases         |
| Shop Redistribute   | ON      | Redistribute leftover shop gold equally       |
| Selfish Default     | ON      | Auto-vote for self on timeout                 |
| Tie-Break Fairness  | 0.10    | Weight bonus for players with fewer wins      |
| Open Voting         | OFF     | Show live vote counts (OFF = blind)           |
| Dead Can Vote       | ON      | Dead players can still vote                   |

## Project Structure

```
slay-the-spire-democracy/
├── DemocracyModCode/
│   ├── MainFile.cs                    — [ModInitializer] entry point
│   ├── DemocracyConfig.cs             — BaseLib SimpleModConfig
│   ├── RewardPool.cs                  — Pooled rewards + SpireFields
│   ├── VoteManager.cs                 — Voting state machine
│   ├── VotePanel.cs                   — Godot vote UI controller
│   ├── Patches/
│   │   ├── CombatRewardPatch.cs       — OnSelect interceptors (verified targets)
│   │   ├── PostCombatPatch.cs         — OnCombatEnded + ShowRewards (verified)
│   │   └── ShopPatch.cs               — OnTryPurchase + AfterItemPurchased (verified)
│   └── Networking/
│       ├── DemocracyMessages.cs       — 6 ICustomMessage types
│       └── MultiplayerCoordinator.cs  — Host/client orchestration
├── DemocracyMod/
│   ├── scenes/VotePanel.tscn          — Godot UI scene
│   └── localization/eng/ui.json
└── [build files] — .csproj, .json, .godot, .props
```

## Remaining Work

1. **Wire reward collection** — `PostCombatPatch.CollectAndPoolRewards()` needs to
   iterate `CombatRoom.ExtraRewards` and all players' RewardsSets. Exact access path
   requires deeper decompile of `NCombatUi.ShowRewards` to see where it reads rewards from.

2. **Test in multiplayer** — verify messages route through `INetGameService`, synced
   between host and clients.

3. **Shop leave detection** — `ShopPatch.OnShopLeave()` needs a hook for merchant room
   exit. Patch `MerchantRoom.OnRoomExit` or monitor room transitions.

## Requirements

- Slay the Spire 2 (Steam, Early Access, >= 0.107.0)
- BaseLib (Steam Workshop #3737335127)
- .NET SDK 9.0+

## License

MIT
