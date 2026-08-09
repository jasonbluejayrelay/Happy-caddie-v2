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

The [`unity/`](unity/) folder is a ready Unity `Assets/` skeleton:

```
unity/Assets/
  Scripts/
    Harvestline.Core/     <- Harvestline.Core.asmdef (noEngineReferences: true)
                             copy src/Harvestline.Core/**/*.cs here (see the marker file)
    Harvestline.Unity/    <- Harvestline.Unity.asmdef (references Harvestline.Core, UnityEngine.UI)
      View/               MeshFactory, Palette, StructureVisuals, GridRenderer
      Input/              CameraController, PlacementController
      UI/                 ForecastPanel, ResumeScreen
      Bootstrap/          GameBootstrap, SaveIO
    Harvestline.Tests/    <- Harvestline.Tests.asmdef (EditMode, NUnit)
  Shaders/
    HarvestlinePalette.shader
```

The Core sources live once under `src/Harvestline.Core/` (so they build and test as a
standalone library) and are copied into `unity/Assets/Scripts/Harvestline.Core/` on
integration. The `tests/Harvestline.Tests/*.cs` files are ordinary NUnit and run as
**EditMode** tests in the Unity Test Framework unchanged.

> **Status:** the `Harvestline.Unity` view scripts and the shader are written against
> the Core API but have **not been compiled inside Unity** in this environment (no
> Editor available here). Treat M2 as "code-complete, needs an in-Editor compile/scene
> pass": create a scene with a Camera (+ `CameraController`), an empty `GameBootstrap`
> object with the serialized references wired, a `Palette` asset, and a material using
> `Harvestline/Palette`.

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
