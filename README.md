# TaoExif

A cross-platform, dark-mode GUI for Phil Harvey's [ExifTool](https://exiftool.org/), inspired by
[ExifToolGui](https://github.com/FrankBijnen/ExifToolGui) (Bogdan Hrastnik, Frank Bijnen). It's a new
implementation in C# / .NET 10 with [Avalonia 12](https://avaloniaui.net/), and it runs on Windows, Linux and macOS.

![TaoExif details view with the Metadata tab](screenshots/main-details.png)

## Download

Get the latest build from [Releases](https://github.com/Taoism/TaoExif/releases). ExifTool is included in every
package.

| Platform | File | How to install |
|---|---|---|
| Windows 10/11 | `TaoExif-<v>-setup-win-x64.exe` | Run the installer. It installs per user, or for all users if you choose that, and can add "Open in TaoExif" to folder right-click menus. |
| Windows, portable | `TaoExif-<v>-win-x64.zip` | Unzip anywhere and run `TaoExif.exe`. |
| Ubuntu / Debian | `taoexif_<v>_amd64.deb` | `sudo apt install ./taoexif_<v>_amd64.deb`, then start TaoExif from the menu or run `taoexif`. |
| Other Linux | `TaoExif-<v>-linux-x64.tar.gz` | Unpack and run `./TaoExif`. The bundled ExifTool needs `perl`. |
| macOS, Apple silicon | `TaoExif-<v>-macos-arm64.zip` | Unzip and move `TaoExif.app` to Applications. |
| macOS, Intel | `TaoExif-<v>-macos-x64.zip` | Same as above. |

The macOS app isn't notarized. The first time, right-click it and choose **Open**, or run
`xattr -dr com.apple.quarantine /Applications/TaoExif.app`.

To build all of these yourself, run `./build-release.ps1` (Windows with Inno Setup 6 and WSL).

## Features

- **Browse**: folder tree, path box, recent folders, optional subfolders, name filter. Details view with
  configurable tag columns, or a thumbnail view (EXIF orientation applied; RAW/HEIC use the embedded preview).
- **Preview** of the selected file (JPEG/PNG/WebP decoded directly, RAW/HEIC/TIFF via exiftool's embedded JPEGs).
- **Metadata tab**: every tag grouped by family 0/1 (EXIF / IFD0, XMP / XMP-dc, MakerNotes …), with filter and
  group selector. You can edit or delete a single tag across all selected files. Right-click a tag to copy it, or
  add it as a file list column or a workspace field.
- **Workspace tab**: your own list of frequently edited fields (title, keywords, copyright, city, …). When several
  files are selected it shows whether their values differ, and it writes only the fields you changed (Ctrl+S).
- **Map tab**: built-in OpenStreetMap map, drawn natively (no browser engine). It shows GPS markers for the folder,
  sets a location by clicking the map or typing coordinates, removes GPS, and draws GPX tracks.
- **Command tab**: run any exiftool command on the selection, the folder or no files, with saved presets.
- **Tools**: shift date/time (EXIF, XMP, QuickTime, file date), rename from a date tag or a name template with a
  dry-run preview, copy metadata from a file (all or chosen groups), remove metadata (all, keeping ICC, or by
  group), geotag from a GPX/KML/NMEA log with a time correction, delete `*_original` backups, export the
  selection as CSV/JSON.
- **Right-click menu** on files, thumbnails and the preview: remove metadata, remove GPS, copy metadata, shift
  dates, rename, geotag, export, open, show in folder.
- **Remove metadata** with ExifToolGui-style choices: remove everything (keeping only what you untick), or just
  chosen groups (EXIF, maker notes, GPS, IFD1, XMP and its sub-groups, IPTC, Photoshop, JFIF, ICC profile).
- **Light, dark, or follow the system**: by default the theme follows the operating system and switches live
  when the OS changes. Dark mode is dark everywhere, including the message boxes and dialogs.
- **Updates**: checks GitHub once a day. Choose off, notify, or install automatically. Downloads are checked
  against the release's SHA-256 list before anything runs. The Windows installer version updates itself (when
  you close TaoExif, or straight away with *Restart now*). Other packages get the verified download.
- **Command line**: `taoexif-cli` for scripts (see below).

| Thumbnails and map | Remove metadata |
|---|---|
| ![Thumbnails and map](screenshots/main-map.png) | ![Remove metadata dialog](screenshots/remove-metadata.png) |

exiftool runs as a persistent `-stay_open` process, so reads are fast, and file names are passed as UTF-8, so
non-ASCII paths work on Windows.

## Command line

Every package includes `taoexif-cli`, which uses the same engine and bundled ExifTool as the app:

- **Windows**: next to `TaoExif.exe`. The installer can add it to PATH.
- **Debian/Ubuntu**: `/usr/bin/taoexif-cli`.
- **macOS**: `TaoExif.app/Contents/MacOS/taoexif-cli`. To use it from anywhere, run
  `sudo ln -s /Applications/TaoExif.app/Contents/MacOS/taoexif-cli /usr/local/bin/`.

```
taoexif-cli info photo.jpg                                    # every tag, as Group:Tag : value
taoexif-cli get -t Model -t DateTimeOriginal -r ~/Pictures --json
taoexif-cli set -t XMP-dc:Title="Sunset" -t IFD0:Artist="Me" *.jpg
taoexif-cli remove --all --keep ICC_Profile,GPS photos/       # same choices as the Remove metadata dialog
taoexif-cli shift --by "+0:0:0 1:00:00" -r trip/
taoexif-cli rename --dry-run --format "%Y%m%d_%H%M%S%%-c.%%e" .
taoexif-cli gps set --lat 48.8584 --lon 2.2945 eiffel.jpg
taoexif-cli geotag --track ride.gpx --sync +1:00:00 *.jpg
taoexif-cli export --csv -r photos/ -o metadata.csv
taoexif-cli run -G1 -s -time:all -- photo.jpg                 # any exiftool arguments
taoexif-cli update --check                                    # exit code 10 when a newer TaoExif exists
```

Folders are expanded (add `-r` to include subfolders; `--ext jpg,cr2` limits the file types), and wildcards work
in cmd.exe too. `get`, `info`, `gps show`, `version` and `update` take `--json`. Exit codes: 0 ok, 1 exiftool
reported errors, 2 bad usage, 3 exiftool not found. `taoexif-cli help <command>` shows the details.

## ExifTool

TaoExif ships with ExifTool (currently 13.63) in an `exiftool` folder next to the app, so it works without
anything else installed:

- **Windows**: the official Windows package (`exiftool.exe` with its own Perl).
- **Linux / macOS**: the official Perl distribution, run with the system `perl`. macOS and most Linux desktops
  include Perl. On minimal installs without it, install Perl or exiftool itself
  (`sudo dnf install perl-Image-ExifTool`, `sudo apt install libimage-exiftool-perl`, `brew install exiftool`).

The order it uses: the path set in **Edit → Settings**, then an exiftool installed on the system (PATH, Program
Files, Homebrew, /usr/bin …), then the bundled copy. The status bar shows which one is in use, e.g.
"exiftool 13.63 (bundled)".

The bundle is downloaded by `tools/fetch-exiftool.ps1`, which checks each file against exiftool.org's SHA-256 list.
The build runs it automatically when `third_party/exiftool` is missing. To update to a newer ExifTool, run
`tools/fetch-exiftool.ps1 -Force`. To build without a bundle, pass `-p:TaoExifBundleExifTool=false`.

## Build

```
dotnet build TaoExif.sln
dotnet test tests/TaoExif.Tests          # uses an installed exiftool, else the bundled one
dotnet publish src/TaoExif -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
dotnet publish src/TaoExif -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/linux-x64
dotnet publish src/TaoExif -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/osx-arm64
```

## Layout

- `src/TaoExif.Cli`: `taoexif-cli`.
- `src/TaoExif.Core`: the exiftool process wrapper (`ExifToolProcess`), the high-level operations
  (`ExifToolService`), settings, and the GPX reader. It has no UI dependencies.
- `src/TaoExif`: the Avalonia app (main window split into `MainWindow.*.cs`, dialogs, map control, theme).
- `tests/TaoExif.Tests`: xUnit tests that run against the real exiftool.

Settings are stored in `%APPDATA%\TaoExif\settings.json` (Windows) or `~/.config/TaoExif/settings.json`
(Linux/macOS). Map tiles are cached under `tiles/` next to the settings file.

Map data © OpenStreetMap contributors.

## License

MIT, see [LICENSE](LICENSE). ExifTool is © Phil Harvey and is distributed under the same terms as Perl; see
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
