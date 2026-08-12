# Play Harvestline now (no Unity, no install of anything)

This is the **playtest build**: the real Harvestline simulation core wrapped in an
interactive terminal so you can experience the whole loop — build a factory, let time
pass, and race the Harvest deadline — today, on any desktop. It runs the *exact*
`Harvestline.Core` the Unity game uses, so it's a faithful test of the loop (spec §12:
"prove the loop is fun before the 3D polish").

## Download & run

Grab the zip for your OS, unzip, and run the single file inside — nothing else to
install (the .NET runtime is bundled).

| OS | File | Run |
|---|---|---|
| Windows | `harvestline-win-x64.zip` | double-click `Harvestline.exe` (or run it in PowerShell) |
| macOS (Apple Silicon) | `harvestline-osx-arm64.zip` | `./Harvestline` in Terminal |
| macOS (Intel) | `harvestline-osx-x64.zip` | `./Harvestline` in Terminal |
| Linux | `harvestline-linux-x64.zip` | `./Harvestline` |

macOS/Linux may need `chmod +x Harvestline` first. On macOS, Gatekeeper may ask you to
allow an unsigned binary (right-click → Open, or `xattr -d com.apple.quarantine Harvestline`).

> Build them yourself any time with `tools/publish-playable.sh`.

## How to play

You start with a small bread line and 120 Credits. A Harvest fires every 72 in-game
hours and checks your food against demand; meet it and population (and next demand)
grows, miss it and it shrinks. Type `advance 72` to jump to the next Harvest.

```
help                 all commands
look                 show the grid, stores, and status
shop                 buildings you can construct + costs
build bakery 3 0     place a building at column 3, row 0
silo bread 6 6       storage silo assigned to Bread (+200)
sell                 sell non-food surplus for Credits
forecast             projected food vs the next Harvest
advance 72           let 72h pass — production accrues, the Harvest fires
```

A quick first session:

```
forecast                 # see where you stand
silo bread 6 6           # more bread storage so you can bank enough food
advance 72               # first Harvest — should pass
sell                     # turn surplus grain/timber into Credits
build coop 5 3           # start branching toward higher-value food (Eggs → Preserves)
advance 72               # keep going; watch demand climb each Harvest
```

Run with `--demo` to watch a scripted showcase without typing.

## What this proves (and doesn't)

- **Does** exercise the real steady-state solver, offline accrual, the Harvest demand
  curve, the market, storage/space pressure, and the save format.
- **Doesn't** render the 3D low-poly world or use touch input — that's the Unity build
  (`unity/`), which produces the Android APK. See `BUILD.md` for the APK path.
