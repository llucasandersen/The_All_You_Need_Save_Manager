## 1.0.8

- Fixed multiplayer save loads stopping early when the full saved party was not matched before restore.
- Multiplayer loads now wait briefly for joiners, then continue with every matching player instead of cancelling the whole load.
- Added the source project and verification tool to the GitHub repo so contributors can build and submit fixes more easily.

## 1.0.7

- Fixed multiplayer load continuing with only the host matched instead of waiting for the full saved party.
- Improved multiplayer player matching during load from Airport into a saved run.
- Added clearer load failure messaging when saved players are missing or not ready yet.

## 1.0.6

- Fixed duplicate item data key restore failures during load, including the reported `CookedAmount` crash.
- Prevented ground item restore errors from blocking the player position restore that happens later in the load flow.
- Improved reliability when loading host saves back from Airport into multiplayer runs.

## 1.0.5

- Fixed a load pipeline failure that could stop restore early during scout or other world object restore work.
- Prevented scout cannon restore mismatches from aborting the rest of the load.
- Improved load failure logging and UI error reporting.
- Added scout coverage to save verification tests.

## 1.0.4

- Fixed save creation failing on some installs when the game root save folder was not writable.
- Added automatic fallback to a writable save folder.
- Improved save failure messages so the UI shows the actual reason when possible.

## 1.0.3

- Added GitHub project link in `manifest.json`.
- Refreshed README content and added icon preview.
- Cleaned package metadata for release page quality.

## 1.0.2

- Packaged release as TAYNSM.dll.
- Clean Thunderstore export layout.
- Documented known bug for Shelf Fungus and Bounce Fungus restore behavior.
