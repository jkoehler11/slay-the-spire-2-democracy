# Democracy Mod — Design Document (STS2 Edition)

## Overview

**Democracy** is a multiplayer-only mod for Slay the Spire 2. After each combat, every
player selects their own loot first, then the group's rewards are pooled into a shared
treasury and voted on together: anyone can claim any reward, and the distribution is
decided by the players, not by who earned it.

## Core Philosophy

> You didn't earn that relic alone — your team carried you through the fight.
> Now convince them you deserve it.

The mod transforms Slay the Spire 2 from a solo deckbuilder with parallel play into a
competitive-cooperative negotiation game.

## Platform

- **Game:** Slay the Spire 2 (Early Access, tested on build 0.113.0)
- **Language:** C# / .NET 9.0
- **Engine:** Godot 4.5.1 (MegaDot)
- **Mod Framework:** BaseLib + Harmony
- **Networking:** STS2 built-in deterministic multiplayer (`MegaCrit.Sts2.Core.Multiplayer`)

## Mechanics

### 1. Loot Selection (vanilla first)

After every combat (normal, elite, boss), each player selects their own rewards on the
vanilla loot screen — the normal "pick 1 of 3" for cards, and the usual claims for
potions, relics, and gold. The mod does **not** auto-pick or suppress this screen. The
interventions during this phase are (a) holding the vanilla **Proceed** button while there
is pooled loot, so a player can't click through to the map before the group vote finishes,
and (b) intercepting the loot screen's **Skip** button so it declines every remaining
reward (never grants or pools them) instead of advancing — with a `ConfirmSkipPanel`
confirmation shown only when unclaimed rewards remain.

### 2. Reward Pooling

Once a player takes a reward, it is captured into the shared **Democracy Pool**, tagged
with the player who earned it. Pooling is driven by `Hook.AfterRewardTaken` (which fires
on every machine for every player), so the pool is built deterministically and identically
everywhere. Non-combat rewards (events, shops, ancients) are never pooled.

| Reward Type     | Pool Behavior                                                                 |
|-----------------|-------------------------------------------------------------------------------|
| Gold            | Pooled, then distributed by a group vote on *how* to split it                 |
| Card Rewards    | **Only the selected card** becomes a pool entry (a "pick 1 of 3" yields 1)    |
| Potions         | Each potion drop becomes one pool entry                                       |
| Relics (combat) | Each relic drop becomes one pool entry                                        |
| Boss Relics     | Treated as relics; a major negotiation moment                                 |

Card rewards pool only the card the player actually picked. The unchosen options are
discarded by the game's own `CardReward.OnSelect` (they are never granted), so the cards
page lists the chosen cards — one entry per card reward — rather than every generated
option.

### 3. The Claim Flow (up to four synchronized pages)

Once every player has finished picking, the group walks through up to four pages in a fixed
order, using the game's native event-choice buttons. Crucially, the group **advances
together**: each player votes on the current page, and the host only broadcasts the
advance once every player has voted. **A page with no pooled entries is skipped**, so a
fight with no potion drop simply omits the potions page.

| Page | Category | Interaction                      | Advance button |
|------|----------|----------------------------------|----------------|
| 1    | Gold     | single choice (3 modes)          | Next           |
| 2    | Potions  | 0..N toggle                      | Next           |
| 3    | Relics   | 0..N toggle                      | Next           |
| 4    | Cards    | 0..N toggle                      | Finish         |

Each page is a native `NEventOptionButton` list (the same large title + description
buttons events use), hosted in a synthetic `NEventRoom`. Pages are skipped deterministically
by checking the pool on every machine, so the stage sequence is identical everywhere.

### 4. Gold Distribution Vote

Gold is page 1, not a per-pile claim. Three mutually exclusive options:

| Mode             | Behavior                                                                  |
|------------------|---------------------------------------------------------------------------|
| Original amount  | Everyone keeps exactly what they earned (no pooling). The default choice. |
| Randomized       | Pool all gold and deal it out in a deterministic random shuffle.          |
| Distribute evenly| Pool all gold and split it evenly (remainder to the lowest-indexed players). |

The mode with the most votes wins (plurality). Ties are broken deterministically.

### 5. Resolution (host-authoritative)

After the cards page, exactly one machine — the **host** (the first player in the run) —
computes the outcome:

- **Uncontested** rewards go to their sole claimant.
- **Contested** rewards are tie-broken deterministically: a stable FNV-1a hash over the
  reward id + sorted player ids, weighted toward players who have won fewer rewards
  (configurable via `TieBreakFairness`).
- **Unclaimed** rewards return to whoever earned them.
- **Gold** is reclaimed and re-granted per the winning mode.

The host then applies the transfers locally and broadcasts the full decision
(`DemocracyResolvedMessage`); every client applies the identical result. A single resolver
plus fully deterministic tie-breaks means all machines converge to the same state.

### 6. Post-Distribution Advance

After the results panel, the **Continue** button replicates the vanilla terminal-reward
proceed: a normal combat reopens the map (`RunManager.ProceedFromTerminalRewardsScreen`),
while a boss/victory room drives the group's act-change transition
(`ActChangeSynchronizer.SetLocalPlayerReady`). This mirrors the vanilla
`NRewardsScreen.OnProceedButtonPressed` branching — the act-change path is **only** used at
boss/victory rooms, never gated on `IsWaitingForOtherPlayers()` (whose ready flags are
all-false outside an act transition and would strand the first player to press Continue).

## Architecture

### Harmony Patches

All interception uses Harmony postfixes/prefixes (the actual targets, verified against the
live `sts2.dll`):

