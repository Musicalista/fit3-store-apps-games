# Fit3 store: apps and games for the Galaxy Fit3

Small native apps and games ("light apps", `.f3a` files) for the **Samsung Galaxy Fit3 (SM-R390)**, an SDK to write your own, and tools that put them on the watch over Bluetooth in seconds, without reflashing the firmware: **Fit3 Apps** (Android, apps only) and **Fit3 Manager** (Windows and Android: apps, files, an app creator, and a firmware flasher).

> **Unofficial. Not affiliated with Samsung.** This repository contains **no Samsung firmware** and no firmware images of any kind. The firmware flasher in the Fit3 Manager is a port of the protocol and package checks of [yuriyurin/fit3-flasher](https://github.com/yuriyurin/fit3-flasher): see [NOTICE.md](NOTICE.md). Everything here has been tested **on a PC only** (a simulator and a mock watch). **Not yet verified on a real watch:** the Bluetooth transfer from the phone, and whether the watch can execute code loaded into RAM, which every light app depends on. The firmware loader needs a patched firmware; flashing firmware can make a watch unusable, so read the warnings in the firmware project before you flash anything.

## What is in here

| Folder | What |
|---|---|
| `apps/` | The SDK (`f3app.h`, `f3game.h`, `app.ld`), the compiler script `appcc.py`, **22 apps** in `src/`, prebuilt `dist/*.f3a`, `build_games.py`, and PC test tools |
| `android/` | **Fit3 Apps**: an Android app (plain Java, no Gradle) that lists the apps, installs, updates and removes them, one or many at a time |
| `pc/` | **Fit3 Manager for Windows** (C#, builds with the compiler that ships with Windows): connect, install apps, send ROMs and saves, **create apps**, and **flash firmware** |
| `android-manager/` | **Fit3 Manager for Android**: the same flasher and app installer in one app (a tab for each) |
| `firmware/` | `appsys.inc.c`: the "Meus apps" loader that runs inside the watch firmware (reference; it builds inside the firmware project, see below) |
| `docs/` | `APP_FORMAT.md`: the `.f3a` file format and the app API |

## The apps

Games: Snake, Flappy, Tetris, 2048, Pong, Tic-tac-toe, Memory, Sudoku, Minesweeper, Breakout, Simon.
Tools: Clock (real time, date, battery), Alarm (3 alarms), Timer and stopwatch, Counter, Coin and number draw, Breathing guide, Reaction test, Flashlight.
Examples: hello, paint, dice.

Each app is 0.4 to 2.7 KB. The watch keeps up to 12 at a time; you choose which ones to install. Best scores are saved in `/user/*.sav` on the watch.

## Step by step

### A. Install apps and games on the watch

1. **Get a watch ready.** The watch needs a firmware with the **"Meus apps"** loader (`firmware/appsys.inc.c`, part of the Fit3 firmware mods project) and its Bluetooth serial service reachable. If it has neither, the app cannot talk to it. Flashing firmware is outside this repository and can make a watch unusable: read that project's warnings first and keep the original firmware.
2. **Build the Android app** (once): install JDK 17 and the Android SDK (platform 34, build-tools 34), set `JAVA_HOME` and `ANDROID_SDK_ROOT`, then run `python android/build.py`. The APK is written to `android/dist/fit3-apps.apk` and bundles every `.f3a` from `apps/dist`. Copy it to the phone and allow installing from unknown sources.
3. **Pair the watch** with the phone in the Bluetooth settings, and close Galaxy Wearable (the watch usually serves one client at a time).
4. Open **Fit3 Apps**, allow the Bluetooth permission, pick the watch and tap **Connect**. It retries for up to 30 seconds and shows a help dialog if the watch refuses.
5. **Choose apps.** Tick **Select** on each card (or **Select all**) and tap **Install selected**; or tap **Install** on a single app. The watch holds up to 12 apps at once.
6. **Open them on the watch:** *Extra apps > Meus apps*.
7. **Remove an app:** tap **Remove** on its card, or on its row under **Installed on the watch** (tap **Show watch apps** first). This takes it off the watch's list; you can install it again at any time.
8. **Add your own app:** **+ Add an app from the phone (.f3a)**, then install it like any other.

If the connection fails: disconnect the watch from the main phone's Bluetooth, force-stop Galaxy Wearable and Samsung Accessory Service, restart the watch, toggle the phone's Bluetooth and tap **Connect** again.

**The Windows Manager's interface is in Portuguese** (the Android apps follow the phone's language). Pages: *Início* (Home: connect), *Loja de apps* (Store: firmware packages by feature, with a guided install wizard), *Apps leves* (Light apps: install, update, remove, tick several), *Jogos e arquivos* (Games and files: Game Boy ROM, saves), *Relógio* (Watch: info, test notification, brightness), *Criar app* (Create app: edit C, compile with one click, install), *Avançado* (Advanced: flash any package you choose). Build it with `python pc/build.py` (add `--test` to run the tests); it needs only Windows, a Bluetooth adapter and, for *Criar app*, the Arm GNU Toolchain.

### A2. Flash a firmware (Fit3 Manager, Windows or Android)

**Flashing can make the watch unusable. This repository has no firmware: you bring your own package, and you keep the original (stock) one.** The Manager only sends a package after it has checked all of it (the FWD container, every component and checksum, the target `R390XXU0AZA3`); a damaged or unknown package is never sent.

1. Charge the watch to 50% or more and keep it next to the PC or phone. In *Settings > About > Software information* check that the version is **R390XXU0AZA3**; if it is not, do not flash.
2. Pair the watch, close Galaxy Wearable and anything else using the watch's Bluetooth, then **Connect** (Android) or **Test connection** (Windows, *Home*). If the watch refuses, see the connection tips in A.
3. **Windows:** *Firmware (Advanced)* > choose the `.bin`. **Android:** the *Firmware* tab > **Choose .bin file**.
4. Wait for the check ("verified"). Tick the risk box, then **Transfer to the watch** (three confirmations; optionally a full read-back, which is slower).
5. Only when the transfer is reported as done, tap **Start installation**. It cannot be cancelled afterwards: do not power the watch off and do not repeat the command. The watch restarts and updates by itself.

To go back to the original firmware, flash the stock package the same way.

### B. Write your own app

1. Install the **Arm GNU Toolchain** (`arm-none-eabi-gcc`, default install folder, or set `ARM_GNU_BIN`) and Python 3.
2. Copy `apps/src/hello.c` (or a game) to a new file and edit it. Read `apps/f3app.h` for the API and `docs/APP_FORMAT.md` for the file format. No libc: no `printf`, `memcpy` or `malloc`; use the helpers in `apps/f3game.h`.
3. Compile: `python apps/appcc.py apps/src/myapp.c --id myapp --pt "Meu app" --en "My app" -o apps/dist/myapp.f3a`. The compiler tells you if the app uses something the watch does not have.
4. Test on the PC (below), then rebuild the APK or add the file from the phone (step A.8).

### C. Test on a PC (no watch needed)

1. Install a host `gcc` (MinGW-w64 on Windows).
2. Run an app in the simulator: `gcc -O1 -I apps -o host_game apps/host_game.c apps/src/snake.c` then `./host_game shot "t5 s128,300>128,100 t20 shot"`. Script tokens: `tN` idle ticks, `pX,Y` tap, `hX,Y:N` hold, `sX1,Y1>X2,Y2` swipe, `cH:M:S` set the clock, `bN` battery, `shot` screenshot, `lang0`/`lang1`.
3. Random input: `./host_game x "" 1` (seed 1) checks nothing crashes or draws with bad arguments.
4. Game logic: `gcc -O1 -I apps -DT_SUDOKU -o logic apps/host_logic_test.c && ./logic` (also `-DT_2048`, `-DT_VELHA`, `-DT_TETRIS`, `-DT_MINES`).
5. Turn screenshots into PNG: `python apps/ppm2png.py shot_0.ppm`.

### D. Rebuild all apps

`python apps/build_games.py` builds every app into `apps/dist` (or name some: `python apps/build_games.py snake tetris`). To add an app to the list, add a line to `GAMES` in `apps/build_games.py` (id, Portuguese name, English name, background and foreground colours as `RRGGBB`).

Light apps run as native code on the watch, with no sandbox: only install apps you trust.

## Status

| Part | Status |
|---|---|
| Apps in the PC simulator, logic tests | Passing |
| `.f3a` loader (CRC, relocations) against a mock watch | Passing |
| Transfer protocol against a mock watch | Passing |
| FWD package validator on real packages (and damaged copies) | Passing |
| App creator (C#) against `appcc.py` | Byte-identical output |
| Real watch: transfer, flashing, running apps from RAM, real time and battery | **Not verified** |

Planned: steps, heart rate and notifications in the app API; a PC app creator.
