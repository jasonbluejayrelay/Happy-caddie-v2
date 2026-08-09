# Unity integration guide (M2+)

The `Harvestline.Core` C# sources are written to drop into a Unity project unchanged.
This file explains the mapping and the M2+ work that layers on top.

## Why it just works

- `Harvestline.Core` targets **`netstandard2.1`**, which Unity's scripting runtime
  consumes directly.
- It has **zero third-party runtime dependencies** and **never references
  UnityEngine** — the save layer even hand-rolls its JSON (Unity's `JsonUtility`
  can't round-trip dictionaries) so nothing external is pulled in.
- Determinism uses a hand-written `DeterministicRng` (SplitMix64), not
  `System.Random`, so behavior is identical across Mono, IL2CPP, and desktop .NET.

## File mapping

Copy the sources under `src/Harvestline.Core/**/*.cs` into your Unity project at:

```
Assets/Scripts/Harvestline.Core/          <- Harvestline.Core.asmdef  (noEngineReferences: true)
    Model/  Content/  Simulation/  Economy/  Progression/  Save/  Rng/  Samples/
Assets/Scripts/Harvestline.Unity/         <- Harvestline.Unity.asmdef (references Harvestline.Core)
    View/ Input/ UI/ Bootstrap/           <- written during M2–M7
Assets/Scripts/Harvestline.Tests/         <- Harvestline.Tests.asmdef (EditMode, NUnit)
```

The three `*.asmdef` templates are in [`unity/`](unity/). The test files under
`tests/Harvestline.Tests/*.cs` are ordinary NUnit and run as **EditMode** tests in the
Unity Test Framework with no changes.

> Keep authoring and running the tests here with `dotnet test` during logic work —
> it's faster than Play Mode and CI-friendly — and let them double as Unity EditMode
> tests.

## What each M2+ milestone adds (all in `Harvestline.Unity`, none touching Core)

- **M2 — Rendering & placement.** `GridRenderer`, `StructureView` (runtime
  flat-shaded low-poly meshes from vertex arrays, GPU-instanced), `CameraController`,
  `TouchInputHandler`, `PlacementController`. Reads `GridState` read-only. Conveyor
  items are instanced quads, not GameObjects (§9).
- **M3 — Resume screen.** `GameBootstrap` reads the save via `SaveIO` (atomic
  temp-file → `File.Replace`), computes elapsed with
  `SaveSerializer.ElapsedSinceLastSim` (rejects clock rollback), calls
  `FactorySimulator.SimulateOffline`, and shows the returned `BottleneckReport`.
- **M4 — Harvest & notifications.** Drive `HarvestResolver` on the fixed 72h clock
  (`GameState.HarvestsDueBy`); a forecast panel reads
  `FactorySimulator.ProjectedFoodPerHour` vs `HarvestResolver.Demand`; schedule the
  local push 6h before the next Harvest.
- **M5 — Market & contracts.** Sell UI over `MarketSimulator.Sell`; advance the walk
  on open with `MarketSimulator.AdvanceBy`; surface `ContractGenerator` offers.
- **M6 — Prestige & tier 3.** Resettlement flow over `PrestigeCalculator`; tier-3
  content already exists in `ContentDatabase`. **Balance tuning validated against the
  `bot`.**

## ScriptableObjects vs. code content

`ContentDatabase.CreateDefault()` authors all §4 structures in code so the headless
core and the balance bot run standalone. In Unity, author the same records as
`ScriptableObject` assets and populate a `ContentDatabase` from them at load — the
solver only ever reads the resulting plain records, so **adding a machine never
touches the solver** (§8).
