using TaoExif.Core;

namespace TaoExif.Tests;

public sealed class ExifToolServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;
    private readonly ExifToolService _svc;

    public ExifToolServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "taoexif-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        // non-ASCII name to prove the UTF-8 filename path works
        _file = Path.Combine(_dir, "pröbe ü.jpg");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "assets", "sample.jpg"), _file);
        var cmd = ExifToolLocator.Locate(null) ?? throw new InvalidOperationException("exiftool not found");
        _svc = new ExifToolService(cmd);
    }

    public void Dispose()
    {
        _svc.Dispose();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // temp leftovers are harmless
        }
    }

    [Fact]
    public async Task ReadsMetadataWithGroups()
    {
        var tags = await _svc.ReadMetadataAsync(_file, new ReadOptions());
        var make = Assert.Single(tags, t => t.Name == "Make");
        Assert.Equal("TestCam", make.Value);
        Assert.Equal("EXIF", make.Family0);
        Assert.Equal("IFD0", make.Family1);
        Assert.Equal("IFD0:Make", make.WriteName);
        Assert.Contains(tags, t => t is { Name: "Title", Family1: "XMP-dc", Value: "Sample" });
    }

    [Fact]
    public async Task WritesAndDeletesTags()
    {
        var r = await _svc.WriteTagsAsync([_file], [new TagWrite("XMP-dc:Title", "Ünïcode title"), new TagWrite("IFD0:Make", null)], new WriteOptions());
        Assert.Contains("1 image files updated", r.Text);
        var tags = await _svc.ReadMetadataAsync(_file, new ReadOptions());
        Assert.Contains(tags, t => t is { Name: "Title", Value: "Ünïcode title" });
        Assert.DoesNotContain(tags, t => t.Name == "Make");
        Assert.False(File.Exists(_file + "_original"));
    }

    [Fact]
    public async Task ShiftsDates()
    {
        await _svc.ShiftDatesAsync([_file], ["AllDates"], ExifToolService.FormatShift(0, 0, 1, 2, 0, 0), true, new WriteOptions());
        var cols = await _svc.ReadTagsAsync([_file], ["DateTimeOriginal"]);
        var row = cols[Path.GetFullPath(_file)];
        Assert.Equal("2024:05:07 09:08:09", ExifToolService.Lookup(row, "DateTimeOriginal"));
        Assert.Equal("2024:05:07 09:08:09", ExifToolService.Lookup(row, "ExifIFD:DateTimeOriginal"));
        Assert.Equal("2024:05:07 09:08:09", ExifToolService.Lookup(row, "EXIF:ExifIFD:DateTimeOriginal"));
    }

    [Fact]
    public async Task SetsAndClearsGps()
    {
        await _svc.SetGpsAsync([_file], new GeoPoint(-33.8568, 151.2153, 12), true, new WriteOptions());
        var gps = await _svc.ReadGpsAsync([_file]);
        var p = gps[Path.GetFullPath(_file)];
        Assert.Equal(-33.8568, p.Latitude, 4);
        Assert.Equal(151.2153, p.Longitude, 4);

        await _svc.SetGpsAsync([_file], null, true, new WriteOptions());
        Assert.Empty(await _svc.ReadGpsAsync([_file]));
    }

    [Fact]
    public async Task CopiesAndRemovesMetadata()
    {
        string target = Path.Combine(_dir, "target.jpg");
        File.Copy(_file, target);
        await _svc.RemoveMetadataAsync([target], MetaGroups.All, true, new WriteOptions());
        Assert.DoesNotContain(await _svc.ReadMetadataAsync(target, new ReadOptions()), t => t.Name == "Make");

        await _svc.CopyMetadataAsync(_file, [target], MetaGroups.Exif, new WriteOptions());
        var tags = await _svc.ReadMetadataAsync(target, new ReadOptions());
        Assert.Contains(tags, t => t is { Name: "Make", Value: "TestCam" });
        Assert.DoesNotContain(tags, t => t.Name == "Title");
    }

    [Fact]
    public async Task WritesKeywordListsAndExports()
    {
        await _svc.WriteTagsAsync([_file], [new TagWrite("XMP-dc:Subject", "one, two words, three")], new WriteOptions());
        var tags = await _svc.ReadMetadataAsync(_file, new ReadOptions());
        Assert.Equal("one, two words, three", Assert.Single(tags, t => t.Name == "Subject").Value);
        var csv = await _svc.ExportAsync([_file], true);
        Assert.Contains("XMP-dc:Subject", csv);
        var json = await _svc.ExportAsync([_file], false);
        Assert.Contains("\"two words\"", json);
    }

    [Fact]
    public async Task RemoveAllExceptKeepsChosenGroups()
    {
        await _svc.SetGpsAsync([_file], new GeoPoint(10, 20), false, new WriteOptions());
        await _svc.RemoveGroupsAsync([_file], true, ["GPS"], new WriteOptions());
        var tags = await _svc.ReadMetadataAsync(_file, new ReadOptions());
        Assert.Contains(tags, t => t.Family1 == "GPS");
        Assert.DoesNotContain(tags, t => t.Name is "Make" or "Title");
    }

    [Fact]
    public async Task RemoveOnlyChosenGroups()
    {
        await _svc.RemoveGroupsAsync([_file], false, ["XMP-dc"], new WriteOptions());
        var tags = await _svc.ReadMetadataAsync(_file, new ReadOptions());
        Assert.DoesNotContain(tags, t => t.Name == "Title");
        Assert.Contains(tags, t => t.Name == "Make");
    }

    [Fact]
    public async Task RenameDryRunDoesNotRename()
    {
        var r = await _svc.RenameAsync([_file], "DateTimeOriginal", "%Y%m%d_%H%M%S%%-c.%%e", true);
        Assert.Contains("20240506_070809.jpg", r.Text);
        Assert.True(File.Exists(_file));
    }

    [Fact]
    public async Task RawCommandAndBinaryOutput()
    {
        var r = await _svc.RunRawAsync("exiftool -s3 \"-Model\"", [_file]);
        Assert.Equal("Model X", r.Text.Trim());
        // no thumbnail in the sample: binary extraction must return null, not hang
        Assert.Null(await _svc.ExtractBinaryAsync(_file, "ThumbnailImage"));
    }

    [Fact]
    public async Task ErrorsAreReported()
    {
        var r = await _svc.ReadMetadataAsync(Path.Combine(_dir, "missing.jpg"), new ReadOptions());
        Assert.Empty(r);
        var raw = await _svc.RunRawAsync("-Make", [Path.Combine(_dir, "missing.jpg")]);
        Assert.Contains("not found", raw.Errors, StringComparison.OrdinalIgnoreCase);
    }
}

