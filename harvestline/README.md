# Harvestline

Tile-based factory automation with offline accrual and a recurring survival deadline.
Target platform is **Android, 3D, Unity + C#** (see the full design spec, §1–§12).

> **You are never "collecting." You are always racing an exponential.** Offline
> accrual is generous; the demand curve is merciless. Every 3 real days a **Harvest**
> checks whether your factory fed the colony.

This directory lives *beside* the existing Happy Caddie golf app and is completely
independent of it.

## What's here (Milestone M1 — Simulation core, headless)

M1 is the highest-leverage slice in the spec: the entire simulation as plain,
Unity-free .NET. It is implemented, unit-tested, and passes its acceptance gate. The
same code drops into Unity for M2+ without modification (see
[`UNITY_INTEGRATION.md`](UNITY_INTEGRATION.md)).

```
harvestline/
├── Harvestline.sln
├── src/
│   ├── Harvestline.Core/         # netstandard2.1 · NO UnityEngine reference
│   │   ├── Model/                #   items, recipes, structures, grid, inventory
│   │   ├── Content/              #   ContentDatabase — all §4 structures as data
│   │   ├── Simulation/           #   RateSolver, PowerBudget, FactorySimulator, BottleneckReport
│   │   ├── Economy/              #   MarketSimulator (mean-reverting walk), ContractGenerator
│   │   ├── Progression/          #   HarvestResolver, PrestigeCalculator, ColonyState
│   │   ├── Save/                 #   JSON serializer + schema/migration + anti-cheat
│   │   ├── Rng/                  #   DeterministicRng (SplitMix64) — cross-platform stable
│   │   └── Samples/              #   SampleFactories + the balance bot (§11)
│   └── Harvestline.Harness/      # net8.0 console: run | gate | bot | save
├── tests/
│   └── Harvestline.Tests/        # NUnit — starve/block/throttle/cycle, determinism, save
└── unity/                        # asmdef templates + integration guide for M2+
```

## Build, test, run

Requires the .NET 8 SDK.

```bash
cd harvestline

# unit tests (27 tests: the M1 gate cases + determinism + economy + save)
dotnet test

# the M1 acceptance gate: 1000h run < 100ms AND bit-identical across runs
dotnet run --project src/Harvestline.Harness -- gate

# run the sample bread factory for N hours and print totals + bottleneck report
dotnet run --project src/Harvestline.Harness -- run 100

# the "reasonable player" balance bot: days-to-milestone table (§11)
dotnet run --project src/Harvestline.Harness -- bot 30

# round-trip a game through the save serializer
dotnet run --project src/Harvestline.Harness -- save
```

## The architectural rule (spec §8)

**The simulation core has no `UnityEngine` dependency.** `Harvestline.Core` targets
`netstandard2.1` and the Unity asmdef sets `noEngineReferences: true` to enforce it.
This is what makes the rest possible:

- the whole simulation is unit-testable without entering Play Mode;
- balance is tuned by running **headless simulations over thousands of hours** (the
  `bot` command);
- **offline catch-up is provably identical to online behavior** — one simulation
  implementation, two callers (verified by `Offline_Equals_Online...`).

## How the offline simulation works (spec §7)

Elapsed time is **not** tick-simulated (48h at 10 ticks/s would be 1.7M ticks).
Instead `FactorySimulator` does **piecewise-linear rate integration**:

1. `RateSolver` resolves a steady-state activity per machine for the current stocks —
   a fixed-point relaxation under starvation (empty input) and blockage (full output),
   capped at 20 passes so the Composter→Greenhouse→Soil Plot feedback cycle
   terminates — then applies one global power throttle.
2. The integrator finds the **next discontinuity** (a buffer fills, a buffer empties,
   or a generator's fuel runs out), integrates all rates linearly to it, applies the
   state change, and re-solves.

A 1000-hour horizon resolves in **~6 events**, not millions of ticks, in well under a
millisecond. Every step is deterministic and produces a per-machine **bottleneck
report** for the game's "Diagnose" loop.

## Milestone status

| Milestone | Scope | Status |
|---|---|---|
| **M1** | Simulation core, headless | ✅ implemented, tested, gate passing |
| M2 | Rendering & placement (Unity) | ⬜ scaffolded (asmdefs, notes) |
| M3 | Offline accrual & save UI | 🟡 core done (accrual, save, bottleneck report); needs Unity resume screen |
| M4 | Harvest & notifications | 🟡 resolver/curve/tokens done; needs Unity forecast panel + push |
| M5 | Market & contracts | 🟡 price walk + pressure + contracts done; needs sell UI |
| M6 | Prestige & tier 3 | 🟡 prestige math + tier-3 recipes done; **balance tuning is this gate** |
| M7 | Polish | ⬜ |

### Balance note

The `bot` command runs and reports days-to-milestone deterministically (the M1
requirement — keep the bot alive from day one). Tuning the content constants so the
first Harvest failure lands on **day 5–7** and first Resettlement on **day 12–20** is
the **M6 gate**, not M1. Because all content lives in `ContentDatabase` as data,
tuning never touches the solver.
