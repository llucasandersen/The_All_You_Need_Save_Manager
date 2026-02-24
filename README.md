# The All You Need Save Manager (TAYNSM)

![TAYNSM icon](https://github.com/llucasandersen/The_All_You_Need_Save_Manager/raw/main/icon.png)

All-in-one save manager for PEAK. Save your run, load it later, and continue with the same run setup, player state, inventory, and most world objects.

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

## Where Loading Works

- You can load from Airport.
- You can load while already in a run.
- In multiplayer, the host controls save and load.

## Save Files

- Folder: `<PEAK root>/PeakSaves`
- Extension: `.peaksave.json`
- Versioned format with compatibility checks for safe loading

## Known Bugs

- Shelf fungus can fail to restore in some saves.
- Bounce fungus can fail to restore in some saves.

## Installation

Extract so this path exists:

`BepInEx/plugins/TAYNSM/TAYNSM.dll`

## Links

- GitHub: https://github.com/llucasandersen/The_All_You_Need_Save_Manager
- Thunderstore: https://thunderstore.io/c/peak/p/llucasandersen/The_All_You_Need_Save_Manager/

## Contributing

- Open an issue: https://github.com/llucasandersen/The_All_You_Need_Save_Manager/issues
- Read the guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Open a pull request: https://github.com/llucasandersen/The_All_You_Need_Save_Manager/pulls
