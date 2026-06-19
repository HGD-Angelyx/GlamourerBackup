# Glamourer Backup

Ever tired of crashing and losing your beautifully crafted outfit? Worry no more! I got your back with the glamourerbackup plugin. Automatically save your current outfit in set intervals and never get that frustration again! It will also backup all of your designs should you somehow lose those!

## Features

- **Auto-backup your current outfit** — Uses Glamourer's IPC API to snapshot whatever you're wearing right now, even if it hasn't been saved as a design. Saved to `Backups/CurrentOutfit/`.
- **Backup all saved designs** — Copies every `.json` design file from Glamourer's config directory into timestamped folders under `Backups/`.
- **Backup ephemeral config & folder organization** — Optionally includes Glamourer's ephemeral settings and design folder structure.
- **Configurable interval** — Set how often backups run (in minutes).
- **Automatic pruning** — Keeps only the latest N backups so disk usage stays under control.
- **Manual backup** — Hit "Back up now" anytime from the settings window.
- **Open backups folder** — One-click access to your backup directory.

## Installation

1. Open Dalamud settings (`/xlsettings`) → **Experimental** tab.
2. Under **Custom Plugin Repositories**, add:
   ```
   https://raw.githubusercontent.com/HGD-Angelyx/GlamourerBackup/main/repo.json
   ```
3. Click **+** then **Save**.
4. Open Plugin Installer → **All Plugins** tab and search for "Glamourer Backup".

## Usage

- Open settings with `/gbackup`.
- Adjust the backup interval, max backups, and toggle what gets backed up.
- The plugin runs on a timer and also triggers on plugin start (configurable).

## Requirements

- [Dalamud](https://github.com/goatcorp/Dalamud) (API level 15)
- [Glamourer](https://github.com/ChirpEye/Glamourer) — required for current-outfit backup; design backups work without it.

## Build

```bash
dotnet build -c Release
```

Output goes to `bin/Release/`.
