using System.Diagnostics;
using System.Text;
using AppTunnel.Core;

namespace AppTunnel.Windows;

internal sealed class WireSockHealth
{
    private const int TailLimit = 64 * 1024;
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private readonly Dictionary<string, Cursor> logs = new(StringComparer.OrdinalIgnoreCase)
    {
        [Path.Combine(ProgramData, @"WireSock Foundation\WireSock Secure Connect\Logs\wiresock-vpn-client.log")] = new(),
        [Path.Combine(ProgramData, @"NT KERNEL\WireSock VPN Client\wiresock-vpn-client.log")] = new()
    };

    public WireSockHealth()
    {
        foreach (var (path, cursor) in logs)
        {
            using var file = OpenLog(path);
            if (file is null) continue;
            try { cursor.Checkpoint(file); }
            catch (IOException) { }
        }
    }

    private long handshakeStarted;
    private TunnelHealth Status { get; set; }
    public TunnelHealth Poll()
    {
        foreach (var (path, cursor) in logs)
        {
            using var file = OpenLog(path);
            if (file is null) continue;
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

    private sealed class Cursor
    {
        private long offset;
        private byte[] checkpoint = [];
        private string partial = "";

        public void Checkpoint(FileStream file)
        {
            offset = file.Length;
            checkpoint = ReadAnchor(file, offset);
        }

        public string[] Read(FileStream file)
        {
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
