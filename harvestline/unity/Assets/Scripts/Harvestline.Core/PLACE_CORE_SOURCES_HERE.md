# Harvestline.Core sources go here

Copy the contents of `harvestline/src/Harvestline.Core/**/*.cs` into this folder
(preserving the subfolders: `Model/`, `Content/`, `Simulation/`, `Economy/`,
`Progression/`, `Save/`, `Rng/`, `Samples/`, plus `GameState.cs`).

The `Harvestline.Core.asmdef` here has `noEngineReferences: true`, which makes Unity
fail the build if any of those files ever reference `UnityEngine` — enforcing the
architectural rule in spec §8. The same sources compile as a standalone
`netstandard2.1` library under `harvestline/src/`, so keep them in one place and copy
(or symlink) on integration; don't fork them.
