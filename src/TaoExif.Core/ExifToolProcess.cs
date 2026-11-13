using System.Diagnostics;
using System.Text;

namespace TaoExif.Core;

/// <summary>Result of one exiftool command: raw stdout bytes and stderr text.</summary>
public sealed record ExifToolResult(byte[] Output, string Errors)
{
    public string Text => Encoding.UTF8.GetString(Output);
}

/// <summary>
/// A persistent exiftool process (-stay_open True -@ -). Commands are serialized;
/// output is read as raw bytes so binary extraction (-b) works on every OS.
/// </summary>
public sealed class ExifToolProcess : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ExifToolCommand _command;
    private Process? _process;
    private Stream? _stdout;
    private Stream? _stderr;
    private int _sequence;
    private bool _disposed;

    public ExifToolProcess(ExifToolCommand command)
    {
        _command = command;
    }

    public ExifToolProcess(string exePath) : this(ExifToolCommand.Direct(exePath, ExifToolSource.Configured))
    {
    }

    /// <summary>Runs one command. Each element of <paramref name="args"/> is one exiftool argument.</summary>
    public async Task<ExifToolResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var a in args)
        {
            if (a.Contains('\n') || a.Contains('\r'))
                throw new ArgumentException("exiftool arguments cannot contain line breaks; use a temporary args file.", nameof(args));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureStarted();
            int id = Interlocked.Increment(ref _sequence);
            string marker = "{ready" + id + "}";

            var sb = new StringBuilder();
            foreach (var a in args)
                sb.Append(a).Append('\n');
            // -echo4 writes the marker to stderr after the command finishes, so both streams can be synchronised.
            sb.Append("-echo4\n").Append(marker).Append('\n');
            sb.Append("-execute").Append(id).Append('\n');

            var input = _process!.StandardInput;
            await input.WriteAsync(sb.ToString().AsMemory(), ct).ConfigureAwait(false);
            await input.FlushAsync(ct).ConfigureAwait(false);

            var outTask = ReadUntilMarkerAsync(_stdout!, marker, ct);
            var errTask = ReadUntilMarkerAsync(_stderr!, marker, ct);
            byte[] output = await outTask.ConfigureAwait(false);
            byte[] errors = await errTask.ConfigureAwait(false);
            return new ExifToolResult(output, Encoding.UTF8.GetString(errors).Trim());
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException)
        {
            // The streams are now out of sync (or the process died); start fresh next time.
            Kill();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureStarted()
    {
        if (_process is { HasExited: false })
            return;
        Kill();

        var psi = new ProcessStartInfo
        {
            FileName = _command.FileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Utf8NoBom,
        };
        foreach (var a in _command.PrefixArgs)
            psi.ArgumentList.Add(a);
        foreach (var a in new[] { "-stay_open", "True", "-@", "-", "-common_args", "-charset", "filename=utf8", "-charset", "utf8" })
            psi.ArgumentList.Add(a);

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start exiftool: " + _command.FileName);
        _stdout = _process.StandardOutput.BaseStream;
        _stderr = _process.StandardError.BaseStream;
    }

    private static async Task<byte[]> ReadUntilMarkerAsync(Stream stream, string marker, CancellationToken ct)
    {
        byte[] m = Encoding.ASCII.GetBytes(marker);
        using var ms = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            int n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0)
                throw new IOException("exiftool exited unexpectedly.");
            await ms.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);

            int end = FindMarkerAtEnd(ms.GetBuffer().AsSpan(0, (int)ms.Length), m);
            if (end >= 0)
                return ms.GetBuffer().AsSpan(0, end).ToArray();
        }
    }

    /// <summary>Returns the index where the marker starts if the data ends with "marker[\r]\n", else -1.</summary>
    internal static int FindMarkerAtEnd(ReadOnlySpan<byte> data, ReadOnlySpan<byte> marker)
    {
        int len = data.Length;
        if (len == 0 || data[len - 1] != (byte)'\n')
            return -1;
        len--;
        if (len > 0 && data[len - 1] == (byte)'\r')
            len--;
        if (len < marker.Length || !data.Slice(len - marker.Length, marker.Length).SequenceEqual(marker))
            return -1;
        return len - marker.Length;
    }

    private void Kill()
    {
        if (_process == null)
            return;
        try
        {
            if (!_process.HasExited)
                _process.Kill(true);
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        _process.Dispose();
        _process = null;
        _stdout = null;
        _stderr = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_process is { HasExited: false })
        {
            try
            {
                _process.StandardInput.Write("-stay_open\nFalse\n");
                _process.StandardInput.Flush();
                if (!_process.WaitForExit(2000))
                    _process.Kill(true);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                // ignore: shutting down
            }
        }
        _process?.Dispose();
        _process = null;
        _gate.Dispose();
    }
}
