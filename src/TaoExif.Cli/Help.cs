namespace TaoExif.Cli;

internal static class Help
{
    private const string General = """
        taoexif-cli - TaoExif from the command line (same engine and bundled ExifTool as the app)

        Usage: taoexif-cli <command> [options] <files | folders | *.jpg ...>

        Commands
          info      show all metadata
          get       show chosen tags (tab-separated, or --json)
          set       write tags                       -t TAG=VALUE ...   (empty VALUE deletes)
          remove    remove metadata                  --all [--keep GROUPS] | --groups GROUPS
          shift     shift dates                      --by [+|-]Y:M:D h:m:s [--tags AllDates,...]
          rename    rename files                     [--format FMT] [--date-tag TAG] | --template T  [--dry-run]
          gps       GPS                              set --lat LAT --lon LON [--alt M] [--xmp] | clear | show
          geotag    geotag from a track log          --track FILE [--sync +1:00:00]
          copy      copy metadata from a file        --from FILE [--groups EXIF,XMP,IPTC,GPS,MakerNotes,ICC]
          export    export metadata                  --json | --csv  [-o FILE]
          run       pass arguments to exiftool       run ARGS... [-- FILES...]
          version   show TaoExif and exiftool versions
          update    check GitHub for a newer TaoExif [--check: exit code 10 when one exists]
          help      help for a command: taoexif-cli help remove

        Common options
          -r, --recursive        include subfolders of folder arguments
          --ext jpg,cr2          file types to take from folders (default: images, RAW, video, audio)
          --json                 JSON output (info, get, gps show, version, update)
          -q, --quiet            no exiftool summary lines
          --keep-backup          keep exiftool's *_original backup files
          -P, --preserve-dates   keep the file modified date when writing
          --exiftool PATH        use this exiftool (default: Settings path, installed, then bundled)

        Exit codes: 0 ok, 1 exiftool reported errors, 2 bad usage, 3 exiftool not found.

        Examples
          taoexif-cli info photo.jpg
          taoexif-cli get -t Model -t DateTimeOriginal -r ~/Pictures/2025 --json
          taoexif-cli set -t XMP-dc:Title="Sunset" -t IFD0:Artist="Me" *.jpg
          taoexif-cli remove --all --keep ICC_Profile,GPS photos/
          taoexif-cli shift --by "+0:0:0 1:00:00" -r trip/
          taoexif-cli rename --dry-run --format "%Y%m%d_%H%M%S%%-c.%%e" .
          taoexif-cli gps set --lat 48.8584 --lon 2.2945 eiffel.jpg
          taoexif-cli run -G1 -s -time:all -- photo.jpg

        """;

    private static readonly Dictionary<string, string> Commands = new()
    {
        ["info"] = """
            taoexif-cli info [--group G] [--numeric] [--duplicates] [--json] FILES
              Shows every tag as Group1:Tag : value (the name you can write with 'set').
              --group EXIF|XMP|IPTC|GPS|XMP-dc|...  only that group
              -n, --numeric      raw values (exiftool -n)
              --duplicates       include duplicate tags from other groups (exiftool -a)

            """,
        ["get"] = """
            taoexif-cli get -t TAG [-t TAG ...] | --tags A,B  [--json] FILES
              One line per file: path, then a tab-separated value per tag (empty when missing).
              Tags may carry a group: Model, ExifIFD:ISO, XMP-dc:Title, Composite:GPSPosition.

            """,
        ["set"] = """
            taoexif-cli set -t TAG=VALUE [-t TAG=VALUE ...] FILES
              Writes the tags into every file. An empty value deletes the tag (-t XMP-dc:Title=).
              List tags take comma-separated values: -t XMP-dc:Subject="travel, beach".

            """,
        ["remove"] = """
            taoexif-cli remove --all [--keep GROUPS] FILES
            taoexif-cli remove --groups GROUPS FILES
              --all removes everything except the --keep groups; --groups removes only those.
              Groups: EXIF, MakerNotes, GPS, IFD1, XMP, XMP-exif, XMP-acdsee, XMP-mediapro, XMP-photoshop,
                      XMP-crs, XMP-microsoft, XMP-tiff, XMP-pdf, IPTC, Photoshop, JFIF, ICC_Profile.
              The exiftool command is shown on stderr (hide it with -q).

            """,
        ["shift"] = """
            taoexif-cli shift --by [+|-]"Y:M:D h:m:s" [--tags AllDates,FileModifyDate] FILES
              Moves dates later (+, default) or earlier (-). Shorter forms work: --by "-1:00:00" (one hour back).
              Default tags: AllDates (DateTimeOriginal, CreateDate, ModifyDate).

            """,
        ["rename"] = """
            taoexif-cli rename [--format FMT] [--date-tag TAG] [--dry-run] FILES
            taoexif-cli rename --template TEMPLATE [--dry-run] FILES
              --format    strftime codes, default "%Y%m%d_%H%M%S%%-c.%%e" (%%-c adds a counter on clashes)
              --date-tag  default DateTimeOriginal
              --template  exiftool name template, e.g. "${Model;tr/ /_/}_%f.%e"
              --dry-run   only show the new names

            """,
        ["gps"] = """
            taoexif-cli gps set --lat LAT --lon LON [--alt METRES] [--xmp] FILES
            taoexif-cli gps clear FILES
            taoexif-cli gps show [--json] FILES
              Decimal degrees; negative for south and west. --xmp also writes XMP GPS tags.

            """,
        ["geotag"] = """
            taoexif-cli geotag --track LOG [--sync +1:00:00] FILES
              Matches DateTimeOriginal against a GPX/KML/NMEA/IGC track. --sync corrects the camera clock.

            """,
        ["copy"] = """
            taoexif-cli copy --from SOURCE [--groups EXIF,XMP,IPTC,GPS,MakerNotes,ICC] FILES
              Copies everything writable (or only the groups) from SOURCE into each file.

            """,
        ["export"] = """
            taoexif-cli export --json|--csv [-o FILE] FILES
              All tags (-G1 -a) of the files as JSON or CSV, to stdout or FILE.

            """,
        ["run"] = """
            taoexif-cli run EXIFTOOL-ARGS... [-- FILES...]
              Runs exiftool with the arguments untouched, then the files (folders and wildcards expanded).
              Example: taoexif-cli run -G1 -s "-FileModifyDate<DateTimeOriginal" -- *.jpg

            """,
    };

    public static string Text(string? command) =>
        command != null && Commands.TryGetValue(command.ToLowerInvariant(), out var t) ? t : General;
}
