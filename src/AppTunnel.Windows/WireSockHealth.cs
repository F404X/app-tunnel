using System.Diagnostics;
using System.Text;
using AppTunnel.Core;

namespace AppTunnel.Windows;

internal sealed class WireSockHealth
{
    private const int TailLimit = 64 * 1024;
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    // WireSock writes its service log in one of these folders. The file name can differ between WireSock releases and
    // installs, so every .log file directly inside them is followed instead of a single fixed name.
    private static readonly string[] LogFolders =
    [
        Path.Combine(ProgramData, @"WireSock Foundation\WireSock Secure Connect\Logs"),
        Path.Combine(ProgramData, @"WireSock Foundation\WireSock Secure Connect"),
        Path.Combine(ProgramData, @"NT KERNEL\WireSock VPN Client")
    ];
    private readonly Dictionary<string, Cursor> logs = new(StringComparer.OrdinalIgnoreCase);

    public WireSockHealth()
    {
        // Logs that already exist are read only from their current end, so an older session's lines are ignored.
        // A log that cannot be opened yet starts from its end the first time it opens.
        foreach (var path in FindLogs())
        {
            var cursor = new Cursor(fromEnd: true);
            logs[path] = cursor;
            using var file = OpenLog(path);
            if (file is null) continue;
            try { cursor.Checkpoint(file); }
            catch (IOException) { }
        }
    }

    private long handshakeStarted;
    private TunnelHealth Status { get; set; }

    /// <summary>True once at least one WireSock log could be opened, so a missing handshake is real evidence.</summary>
    public bool LogFound { get; private set; }

    public TunnelHealth Poll()
    {
        // A log created after this session started (a new WireSock install or a rotated file) is read from its start.
        foreach (var path in FindLogs())
        {
            if (!logs.ContainsKey(path)) logs[path] = new Cursor(fromEnd: false);
        }
        foreach (var (path, cursor) in logs)
        {
            using var file = OpenLog(path);
            if (file is null) continue;
            LogFound = true;
            try
            {
                foreach (var line in cursor.Read(file))
                {
                    var state = TunnelDiagnostics.Observe(Status, line);
                    if (state == TunnelHealth.HandshakeSent && Status != state)
                        handshakeStarted = Stopwatch.GetTimestamp();
                    Status = state;
                }
            }
            catch (IOException) { }
        }
        if (Status == TunnelHealth.HandshakeSent && Stopwatch.GetElapsedTime(handshakeStarted) >= TimeSpan.FromSeconds(12))
            Status = TunnelHealth.HandshakeTimedOut;
        return Status;
    }

    private static IEnumerable<string> FindLogs()
    {
        foreach (var folder in LogFolders)
        {
            string[] files;
            try { files = Directory.GetFiles(folder, "*.log", SearchOption.TopDirectoryOnly); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files) yield return file;
        }
    }

    private static FileStream? OpenLog(string path)
    {
        try
        {
            for (var entry = path; !string.Equals(entry, ProgramData, StringComparison.OrdinalIgnoreCase);
                entry = Path.GetDirectoryName(entry)!)
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return null;
            }
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class Cursor(bool fromEnd)
    {
        private bool pending = fromEnd;
        private long offset;
        private byte[] checkpoint = [];
        private string partial = "";

        public void Checkpoint(FileStream file)
        {
            pending = false;
            offset = file.Length;
            checkpoint = ReadAnchor(file, offset);
        }

        public string[] Read(FileStream file)
        {
            if (pending)
            {
                Checkpoint(file);
                return [];
            }
            if (file.Length < offset || !ReadAnchor(file, offset).AsSpan().SequenceEqual(checkpoint))
            {
                offset = 0;
                partial = "";
            }
            var length = file.Length;
            file.Position = Math.Min(length, Math.Max(offset, length - TailLimit));
            var bytes = new byte[(int)(length - file.Position)];
            var count = file.ReadAtLeast(bytes, bytes.Length, false);
            offset = file.Position;
            checkpoint = ReadAnchor(file, offset);
            if (count == 0) return [];
            var lines = (partial + Encoding.UTF8.GetString(bytes, 0, count)).Split('\n');
            partial = lines[^1].Length < 4096 ? lines[^1] : "";
            return lines[..^1];
        }

        private static byte[] ReadAnchor(FileStream file, long end)
        {
            var length = (int)Math.Min(64, end);
            var bytes = new byte[length];
            file.Position = end - length;
            var count = file.ReadAtLeast(bytes, length, false);
            return bytes[..count];
        }
    }
}
