using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed class ProtonLogin(ProfileStore store)
{
    private string? pending;
    private DateTime launchTime;
    private DateTime catalogStamp;
    public ProtonAccountSnapshot? Catalog { get; private set; }

    public void Open(bool create)
    {
        var current = pending is not null && Directory.Exists(pending) &&
            (DateTime.UtcNow - launchTime < TimeSpan.FromSeconds(20) || IsOpen(pending))
            ? pending : Requests().FirstOrDefault(IsOpen);
        if (current is not null)
        {
            Signal(current, "show");
            if (create) Signal(current, "create");
            return;
        }

        var executable = Path.Combine(AppContext.BaseDirectory, "AppTunnel.Proton.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException(T("Extract the complete ZIP to open the Proton sign-in window.",
                "Extraia o ZIP completo para abrir a janela de login da Proton."));

        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(store.ProtonRoot, "request-" + id);
        Directory.CreateDirectory(directory);
        try
        {
            if (create) Signal(directory, "create");
            OpenThroughExplorer(executable, $"{id} {(Portuguese ? "pt" : "en")}");
            pending = directory;
            launchTime = DateTime.UtcNow;
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public string? Poll(Action<ProtonConfiguration> import)
    {
        ReadCatalog();
        string? notice = null;
        foreach (var directory in Requests())
        {
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "profile-*.dpapi").Take(14))
            {
                if (IsLink(file)) continue;
                try { import(ProtonSync.ReadExport(file)); }
                catch (FormatException error) { notice = error.Message; }
                File.Delete(file);
                count++;
            }

            if (count >= 14 || !File.Exists(Path.Combine(directory, "closed"))) continue;
            var failure = File.Exists(Path.Combine(directory, "failed"));
            Directory.Delete(directory, recursive: true);
            if (pending == directory) pending = null;
            if (failure)
                notice = T("The Proton window could not open. Check the WebView2 Runtime and try again.",
                    "A janela Proton não abriu. Verifique o WebView2 Runtime e tente novamente.");
        }

        if (pending is not null && Directory.Exists(pending) && DateTime.UtcNow - launchTime > TimeSpan.FromSeconds(25) &&
            !File.Exists(Path.Combine(pending, "running")))
        {
            Directory.Delete(pending, recursive: true);
            pending = null;
            notice = T("The Proton window did not start. Try again or import a profile from the Proton website.",
                "A janela Proton não abriu. Tente novamente ou importe um perfil baixado do site da Proton.");
        }
        return notice;
    }

    private void ReadCatalog()
    {
        var path = Path.Combine(store.ProtonRoot, "account.json");
        if (!File.Exists(path) || IsLink(path)) return;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (stamp == catalogStamp || new FileInfo(path).Length > 512 * 1024) return;
        Catalog = JsonSerializer.Deserialize(File.ReadAllBytes(path), ProtonJson.Default.ProtonAccountSnapshot);
        catalogStamp = stamp;
    }

    private IEnumerable<string> Requests()
    {
        if (!Directory.Exists(store.ProtonRoot) || IsLink(store.ProtonRoot)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(store.ProtonRoot, "request-*"))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith("request-", StringComparison.Ordinal) &&
                Guid.TryParseExact(name[8..], "N", out _) && !IsLink(directory))
                yield return directory;
        }
    }

    private static void Signal(string directory, string command) =>
        ProtonSync.WriteAtomic(Path.Combine(directory, command), []);

    private static bool IsOpen(string directory)
    {
        var marker = Path.Combine(directory, "running");
        if (!File.Exists(marker)) return false;
        try
        {
            using var file = File.Open(marker, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void OpenThroughExplorer(string executable, string arguments)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            object location = 0;
            object? root = null;
            int handle;
            dynamic desktop = windows.FindWindowSW(ref location, ref root, 8, out handle, 1);
            if (desktop is null || handle == 0)
                throw new InvalidOperationException(T("The Windows desktop could not be reached.", "A área de trabalho do Windows está indisponível."));
            desktop.Document.Application.ShellExecute(executable, arguments, Path.GetDirectoryName(executable), "open", 1);
        }
        catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or Win32Exception)
        {
            throw new InvalidOperationException(T("AppTunnel could not open the Proton window through Windows Explorer.",
                "O AppTunnel não conseguiu abrir a janela Proton pelo Windows Explorer."), error);
        }
    }
}
