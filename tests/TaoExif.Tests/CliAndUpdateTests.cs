using System.Runtime.InteropServices;
using TaoExif.Cli;
using TaoExif.Core;

namespace TaoExif.Tests;

public class CliArgsTests
{
    [Fact]
    public void ParsesCommandOptionsAndFiles()
    {
        var a = CliArgs.Parse(["--json", "get", "-t", "Model", "--tags", "ISO,FNumber", "-r", "a.jpg", "dir"]);
        Assert.Equal("get", a.Command);
        Assert.True(a.Has("--json"));
        Assert.True(a.Has("--recursive"));
        Assert.Equal(["Model"], a.Values("--tag"));
        Assert.Equal(["ISO", "FNumber"], a.List("--tags"));
        Assert.Equal(["a.jpg", "dir"], a.Positional);
    }

    [Fact]
    public void OptionValuesMayStartWithMinus()
    {
        var a = CliArgs.Parse(["gps", "set", "--lat", "-33.85", "--lon=151.2", "x.jpg"]);
        Assert.Equal("-33.85", a.Value("--lat"));
        Assert.Equal("151.2", a.Value("--lon"));
        Assert.Equal(["set", "x.jpg"], a.Positional);
        Assert.Equal("-1:00:00", CliArgs.Parse(["shift", "--by", "-1:00:00", "f"]).Value("--by"));
    }

    [Fact]
    public void RunKeepsExiftoolArgumentsUntouched()
    {
        var a = CliArgs.Parse(["run", "-G1", "-s", "-FileModifyDate<DateTimeOriginal", "--", "a.jpg", "b.jpg"]);
        Assert.Equal(["-G1", "-s", "-FileModifyDate<DateTimeOriginal"], a.Raw);
        Assert.Equal(["a.jpg", "b.jpg"], a.Positional);
    }

    [Fact]
    public void RejectsUnknownOptionsAndMissingValues()
    {
        Assert.Throws<UsageException>(() => CliArgs.Parse(["info", "--bogus", "x"]));
        Assert.Throws<UsageException>(() => CliArgs.Parse(["set", "-t"]));
        Assert.Throws<UsageException>(() => CliArgs.Parse(["info", "--json=1", "x"]));
    }

    [Fact]
    public void ParsesTagWrites()
    {
        var w = Program.ParseWrites(CliArgs.Parse(["set", "-t", "XMP-dc:Title=Hello = world", "-t", "IFD0:Artist=", "f"]));
        Assert.Equal(new TagWrite("XMP-dc:Title", "Hello = world"), w[0]);
        Assert.Equal(new TagWrite("IFD0:Artist", ""), w[1]);
        Assert.Throws<UsageException>(() => Program.ParseWrites(CliArgs.Parse(["set", "-t", "NoEquals", "f"])));
    }

    [Fact]
    public void ExifToolErrorsAreDetected()
    {
        Assert.True(Program.HasErrors(new ExifToolResult([], "Error: File not found - x.jpg")));
        Assert.True(Program.HasErrors(new ExifToolResult([], "Warning: Tag 'X' is not defined\nNothing to do.")));
        Assert.False(Program.HasErrors(new ExifToolResult("    1 image files updated\n"u8.ToArray(), "Warning: minor")));
    }

