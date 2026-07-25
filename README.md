# The All You Need Save Manager (TAYNSM)

![TAYNSM icon](https://github.com/llucasandersen/The_All_You_Need_Save_Manager/raw/main/icon.png)

All-in-one save manager for PEAK. Save a run, load it later, and restore the run setup, players, inventory, and world state as closely as the game allows.

## What It Syncs

### Run setup
- Scene name (`Level_X`)
- Level id and segment
- Seed and ascent
- Run day, run timer, and time of day

### Player state
- Position and rotation
- Velocity and angular velocity
- Stamina and extra stamina
- Status effects
- Checkpoint ownership links

### Inventory
- Main slots
- Backpack slots
- Temp slot
- Equipped and selected slot
- Held item id when available
- Item uses and item data entries

### World and interactables
- Campfire state and progress
- Luggage and cursed luggage state
- Respawn chest and mirage luggage state
- Ground items with transform and rigidbody data

### Deployables and world objects
- Piton and piton handle
- Rope anchor, rope, rope anchor with rope, rope anchor projectile
- Chain launcher
- Magic bean vine
- Cloud fungus
- Bounce fungus
- Shelf fungus
- Portable stove
- Checkpoint flag and checkpoint constructable
- Scout cannon

## How To Use

1. Open the pause menu and click `SAVE MANAGER`.
2. Click `Quick Save`, or enter a save name and click `Save`.
3. In the save list, click `Load` on the save you want.
4. Use `Overwrite` to replace an existing save with your current run state.

## Autosave

Autosave can be disabled in the BepInEx config file.

- Config file: `BepInEx/config/com.lucasandersen.peakallyouneedsavemanager.cfg`
- Section: `AutoSave`
- Setting: `Enable AutoSave = false`
- Timer setting: `Interval Seconds = 300`

If the config file does not exist yet, launch the game once with the mod installed and BepInEx will generate it.

## Loading Notes

- You can load from Airport.
- You can load while already in a run.
- In multiplayer, the host controls save and load.
- Multiplayer loads now restore every saved player that matches, even if the full saved party is not present yet.

## Save Files

- Folder: `<PEAK root>/PeakSaves`
- Extension: `.peaksave.json`
- Versioned format with compatibility checks for safe loading
- If the game install folder is not writable, TAYNSM falls back to a writable save folder automatically

## Installation

Extract so this path exists:

`BepInEx/plugins/TAYNSM/TAYNSM.dll`

## Source And Contributing

The GitHub repo now includes the source project and the verification tool.

- Source project: `src/PeakSaveManager`
- Verification tool: `tools/SaveVerificationTests`
- Issues: https://github.com/llucasandersen/The_All_You_Need_Save_Manager/issues
- Pull requests: https://github.com/llucasandersen/The_All_You_Need_Save_Manager/pulls
- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)

Build command:

```powershell
dotnet build src/PeakSaveManager/PeakSaveManager.csproj -c Release -p:BepInExCorePath="F:/Video Games/SteamLibrary/steamapps/common/PEAK/backup/BepInEx/core"
```

Verification command:

```powershell
dotnet run --project tools/SaveVerificationTests/SaveVerificationTests.csproj -c Release
```

## Changelog

### 1.0.8
- Fixed multiplayer save loads stopping early when not every saved player was matched before restore.
- Multiplayer loads now wait briefly for joiners, then continue with every player that does match instead of cancelling the whole load.
- Added the source project and verification tool to the GitHub repo.

Older release notes are in [CHANGELOG.md](CHANGELOG.md).

## Known Bugs

- Shelf fungus can fail to restore in some saves.
- Bounce fungus can fail to restore in some saves.

## Links

- GitHub: https://github.com/llucasandersen/The_All_You_Need_Save_Manager
- Thunderstore: https://thunderstore.io/c/peak/p/llucasandersen/The_All_You_Need_Save_Manager/
