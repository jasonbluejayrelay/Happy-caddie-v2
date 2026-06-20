# Happy Caddie

A personal golf app for **North Hampton Golf Club** (Fernandina Beach, FL). Packaged as a
native Android APK.

**Features**
- Club-by-club **shot tracking** with GPS, and per-club **average distances + suggestions**
- **Edit / undo shots** (corrects club averages automatically)
- **GPS/camera rangefinder** with **Front / Center / Back** of green distances
- **Hazard carries & layup** distances per hole
- **Team best-ball scorecard** + **live match**: net best-ball with handicaps, skins, who's winning
- **FIR / GIR / Putts** tracking per hole
- **Round history & trends** (scoring average, GIR%, FIR%, putts, rough handicap)
- **Editable bag** — set the clubs you actually carry
- **Voice commands** (wake word "golf") + **spoken confirmations** (hands-free)
- **Auto-advance to the next hole by GPS**
- In-app **Commands & Help** screen
- **Keeps the screen awake** during a round
- **Backup / Restore** so your data survives a reinstall or new phone

## 📲 Install on your phone (no Play Store)

1. Go to the **[Releases](../../releases)** page (or the **Actions** tab → latest run → Artifacts).
2. Download **`golf-tracker.apk`** directly on your Android phone.
3. Open the downloaded file. If Android warns you, allow **"Install from unknown sources"**
   for your browser, then tap **Install**.
4. Launch **Happy Caddie** and grant **Location** and **Camera** when asked.

Your data (club averages, rounds, scorecards) is stored on-device and persists until you
uninstall the app.

## 🔨 How the APK is built

Every push to `main` (or a manual run from the **Actions** tab → *Build Android APK* →
*Run workflow*) triggers GitHub Actions to:

1. Install Capacitor (`npm ci`)
2. Generate the native Android project (`npx cap add android`)
3. Apply camera + GPS permissions (`AndroidManifest.xml`)
4. Build a debug-signed APK (`./gradlew assembleDebug`)
5. Publish it to the **Releases** page and as a build **Artifact**

No Android tooling needed on your computer — it all happens in the cloud.

## 🗣 Voice commands

Tap **🎤 Listen** (push-to-talk) or **🔄** for always-on. Say **"golf"** to wake, then:

- **"next shot"** — record the current shot and start the next
- **"made it"** — hole complete (opens score entry)
- **"driver" / "7 iron" / "pitching wedge"** — select a club
- **"score 5"** — set your score for the hole
- **"birdie" / "bogey" / "par"** — set score by name

## 📁 Project layout

```
www/                     # The actual app (HTML/CSS/JS, runs offline)
  index.html
  app.js
  course-data.js         # North Hampton 18-hole data
capacitor.config.json    # Native wrapper config
AndroidManifest.xml      # Permission overlay (camera, fine/coarse location)
.github/workflows/       # Cloud APK build
```

## 🛠 Building locally (optional)

If you ever want to build on your own machine instead of the cloud:

```bash
npm install
npx cap add android
cp AndroidManifest.xml android/app/src/main/AndroidManifest.xml
npx cap sync android
cd android && ./gradlew assembleDebug
# APK at: android/app/build/outputs/apk/debug/app-debug.apk
```

Requires JDK 17+ and the Android SDK (Android Studio installs both).