public class LocatorTests
{
    [Fact]
    public void FindsBundledCopy()
    {
        string dir = Path.Combine(Path.GetTempPath(), "taoexif-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, ExifToolLocator.BundleFolder));
        try
        {
            Assert.Null(ExifToolLocator.FindBundled(dir));
            string name = OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool";
            File.WriteAllText(Path.Combine(dir, ExifToolLocator.BundleFolder, name), "");
            var cmd = ExifToolLocator.FindBundled(dir);
            Assert.NotNull(cmd);
            Assert.Equal(ExifToolSource.Bundled, cmd.Source);
            Assert.EndsWith(name, cmd.ExifToolPath);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task BundledExifToolRuns()
    {
        // third_party/exiftool is filled by tools/fetch-exiftool.ps1 and copied next to the test assembly
        var cmd = ExifToolLocator.FindBundled(AppContext.BaseDirectory);
        Assert.NotNull(cmd);
        using var svc = new ExifToolService(cmd);
        Assert.Matches(@"^\d+\.\d+$", await svc.GetVersionAsync());
    }

    [Fact]
    public void ConfiguredPathWins()
    {
        string file = Path.GetTempFileName();
        try
        {
            var cmd = ExifToolLocator.Locate(file);
            Assert.Equal(ExifToolSource.Configured, cmd?.Source);
            Assert.Equal(file, cmd?.FileName);
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public class ParsingTests
{
    [Theory]
    [InlineData("-a -b", new[] { "-a", "-b" })]
    [InlineData("\"-FileName<${Model} x\" -d '%Y %m'", new[] { "-FileName<${Model} x", "-d", "%Y %m" })]
    [InlineData("  ", new string[0])]
    [InlineData("-Title=\"\"", new[] { "-Title=" })]
    public void SplitsCommandLines(string text, string[] expected) => Assert.Equal(expected, CommandLine.Split(text));

    [Fact]
    public void FindsMarker()
    {
        var m = "{ready7}"u8;
        Assert.Equal(3, ExifToolProcess.FindMarkerAtEnd("abc{ready7}\r\n"u8, m));
        Assert.Equal(0, ExifToolProcess.FindMarkerAtEnd("{ready7}\n"u8, m));
        Assert.Equal(-1, ExifToolProcess.FindMarkerAtEnd("{ready7}"u8, m));
        Assert.Equal(-1, ExifToolProcess.FindMarkerAtEnd("{ready17}\n"u8, m));
    }

    [Fact]
    public void ParsesDuplicateKeysAndArrays()
    {
        const string json = """[{"SourceFile":"a.jpg","EXIF:IFD0:Make":"A","EXIF:IFD1:Make":"B","XMP:XMP-dc:Subject":["x","y"],"File:FileType":"JPEG","Composite:Megapixels":9.2}]""";
        var tags = ExifToolService.ParseMetadataJson(json);
        Assert.Equal(5, tags.Count);
        Assert.Equal("x, y", tags[2].Value);
        Assert.Equal("File", tags[3].Family1);
        Assert.True(tags[4].IsReadOnly);
        Assert.False(tags[0].IsReadOnly);
    }

    [Fact]
    public void RemoveArgsAllOrSome()
    {
        Assert.Equal(["-all=", "-tagsFromFile", "@", "-GPS:all", "-ICC_Profile", "-MakerNotes"], ExifToolService.RemoveArgs(true, ["GPS", "ICC_Profile", "gps", "MakerNotes"]));
        Assert.Equal(["-all="], ExifToolService.RemoveArgs(true, []));
        Assert.Equal(["-MakerNotes:all=", "-XMP-crs:all="], ExifToolService.RemoveArgs(false, ["MakerNotes", "XMP-crs"]));
    }

    [Fact]
    public void GpsArgsUseRefs()
    {
        var args = ExifToolService.GpsWriteArgs(new GeoPoint(-1.5, -2.25), false).ToList();
        Assert.Contains("-GPS:GPSLatitudeRef=S", args);
        Assert.Contains("-GPS:GPSLongitude=2.25", args);
        Assert.Contains("-GPS:GPSLongitudeRef=W", args);
    }
}
