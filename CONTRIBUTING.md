# Contributing

Thanks for helping improve The All You Need Save Manager.

## Fastest way to contribute

1. Open an issue first.
2. Use the issue template that matches your report.
3. If you want to code a fix, comment that you are working on it.
4. Open a pull request linked to the issue.

## Repo layout

- `src/PeakSaveManager` contains the plugin source.
- `tools/SaveVerificationTests` contains the lightweight save verification tool.
- `BepInEx/plugins/TAYNSM/TAYNSM.dll` is the packaged release binary.

## Build

Use a local PEAK install and BepInEx core path when building.

```powershell
dotnet build src/PeakSaveManager/PeakSaveManager.csproj -c Release -p:BepInExCorePath="F:/Video Games/SteamLibrary/steamapps/common/PEAK/backup/BepInEx/core"
```

## Test

```powershell
dotnet run --project tools/SaveVerificationTests/SaveVerificationTests.csproj -c Release
```

## What to include in issues

- Plugin version
- PEAK game version
- BepInEx version
- Singleplayer or multiplayer (host or client)
- Clear reproduction steps
- Expected result
- Actual result
- BepInEx log snippets if available

## Pull request guidelines

1. Fork the repo.
2. Create a branch from `main`.
3. Keep changes focused to one fix or one feature.
4. Update docs if behavior changed.
5. Update `CHANGELOG.md` when needed.
6. Open a PR with a clear test description.

## Testing checklist for gameplay changes

- Plugin loads without errors.
- Save list opens from pause menu.
- Quick Save works.
- Named Save works.
- Load works from Airport.
- Load works in an active run.
- Overwrite works.
- If the change touches multiplayer load logic, test with more than one player in the room.

## Scope notes

- Known bug: Shelf fungus and Bounce fungus can fail to restore in some saves.
- If your change touches fungus restore logic, include exact before and after behavior in the PR.
