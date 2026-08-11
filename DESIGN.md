# Democracy Mod — Design Document (STS2 Edition)

## Overview

**Democracy** is a multiplayer-only mod for Slay the Spire 2. All players
pool their post-combat rewards into a shared treasury. Instead of each player
getting their own reward screen, every reward — gold, cards, potions, relics —
goes into a common pool. Players then vote on who receives each reward.

## Core Philosophy

> You didn't earn that relic alone — your team carried you through the fight.
> Now convince them you deserve it.

The mod transforms Slay the Spire 2 from a solo deckbuilder with parallel play
into a competitive-cooperative negotiation game.

## Platform

- **Game:** Slay the Spire 2 (Early Access, build 0.107+)
- **Language:** C# / .NET 9.0
- **Engine:** Godot 4.5.1 (MegaDot)
- **Mod Framework:** BaseLib + Harmony
- **Networking:** STS2 built-in multiplayer (`MegaCrit.Sts2.Core.Multiplayer`)

## Mechanics

### 1. Reward Pooling

After every combat (normal, elite, boss), all rewards that would normally go
to each individual player are instead deposited into the **Democracy Pool**.

| Reward Type         | Pool Behavior                                              |
|---------------------|------------------------------------------------------------|
| Gold                | All gold pooled into a single treasury, distributed by vote|
| Card Rewards        | Each "pick 1 of 3" becomes a pool entry; all options shown |
| Potions             | All potion drops pooled; players vote per potion           |
| Relics (combat)     | Pooled; one vote per relic                                 |
| Boss Relics         | Pooled; major negotiation moment                           |
| Neow Blessings      | Optional: democratic blessing selection at game start      |

### 2. Voting System

- **One player, one vote per reward**
- **Simultaneous voting** — hidden until all votes cast
- **Plurality wins** — player with most votes gets the reward
- **Tie-breaking** — weighted random among tied players. Win-count deficit
  gives a weight bonus (configurable via `tie_break_fairness`)
- **Vote timeout** — default 45s. Auto-votes for self on expiry (configurable)

### 3. Negotiation Phase

Optional pre-vote discussion phase (default 30s):
- All pooled rewards visible to all players
- Players can click rewards to "express interest"
- Configurable or skippable (set `negotiation_timeout` to 0)

### 4. Shop Democracy

When entering a shop with `shop_democracy=true`:
- All players' gold is pooled into a Shop Treasury
- Any player can propose a purchase → goes to vote
- Winning proposal: item bought with pooled gold
- On leaving: remaining gold redistributed equally (configurable)

## Architecture

### Harmony Patches

All game interception uses Harmony `[HarmonyPrefix]` / `[HarmonyPostfix]`:

| Patch                    | Target (TBD after decompile)       | Purpose                          |
|--------------------------|-------------------------------------|----------------------------------|
| GoldRewardRedirect       | MonsterRoom.AddGoldReward           | Redirect gold → pool             |
| CardRewardRedirect       | CombatManager.AddCardReward         | Redirect cards → pool            |
| PotionRewardRedirect     | CombatManager.AddPotionReward       | Redirect potions → pool          |
| RelicRewardRedirect      | MonsterRoomElite.AddRelicReward     | Redirect relics → pool           |
| TriggerDemocracyAfterCombat | CombatManager.OnBattleEnd        | Start vote flow after combat     |
| SuppressVanillaRewardScreen | RewardManager.ShowRewards        | Hide vanilla reward screen       |
| PoolGoldOnShopEnter      | ShopScreen.Init                    | Pool gold on shop entry          |
| VoteOnPurchase           | ShopScreen.PurchaseCard            | Route purchases through voting   |

### BaseLib SpireFields

- `PlayerWinCount` (int) — how many Democracy rewards a player has won
- `IsPoolHost` (bool) — marks the player coordinating the vote flow

### Network Protocol

All messages use STS2's `CustomMessage` system (JSON payloads):

```
POOL_UPDATE          — Host broadcasts current pool state
VOTE_START           — Host starts voting on a reward
VOTE_CAST            — Client casts a vote (→ Host)
VOTE_RESULT          — Host broadcasts resolution + tally
INTEREST             — Player expresses interest in a reward
NEGOTIATION_START/END
POOL_DISTRIBUTED     — All entries resolved
POOL_SYNC_REQUEST    — New client requests full pool state
```

## Configuration

Via BaseLib `SimpleModConfig` (editable in-game):

```json
{
  "vote_timeout_seconds": 45,
  "negotiation_timeout_seconds": 30,
  "shop_democracy": true,
  "shop_redistribute": true,
  "selfish_default": true,
  "tie_break_fairness": 0.1,
  "open_voting": false,
  "dead_can_vote": true
}
```

## Roadmap

| Phase | Feature                                    | Status |
|-------|--------------------------------------------|--------|
| 1     | Project setup (C# / .NET 9 / BaseLib)      | DONE   |
| 2     | Reward pooling + VoteManager + Config      | DONE   |
| 3     | Harmony patches (targets TBD)              | STUB   |
| 4     | Network protocol + MultiplayerCoordinator  | DONE   |
| 5     | Shop Democracy                             | DONE   |
| 6     | In-game vote UI (Godot scene)              | TODO   |
| 7     | Decompile STS2 → pin exact Harmony targets | TODO   |
| 8     | Test in multiplayer session                | TODO   |

## Prior Version

The original mod (v0.0) was written in Java targeting STS1 / ModTheSpire.
It was rewritten in C# for STS2 after discovering the Alchyr modding guide.
