# *Hunty*  

Let's go hunt together, finally finishing that old log of yours~

> **Fork notice:** this repository is a fork of [Infiziert90/Hunty](https://github.com/Infiziert90/Hunty)
> maintained by [kngbong](https://github.com/kngbong). It includes stability and performance fixes;
> see [CHANGELOG.md](CHANGELOG.md). Bug reports for this fork belong in the
> [fork's issue tracker](https://github.com/kngbong/Hunty/issues).

## Features  
+ List of monsters in the same location based on Rank
+ Click the coords to open your map on the exact location
+ Click the dungeon name to open your duty finder
+ Easy switching between Grand Company and jobs
+ Automatic selection based on current job
+ Tracks all of the progress for you
+ Alert window with pending entries in your current zone for the classes you choose
+ Click a class in the alert window to flag every remaining spawn point on the map
+ Localizations for EN, DE, FR, JP and ZH  

## Guide Book  
![Book](Hunty/images/hunty.png)

## XL Book
![Book](Hunty/images/xl.png)

## Companion Book
![Book](Hunty/images/companion.png)

## Localized languages (de, fr, jp)  
![Japanese](Hunty/images/loc.png)

## Installation (custom plugin repository)

This fork is published through its own Dalamud plugin repository, so it can update automatically.
It is installed as **Hunty (fork)** with the internal name `HuntyFork`.

1. Install the [FFXIV Quick Launcher](https://github.com/goatcorp/FFXIVQuickLauncher) and log into the game with Dalamud enabled.
2. Run `/xlsettings` and open the **Experimental** tab.
3. Under **Custom Plugin Repositories**, add this URL and enable it:

   ```
   https://raw.githubusercontent.com/kngbong/Hunty/master/repo.json
   ```

4. Click **Save** (the `+` button) and close the window.
5. Run `/xlplugins`, search for **Hunty (fork)** and click **Install**.

> **Important:** if you already have the official **Hunty** installed, disable or uninstall it first.
> Both plugins register the same `/hunty`, `/huntyxl`, `/huntycompanion` and `/huntyalert`
> commands, so keeping them enabled together causes command conflicts.

### Commands

| Command | Action |
| --- | --- |
| `/hunty` | Main window |
| `/huntyxl` | XL guide book |
| `/huntycompanion` | Guide book grouped by area |
| `/huntyalert` | Toggle the hunting log alert window |

The alert window can also be shown/hidden and configured (which classes to watch) from the
plugin configuration window, **Alerts** tab.

Clicking a class name in the alert window places a red flag on every known spawn point of that
class's pending monsters in the current zone. The flags refresh automatically as entries are
completed, and clicking the class name again removes them.

### Building from source

```powershell
dotnet build .\Hunty.sln --configuration Release
```

The packaged release is written to `Hunty\bin\Release\HuntyFork\latest.zip`.

## Development

A Dalamud plugin for Final Fantasy XIV. This repository is for development; end users should
install it through the custom repository described above. See the
[FAQ](https://goatcorp.github.io/faq/dalamud_troubleshooting.html#q-how-do-i-installenable-plugins)
for general Dalamud plugin help.
  
  
## Thanks
#### Thank you MidoriKami for helping with the tracking