    [Fact]
    public void ExpandsFoldersAndPatterns()
    {
        string dir = Path.Combine(Path.GetTempPath(), "taoexif-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        try
        {
            foreach (var f in new[] { "a.jpg", "b.CR2", "notes.txt", "c.jpg_original", Path.Combine("sub", "d.jpg") })
                File.WriteAllText(Path.Combine(dir, f), "");
            Assert.Equal(["a.jpg", "b.CR2"], FileArgs.Expand([dir], false, null).Select(Path.GetFileName));
            Assert.Equal(3, FileArgs.Expand([dir], true, null).Count);
            Assert.Equal(["a.jpg", "d.jpg"], FileArgs.Expand([dir], true, ["jpg"]).Select(Path.GetFileName));
            Assert.Equal(["a.jpg"], FileArgs.Expand([Path.Combine(dir, "*.jpg")], false, null).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class UpdateTests
{
    private const string ReleaseJson = """
        {"tag_name":"v0.3.2","name":"TaoExif 0.3.2","html_url":"https://github.com/Taoism/TaoExif/releases/tag/v0.3.2","body":"notes",
         "assets":[
          {"name":"TaoExif-0.3.2-setup-win-x64.exe","browser_download_url":"https://github.com/Taoism/TaoExif/releases/download/v0.3.2/TaoExif-0.3.2-setup-win-x64.exe","size":10},
          {"name":"TaoExif-0.3.2-win-x64.zip","browser_download_url":"https://github.com/x/y/z.zip","size":10},
          {"name":"taoexif_0.3.2_amd64.deb","browser_download_url":"https://github.com/x/y/z.deb","size":10},
          {"name":"TaoExif-0.3.2-linux-x64.tar.gz","browser_download_url":"https://github.com/x/y/z.tgz","size":10},
          {"name":"TaoExif-0.3.2-macos-arm64.zip","browser_download_url":"https://github.com/x/y/a.zip","size":10},
          {"name":"TaoExif-0.3.2-macos-x64.zip","browser_download_url":"https://github.com/x/y/i.zip","size":10},
          {"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/x/y/s.txt","size":10}]}
        """;

    [Fact]
    public void ParsesReleaseAndPicksAssets()
    {
        var r = UpdateChecker.ParseRelease(ReleaseJson)!;
        Assert.Equal(new Version(0, 3, 2), r.Version);
        Assert.Equal("TaoExif-0.3.2-setup-win-x64.exe", UpdateChecker.PickAsset(r, InstallKind.WindowsInstaller, Architecture.X64)!.Name);
        Assert.Equal("TaoExif-0.3.2-win-x64.zip", UpdateChecker.PickAsset(r, InstallKind.WindowsPortable, Architecture.X64)!.Name);
        Assert.Equal("taoexif_0.3.2_amd64.deb", UpdateChecker.PickAsset(r, InstallKind.LinuxDeb, Architecture.X64)!.Name);
        Assert.Equal("TaoExif-0.3.2-macos-arm64.zip", UpdateChecker.PickAsset(r, InstallKind.MacApp, Architecture.Arm64)!.Name);
        Assert.Equal("TaoExif-0.3.2-macos-x64.zip", UpdateChecker.PickAsset(r, InstallKind.MacApp, Architecture.X64)!.Name);
        Assert.Null(UpdateChecker.PickAsset(r, InstallKind.Unknown, Architecture.X64));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("0.2", "0.2.0")]
    [InlineData("v2.0.0-beta", "2.0.0")]
    public void ParsesVersions(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParseVersion(tag, out var v));
        Assert.Equal(Version.Parse(expected), v);
    }

    [Fact]
    public void ComparesVersions()
    {
        Assert.True(UpdateChecker.IsNewer(new Version(0, 2, 0), new Version(0, 1, 1, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(0, 1, 1), new Version(0, 1, 1, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(0, 1, 0), new Version(0, 1, 1)));
    }

    [Fact]
    public void ParsesSumsFiles()
    {
        string h = new('a', 64), g = new('B', 64);
        var map = UpdateChecker.ParseSums($"{h}  TaoExif-1.zip\r\n{g} *taoexif_1_amd64.deb\nnot a line\n");
        Assert.Equal(h, map["TaoExif-1.zip"]);
        Assert.Equal(g.ToLowerInvariant(), map["taoexif_1_amd64.deb"]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public async Task DownloadIsVerifiedAgainstSums()
    {
        byte[] payload = "installer bytes"u8.ToArray();
        string good = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
        var r = UpdateChecker.ParseRelease(ReleaseJson)!;
        var asset = r.Assets[0];
        string dir = Path.Combine(Path.GetTempPath(), "taoexif-upd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ok = new UpdateChecker(new HttpClient(new FakeHandler(payload, $"{good}  {asset.Name}\n")));
            string path = await ok.DownloadVerifiedAsync(r, asset, dir);
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));

            var bad = new UpdateChecker(new HttpClient(new FakeHandler(payload, $"{new string('0', 64)}  {asset.Name}\n")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => bad.DownloadVerifiedAsync(r, asset, dir));
            Assert.False(File.Exists(Path.Combine(dir, asset.Name)));

            var unlisted = new UpdateChecker(new HttpClient(new FakeHandler(payload, $"{good}  other.zip\n")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => unlisted.DownloadVerifiedAsync(r, asset, dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private sealed class FakeHandler(byte[] payload, string sums) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath.EndsWith(".txt", StringComparison.Ordinal)
                ? new StringContent(sums)
                : (HttpContent)new ByteArrayContent(payload);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
