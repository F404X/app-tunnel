using System.Text.Json;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed class ProtonLogin(ProfileStore store)
{
    private string? pending;
    private DateTime launchTime;
    private DateTime catalogStamp;
    public bool HasOpenWindow
    {
        get
        {
            try { return pending is not null || Requests().Any(IsOpen); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; }
        }
    }

    public ProtonAccountSnapshot? Catalog { get; private set; }

    private bool opening;

    // OpenAsync may wait for an old helper to close. ProtonLogin is shared by every MainForm instance,
    // so this guard keeps repeat clicks (or a recreated window) from launching a second helper.
    public async Task OpenAsync(bool create)
    {
        if (opening) return;
        opening = true;
        try { await OpenCoreAsync(create); }
        finally { opening = false; }
    }

    private async Task OpenCoreAsync(bool create)
    {
        var theme = DesktopTheme.IsDark ? "dark" : "light";
        var appearance = $"{Language} {theme}";
        var current = pending is not null && Directory.Exists(pending) &&
            (DateTime.UtcNow - launchTime < TimeSpan.FromSeconds(20) || IsOpen(pending))
            ? pending : Requests().FirstOrDefault(IsOpen);
        if (current is not null && ReadAppearance(current) is { } shown && shown != appearance)
        {
            // The helper reads language and theme only at startup, so restart it to match the main window.
            Signal(current, "close");
            for (var attempt = 0; attempt < 25 && IsOpen(current); attempt++) await Task.Delay(150);
            if (!IsOpen(current))
            {
                if (pending == current) pending = null;
                current = null;
            }
        }
        if (current is not null)
        {
            Signal(current, "show");
            if (create) Signal(current, "create");
            return;
        }

        var executable = Path.Combine(AppContext.BaseDirectory, "AppTunnel.Proton.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException(T("Extract the complete ZIP to open the Proton sign-in window.",
                "Extraia o ZIP completo para abrir a janela de login da Proton.", "Распакуйте весь ZIP-архив, чтобы открыть окно входа в Proton."));

        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(store.ProtonRoot, "request-" + id);
        Directory.CreateDirectory(directory);
        try
        {
            if (create) Signal(directory, "create");
            ProtonSync.WriteAtomic(Path.Combine(directory, AppearanceFile), System.Text.Encoding.UTF8.GetBytes(appearance));
            ExplorerLauncher.Open(executable, $"{id} {appearance}");
            pending = directory;
            launchTime = DateTime.UtcNow;
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    private const string AppearanceFile = "appearance";

    private static string? ReadAppearance(string directory)
    {
        var path = Path.Combine(directory, AppearanceFile);
        try { return File.Exists(path) && !IsLink(path) ? File.ReadAllText(path).Trim() : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    public async Task CloseForSetupAsync()
    {
        var requests = Requests().Where(directory => directory == pending || IsOpen(directory)).ToArray();
        foreach (var directory in requests)
        {
            try { Signal(directory, "close"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        for (var attempt = 0; attempt < 25 && requests.Any(IsOpen); attempt++)
            await Task.Delay(150);
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
                    "A janela Proton não abriu. Verifique o WebView2 Runtime e tente novamente.", "Не удалось открыть окно Proton. Проверьте WebView2 Runtime и попробуйте снова.");
        }

        if (pending is not null && Directory.Exists(pending) && DateTime.UtcNow - launchTime > TimeSpan.FromSeconds(25) &&
            !File.Exists(Path.Combine(pending, "running")))
        {
            Directory.Delete(pending, recursive: true);
            pending = null;
            notice = T("The Proton window did not start. Try again or import a profile from the Proton website.",
                "A janela Proton não abriu. Tente novamente ou importe um perfil baixado do site da Proton.", "Окно Proton не открылось. Попробуйте снова или импортируйте профиль с сайта Proton.");
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
}