| Patch                          | Target method                             | Purpose                                    |
|--------------------------------|-------------------------------------------|--------------------------------------------|
| Capture gate                   | `RewardsSetSynchronizer.BeginRewardsSet`  | Gate reward-grant capture to the reward phase |
| Completion detection           | `RewardsSetSynchronizer.CompleteRewardsSetIfNecessary` | Detect all-players-done |
| Proceed hold                   | `NRewardsScreen.TryEnableProceedButton`   | Hold vanilla Proceed while loot is pooled  |
| Reward pooling                 | `Hook.AfterRewardTaken`                   | Pool rewards (selected card only)          |
| Shop purchase logging          | `Hook.AfterItemPurchased`                 | Log shop purchases                         |
| Grant capture (card)           | `CardPileCmd.Add`                          | Capture granted card models                |
| Grant capture (potion)         | `PotionCmd.TryToProcure`                   | Capture granted potion models              |
| Grant capture (relic)          | `RelicCmd.Obtain`                          | Capture granted relic models               |
| Per-combat reset               | `CombatRoom.OnCombatEnded`                | Reset state between combats                |
| Native button routing          | `NEventRoom.get_Instance`                  | Route native buttons without a room        |
| Portrait skip                  | `EventModel.CreateInitialPortrait` / `CreateInitialPhobiaModePortrait` | Skip the synthetic event's portrait |
| Crash probes                   | `NEventRoom.SetOptions` / `NEventLayout.OnSetupComplete` | Diagnostic logging |
| Deck view refresh              | `NDeckViewScreen.ShowScreen`               | Conditional stale-snapshot refresh         |
| Deck count                     | `NTopBarDeckButton.Initialize` / `OnPileContentsChanged` | Keep the top-bar count correct after transfers |

### Networking

Four binary messages (STS2 `PacketWriter`/`PacketReader` via BaseLib `ICustomMessage`):

- `DemocracyStageMessage` — a player's vote for one stage: the stage index, a gold mode
  (stage 0 only), and the reward entry ids they want (stages 1–3). Broadcast to everyone.
- `DemocracyAdvanceMessage` — the host's signal that every player has voted for the current
  stage, carrying the next stage index. Broadcast to everyone; each machine shows the next
  page.
- `DemocracyResolvedMessage` — the host's authoritative decision after the final stage:
  per-entry winner ids (0 = discarded), gold reclaim/grant lists, and the winning gold
  mode. Broadcast to everyone; clients apply it verbatim.
- `DemocracyConfigMessage` — the host's gameplay config snapshot, broadcast once at run
  launch so every machine follows the host's settings (6 bools + `TieBreakFairness`).

### Determinism Rules

Every decision the host makes must be byte-identical on every machine. The mod enforces:

- sorted player ids everywhere (no unordered `Dictionary`/`HashSet` iteration),
- a stable FNV-1a hash for tie-breaks and the randomized gold shuffle (no `Random`,
  `Guid`, `DateTime`, or `GetHashCode()`),
- host-authoritative resolution (clients never resolve independently),
- deterministic stage skipping (a page is skipped on every machine only when its pool is
  empty on every machine),
- host-authoritative config (at run launch the host broadcasts its gameplay settings via
  `DemocracyConfigMessage`; every machine reads those values through the `HostConfig`
  effective layer, so a client's own local settings never diverge the flow). Logging flags
  stay local — verbosity only, no gameplay effect.

## Configuration

Via BaseLib `SimpleModConfig` (editable in-game):

| Setting            | Default          | Purpose                                                    |
|--------------------|------------------|------------------------------------------------------------|
| Show Gold Screen   | ON               | Vote on gold split. OFF: gold stays with its earner.       |
| Show Potions Screen| ON               | Vote on potion claims. OFF: potions stay with their earner.|
| Show Relics Screen | ON               | Vote on relic claims. OFF: relics stay with their earner.  |
| Show Cards Screen  | ON               | Vote on card claims. OFF: cards stay with their earner.    |
| Show Results       | ON               | Show the post-combat results summary (OFF skips it)        |
| Enable Ancients    | ON               | Pool ancient rewards (Neow, Darv, Orobas, …) and vote on them |
| Tie-Break Fairness | 0.10             | Weight bonus for win-count deficit in contested tie-breaks |

The four show-screen toggles live in a **Combat** section; the results summary toggle also
moved there; **Enable Ancients** lives in its own **Ancients** section. A disabled screen's
rewards are simply kept by whoever earned them. All **Combat**/**Ancients**/**Gameplay**
settings are **host-authoritative** — the host broadcasts them at run launch and clients
follow the host's values (only the **Logging** toggles are per-machine).

## Roadmap

| Feature                        | Status |
|--------------------------------|--------|
| Vanilla loot selection first   | DONE   |
| Reward pooling                 | DONE   |
| Four-page claim flow (skip empty pages) | DONE |
| Gold distribution vote         | DONE   |
| Selected-card pooling          | DONE   |
| Host-authoritative resolution  | DONE   |
| Deterministic transfers        | DONE   |
| Post-distribution advance      | DONE   |
| Ancient reward pooling         | DONE   |
| Skip-all-rewards on loot screen| DONE   |
| Host-authoritative config sync | DONE   |
| Shop voting                    | TODO   |
| Shop redistribution            | TODO   |
| Dead-player gold handling      | PARTIAL (pooled gold still split) |
| Neow blessing vote             | TODO   |

## History

The original mod (v0.0) was written in Java targeting STS1 / ModTheSpire. It was rewritten
in C# for STS2 after the Alchyr modding guide was published. The claim UI was then replaced
with the game's native event-choice buttons and split into four stage-gated pages (gold →
potions → relics → cards) so the group advances together after each page. Later revisions
reversed the auto-pick/suppress design in favor of vanilla loot selection first, made the
cards page pool only the selected card, and added deterministic stage skipping and the
correct post-distribution advance (map vs. act-change) logic.
