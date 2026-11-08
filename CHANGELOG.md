# Changelog

All notable changes to this fork are documented here.

## 1.4.1.1

### Fixed

- Restored the vanilla map markers when the plugin is unloaded while the map is open.

## 1.4.1.0

### Added

- Clicking a class name in the **Hunty Alerts** window now places a red flag on every known
  spawn point of that class's still-pending hunting log monsters in the current zone.
- Map flags refresh automatically when an entry is completed, so only the remaining spawn
  points stay marked. Clicking the class name again removes its flags.

### Changed

- Removed the **Kills left** column from the alert window. Pending kill counts are still shown
  in the class tooltip.

## 1.4.0.0

### Added

- New **Hunty Alerts** window: shows how many hunting log entries are still pending in the
  zone you are currently in, for every class you choose to watch, and how many kills are left.
  Hovering a class lists the pending monsters with their current progress.
- New **Alerts** tab in the configuration window to show/hide the alert window and pick the
  watched classes (`Select all` / `Clear all` shortcuts included).
- New `/huntyalert` command to toggle the alert window. Its visibility is persisted across
  restarts.

### Changed

- The fork is now published through its own Dalamud custom plugin repository (`repo.json`).
  Because the official repository already ships a plugin named `Hunty`, Dalamud would ignore
  a third-party entry with the same internal name, so this fork uses the internal name
  **`HuntyFork`** and is shown as **Hunty (fork)**. The assembly, manifest and resource
  assemblies were renamed accordingly; the namespace, commands and configuration stay the same.
- Added custom repository installation instructions to the README, including a note about
  disabling the official `Hunty` to avoid `/hunty*` command conflicts.

## 1.3.8.0

### Fixed

- Removed the `bNpcNames.json` file that was written to the plugin directory on every load and was never read.
- Hunting data is no longer left `null` when `monsters.json` is missing or invalid; the UI now shows a message instead of throwing.
- Guarded empty and out-of-range rank/area selections in the main, XL and Companion windows.
- Bounded the task/monster progress reads so mismatches between `monsters.json` and the game memory cannot throw.
- Progress is now keyed by monster ID instead of by localized name.
- Fixed Grand Company detection for characters without a Grand Company.
- Guarded `MonsterNoteManager` access while the player is not logged in.
- Unsupported UI languages now fall back to the neutral language instead of throwing.

### Changed

- The Companion window now computes progress once per job instead of twice per monster, handles an empty area list, and uses the duty flag of the displayed location.
- The XL window caches area grouping per job and rank instead of rebuilding it every frame.
- The Teleport availability check now runs on first access and uses a clearer field name.
- Added a localized "Nothing to hunt in this area." message (EN/DE/FR/JA/ZH).
- Updated fork metadata, issue links and version to `1.3.8.0`.

### Added

- GitHub Actions build workflow.
