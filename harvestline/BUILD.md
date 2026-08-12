# Building Harvestline

There are two builds. The **playtest build** (desktop, real simulation, no Unity) is
the quickest way to test the loop — see [`PLAY.md`](PLAY.md). This file covers the
**Unity Android APK**: the 3D game you install on a phone.

## Why the APK is built in CI, not handed to you pre-built

Building the Unity APK requires the Unity Editor plus a **Unity licence tied to a Unity
account**. Licences can't be shared, and activation is interactive/online — so I can't
build the APK for you. The build therefore runs in **GitHub Actions** (where the repo
already builds the golf APK), producing a downloadable artifact once you add your
licence as a secret. This is a one-time setup.

## One-time setup: add your Unity licence secret

The workflow uses [GameCI](https://game.ci). Follow their
[activation guide](https://game.ci/docs/github/activation) — summary:

1. **Free Personal licence works.** In GitHub → repo **Settings → Secrets and variables
   → Actions**, add:
   - `UNITY_EMAIL` — your Unity account email
   - `UNITY_PASSWORD` — your Unity account password
   - `UNITY_LICENSE` — the contents of your `.ulf` licence file (generate it once via
     GameCI's activation steps: run their `request-activation-file` action, upload the
     `.alf` to <https://license.unity3d.com/manual>, download the `.ulf`, paste it here).

   (Unity **Plus/Pro** users can instead set `UNITY_SERIAL` + `UNITY_EMAIL` +
   `UNITY_PASSWORD`.)

2. Commit is already done — the workflow lives at
   `.github/workflows/harvestline-unity-apk.yml`.

## Build the APK

GitHub → **Actions** tab → **Build Harvestline APK (Unity)** → **Run workflow**.

When it finishes (first run is slow — Unity image pull + IL2CPP), download the
**Harvestline-APK** artifact from the run, unzip, and sideload `Harvestline.apk` onto
your Android device (enable "install from unknown sources").

## What the workflow does

1. Checks out the repo.
2. **Syncs the Core sources** from `harvestline/src/Harvestline.Core/**/*.cs` into
   `harvestline/unity/Assets/Scripts/Harvestline.Core/` (the Core lives in one place and
   compiles both as a standalone .NET library and inside Unity).
3. Runs `game-ci/unity-builder` against `harvestline/unity`, calling
   `HarvestlineBuild.PerformAndroidBuild`, which:
   - creates a URP pipeline asset in-memory,
   - configures Player settings (bundle id `com.harvestline.game`, IL2CPP, ARM64),
   - creates a one-object scene carrying `SceneComposer` (which builds the camera,
     light, renderer, input, and HUD from code — no hand-authored `.unity` asset),
   - builds a sideloadable APK.
4. Uploads the APK as a build artifact.

## Building locally (alternative)

If you have Unity Hub with **2022.3.40f1** + **Android Build Support** (SDK/NDK/JDK):

```bash
# from the repo root, sync Core sources into the Unity project once:
rsync -am --include='*/' --include='*.cs' --exclude='*' \
  harvestline/src/Harvestline.Core/ harvestline/unity/Assets/Scripts/Harvestline.Core/

# then open harvestline/unity in the Editor and Build (Android), or headless:
/path/to/Unity -batchmode -quit -projectPath harvestline/unity \
  -buildTarget Android \
  -executeMethod Harvestline.Editor.HarvestlineBuild.PerformAndroidBuild \
  -outputPath build/Harvestline.apk -logFile -
```

## Status / caveat

The Unity view scripts, shader, scene composer, and this build pipeline are written and
internally consistent against the Core API, but they have **not been compiled inside a
Unity Editor in this environment** (no Editor available here). Treat the first CI run as
the compile/validation pass — expect to fix a small number of Editor-only issues
(missing `.meta` files are generated on first import; a package version may need a bump
for your exact Editor). Everything under `harvestline/src` and `harvestline/tests` **is**
compiled and tested (37 passing).
