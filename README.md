# SvZ-Modding

A mod loader and mod manager for the Windows PC build of **Samurai vs Zombies Defense**
(the Unity 2017.4 build, `SvZ Defense Cheats.exe`).

- **SvZ Mod Loader** - a [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin. Mods are plain folders
  that change the game's data (stats, waves, enemies, store), text, textures, sounds and models without
  touching any game file. It can also replace characters with rigged glTF models from Blender, driven by
  the game's animations or by the model's own, and turn a character's shots into lasers.
- **SvZ Mod Manager** - a Windows app to install or remove the loader, turn mods and parts of mods
  (data, text, textures, sounds, models) on and off, set load order, and start the game.

How to play with mods and how to make them: **[MODDING.md](MODDING.md)**. Example mods: [examples](examples).

This repository contains only the tools' own code. It does not include the game, BepInEx, or any game or
Plants vs. Zombies assets; you need your own copy of the game. Not affiliated with Glu Mobile or
Electronic Arts.

## Using it

1. Build or download `SvZ Mod Manager.exe` (see below) and put it in the game folder.
2. Run it, click **Install loader**, add mods, click **Play**.

Requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

## Building

You need the .NET 8 SDK and two things this repository doesn't include:

- a copy of the game in `game\` (the loader compiles against the game's own DLLs in
  `game\SvZ Defense Cheats_Data\Managed`; to use another location, add `-p:GameDir=<folder>`)
- BepInEx 5.4.23.5 for Windows x64, saved as `downloads\BepInEx_win_x64_5.4.23.5.zip`
  ([release page](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)) - the manager installs it

```
dotnet publish SvZModManager -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

That builds the loader too and embeds it, with BepInEx, in `publish\SvZ Mod Manager.exe`.
`dotnet build SvZModLoader` alone builds the plugin and copies it into `game\BepInEx\plugins`.

## Layout

| Folder | |
|---|---|
| `SvZModLoader` | the BepInEx plugin (.NET 3.5, matching the game's Mono runtime) |
| `SvZModManager` | the manager (.NET 8 WinForms) |
| `Shared` | the mod folder layout and `modlist.txt` format, compiled into both |
| `examples` | example mods |
| `work` | Blender scripts: convert, rig and animate models for mods |
| `tools/SaveTool` | decrypt and re-encrypt the game's `save.data` |
| `devtools/SvZTestDriver` | test-only plugin for scripted taps and screenshots; don't ship it |
