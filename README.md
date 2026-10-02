# TaikoNauts ModLoader Installer

English | [日本語](README.ja.md)

A Windows installer that sets up the
[TaikoNauts ModLoader](https://github.com/aightallthing/taikonauts-mod-loader) and the
[NULM Background](https://github.com/36536pu4jt98g3ju095jk/taikonauts-nulm-background-mod)
mod for you. Select `TaikoNauts.exe`, press **Install**, and it does the rest.

## Use it

1. Close TaikoNauts and the Mod Manager.
2. Run `TaikoNautsModloaderInstaller.exe`.
3. Select `TaikoNauts.exe` with **Browse...** (or drag it onto the window). The
   installer remembers the last choice.
4. Press **Install**.
5. Put your NULM packs in the `Lumens` folder it created, then start the game.

The window shows what is already installed. Everything is optional: you can
untick the ModLoader, the mod, or the `Lumens` folder.

## What it does

1. **ModLoader.** Downloads the latest release of the ModLoader, extracts it into the
   game folder and runs the ModLoader's own `install.ps1`, so its checks of the
   original `raylib.dll` apply. An up-to-date ModLoader is not downloaded again.
2. **NULM Background.** Downloads the latest release of the mod into
   `mods\nulm-background`. An existing `config.json` and your packs are kept.
3. **Lumens.** Creates `Skins\<skin>\Lumens` in the skin the game uses (or the one
   you choose) with a short note on where packs go.

No NULM data or textures are installed; bring your own packs.

## Safety

- Downloads come from the two public GitHub repositories above only.
- Every download is checked against the SHA-256 digest GitHub publishes for the
  release file. A mismatch discards the file.
- Archive entries that would land outside the target folder are rejected.
- It never modifies `TaikoNauts.exe`. The ModLoader's installer refuses to replace an
  unknown or already modified `raylib.dll`, and the installer reports that message.
- It runs as the current user and does not ask for administrator rights. If the
  game is in a protected folder, run it as administrator.
- The executable is not code-signed, so Windows SmartScreen may warn you.

## Command line

```text
TaikoNautsModloaderInstaller.exe --game <path to TaikoNauts.exe> [options]
```

| Option | Meaning |
| --- | --- |
| `--no-loader` / `--no-mod` / `--no-lumens` | Skip a step. |
| `--skin <name>` | Skin that gets the `Lumens` folder (default: the one in use). |
| `--force` | Reinstall the ModLoader even when it is up to date. |
| `--loader-zip <file>` / `--mod-zip <file>` | Use a local package instead of downloading (offline installs). |
| `--log <file>` | Also write the log to a file. |
| `--lang ja\|en` | Language of the messages. |

Exit codes: `0` success, `1` the installation failed, `2` `TaikoNauts.exe` was not found.

## Uninstall

Run `uninstall.bat` in the game folder to restore the original `raylib.dll`, and delete
`mods\nulm-background` to remove the mod.

## Build

Requires the .NET 8 SDK on Windows.

```powershell
.\scripts\build.ps1
```

produces `dist\TaikoNautsModloaderInstaller.exe`, a self-contained single file (about 68 MB).
`tests\integration.ps1` installs into throw-away folders with local packages and needs no
network:

```powershell
.\tests\integration.ps1 -Exe .\dist\TaikoNautsModloaderInstaller.exe `
    -LoaderZip <ModLoader zip> -ModZip <mod zip> -OriginalRaylib <original raylib.dll>
```

## License

MIT. See [LICENSE](LICENSE).
