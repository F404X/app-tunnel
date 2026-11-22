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

    private string? activeCall;
    private static readonly string[] ExchangeFiles = ["command.json", "result.json", "profile.dpapi", "registered", "uncertain", "running", "closed", "failed"];

    // Runs one request through the signed-in Proton session without showing a window. A created route is handed
    // to save before its folder is removed; when saving fails, the folder stays and Poll imports it later.
    public async Task<ProtonApiResult> RunApiAsync(ProtonApiCommand command, Action<ProtonConfiguration>? save, CancellationToken token)
    {
        if (activeCall is { } previous && Directory.Exists(previous) && (IsOpen(previous) || !File.Exists(Path.Combine(previous, "closed"))) &&
            DateTime.UtcNow - Directory.GetCreationTimeUtc(previous) < TimeSpan.FromMinutes(3))
            throw new InvalidOperationException(T("Proton is still answering the previous request.", "A Proton ainda está respondendo ao pedido anterior.",
                "Proton ещё обрабатывает предыдущий запрос."));
        var executable = Path.Combine(AppContext.BaseDirectory, "AppTunnel.Proton.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException(T("Extract the complete ZIP to reach Proton.", "Extraia o ZIP completo para acessar a Proton.",
                "Распакуйте весь ZIP-архив, чтобы подключиться к Proton."));
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(store.ProtonRoot, "api-" + id);
        var keep = false;
        activeCall = directory;
        try
        {
            Directory.CreateDirectory(directory);
            WriteVerified(Path.Combine(directory, "command.json"),
                JsonSerializer.SerializeToUtf8Bytes(command, ProtonRouteJson.Default.ProtonApiCommand));
            var theme = DesktopTheme.IsDark ? "dark" : "light";
            ExplorerLauncher.Open(executable, $"{id} {Language} {theme} api");
            var result = Path.Combine(directory, "result.json");
            var started = DateTime.UtcNow;
            ProtonApiResult? answer = null;
            while (!File.Exists(result))
            {
                try { await Task.Delay(300, token); }
                catch (OperationCanceledException)
                {
                    // The helper keeps running to its own deadline; Poll cleans up its folder or imports its route.
                    keep = true;
                    throw;
                }
                if (File.Exists(Path.Combine(directory, "closed")) && !File.Exists(result))
                {
                    // A route Proton confirmed still counts when only the answer file is missing.
                    if (File.Exists(Path.Combine(directory, "registered")))
                    {
                        answer = new ProtonApiResult(Created: command.Server?.Name);
                        break;
                    }
                    keep = File.Exists(Path.Combine(directory, "uncertain"));
                    throw new InvalidOperationException(File.Exists(Path.Combine(directory, "failed"))
                        ? T("Proton could not be reached. Check the WebView2 Runtime and try again.",
                            "Não foi possível acessar a Proton. Verifique o WebView2 Runtime e tente novamente.",
                            "Не удалось подключиться к Proton. Проверьте WebView2 Runtime и попробуйте снова.")
                        : T("Proton did not answer. Try again.", "A Proton não respondeu. Tente novamente.", "Proton не ответил. Попробуйте снова."));
                }
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(110))
                {
                    keep = true;
                    throw new InvalidOperationException(T("Proton did not answer in time. Try again.", "A Proton não respondeu a tempo. Tente novamente.",
                        "Proton не ответил вовремя. Попробуйте снова."));
                }
            }
            // A saved key survives any failure while its answer is read; only a finished outcome releases it below.
            keep = File.Exists(Path.Combine(directory, "profile.dpapi"));
            answer ??= JsonSerializer.Deserialize(ReadVerified(result, 4 * 1024 * 1024), ProtonRouteJson.Default.ProtonApiResult)
                ?? throw new FormatException(T("Proton sent an empty answer.", "A Proton enviou uma resposta vazia.", "Proton прислал пустой ответ."));
            answer = answer with { Servers = answer.Servers?.Where(ProtonRoutes.IsUsable).ToArray() };
            // A key that is registered or possibly registered stays on disk until it has been imported.
            keep = File.Exists(Path.Combine(directory, "uncertain")) || File.Exists(Path.Combine(directory, "registered"));
            if (command.Action != ProtonApiCommand.CreateRoute || answer.Created is null) return answer with { Created = null };

            if (!File.Exists(Path.Combine(directory, "registered")) || !File.Exists(Path.Combine(directory, "profile.dpapi")))
                return answer with { Created = null, Error = answer.Error ?? T("Proton did not confirm the new route.",
                    "A Proton não confirmou a nova rota.", "Proton не подтвердил новый маршрут.") };
            keep = true;
            var config = ReadRoute(directory, command);
            save?.Invoke(config);
            keep = false;
            return answer;
        }
        finally
        {
            if (!keep) TryDelete(directory);
        }
    }

    // Reads the route the helper saved and checks it against the server that was requested.
    private ProtonConfiguration ReadRoute(string directory, ProtonApiCommand command)
    {
        var config = ProtonSync.ReadExport(ReadVerified(Path.Combine(directory, "profile.dpapi"), 512 * 1024));
        if (command.Server is not { } server || !ProtonRoutes.IsUsable(server) || !Matches(config, server))
            throw new FormatException(T("The new route does not match the chosen server.", "A nova rota não corresponde ao servidor escolhido.",
                "Новый маршрут не совпадает с выбранным сервером."));
        return config;
    }

    private static bool Matches(ProtonConfiguration config, ProtonServerChoice server)
    {
        try
        {
            var parsed = WireGuardProfile.Parse(config.Text);
            return parsed.Endpoint == $"{server.EntryIp}:51820" &&
                config.Text.Contains($"\nPublicKey = {server.PublicKey}\n", StringComparison.Ordinal);
        }
        catch (FormatException) { return false; }
    }

    // Recovers routes whose import failed or whose window closed mid-creation, and removes finished folders.
    // Recovered routes get the same check against the requested server as the foreground path.
    private string? RecoverApiCalls(Action<ProtonConfiguration> import)
    {
        string? notice = null;
        if (!Directory.Exists(store.ProtonRoot) || IsLink(store.ProtonRoot)) return null;
        // Kept (uncertain) folders are skipped cheaply and do not count toward the work done per poll, so they can
        // never crowd out newer folders.
        var handled = 0;
        foreach (var directory in Directory.EnumerateDirectories(store.ProtonRoot, "api-*").ToArray())
        {
            if (handled >= 32) break;
            var name = Path.GetFileName(directory);
            if (!Guid.TryParseExact(name[4..], "N", out _) || IsLink(directory)) continue;
            if (File.Exists(Path.Combine(directory, "uncertain")) && !File.Exists(Path.Combine(directory, "registered"))) continue;
            if (IsOpen(directory)) continue;
            handled++;
            var closed = File.Exists(Path.Combine(directory, "closed"));
            var age = DateTime.UtcNow - Directory.GetCreationTimeUtc(directory);
            if (directory == activeCall && !closed && age < TimeSpan.FromMinutes(3)) continue;
            if (File.Exists(Path.Combine(directory, "registered")) && File.Exists(Path.Combine(directory, "profile.dpapi")))
            {
                try
                {
                    var command = JsonSerializer.Deserialize(ReadVerified(Path.Combine(directory, "command.json"), 64 * 1024),
                        ProtonRouteJson.Default.ProtonApiCommand) ?? throw new FormatException(T("The saved Proton request is damaged.",
                        "O pedido salvo da Proton está danificado.", "Сохранённый запрос Proton повреждён."));
                    import(ReadRoute(directory, command));
                }
                catch (Exception error) when (error is FormatException or JsonException or IOException or UnauthorizedAccessException)
                {
                    notice = error.Message;
                    continue;
                }
            }
            // An unconfirmed key is never thrown away automatically: Proton may have registered it. It is a few
            // hundred bytes and stays until the user removes it.
            else if (!closed && age < TimeSpan.FromMinutes(3))
                continue;
            TryDelete(directory);
        }
        return notice;
    }

    // The exchange folder is writable without elevation, so every file the elevated window touches is opened first
    // and its real location checked: a folder or file swapped for a link elsewhere is refused.
    private string? realLocalData;

    // Where a file must really be: the user's Local AppData as Windows resolves it (a profile moved to another
    // drive is fine), followed by plain AppTunnel folders with no links in between.
    private string ExpectedPath(string path)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(store.ProtonRoot);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(T("The Proton folder is not a plain folder.", "A pasta da Proton não é uma pasta comum.", "Папка Proton не является обычной папкой."));
        realLocalData ??= FinalPath(local, directory: true);
        return Path.Combine(realLocalData, Path.GetRelativePath(local, full));
    }

    private static void Verify(FileStream file, string expected)
    {
        if (!FinalPath(file.SafeFileHandle).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException(T("The Proton folder is not a plain folder.", "A pasta da Proton não é uma pasta comum.", "Папка Proton не является обычной папкой."));
    }

    private static string FinalPath(string path, bool directory)
    {
        // FILE_READ_ATTRIBUTES with FILE_FLAG_BACKUP_SEMANTICS opens a folder without locking it.
        using var handle = CreateFileW(path, 0x80, 7, 0, 3, directory ? 0x02000000u : 0, 0);
        if (handle.IsInvalid) throw new IOException(T("The Proton folder is unavailable.", "A pasta da Proton está indisponível.", "Папка Proton недоступна."));
        return FinalPath(handle);
    }

    private static string FinalPath(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        var buffer = new char[1024];
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
        var actual = length is > 0 and < 1024 ? new string(buffer, 0, (int)length) : "";
        if (actual.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + actual[8..];
        return actual.StartsWith(@"\\?\", StringComparison.Ordinal) ? actual[4..] : actual;
    }

    private const uint Delete = 0x00010000, ReadAttributes = 0x80, OpenReparsePoint = 0x00200000;

    // FileDispositionInfo (4): marks the open file for deletion when its last handle closes.
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, int type, ref byte info, uint size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    private byte[] ReadVerified(string path, int limit)
    {
        var expected = ExpectedPath(path);
        using var file = new FileStream(expected, FileMode.Open, FileAccess.Read, FileShare.Read);
        Verify(file, expected);
        if (file.Length > limit)
            throw new FormatException(T("Proton sent an unexpected answer.", "A Proton enviou uma resposta inesperada.", "Proton прислал неожиданный ответ."));
        var bytes = new byte[file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }

    private void WriteVerified(string path, byte[] bytes)
    {
        var expected = ExpectedPath(path);
        using var file = new FileStream(expected, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Verify(file, expected);
        file.Write(bytes);
    }

    // Deletes only the known exchange files, each through a handle whose location was checked, then the folder
    // itself if it is empty; removing an empty folder or a link never touches anything outside it.
    private void TryDelete(string directory)
    {
        try
        {
            if (!Directory.Exists(directory) || IsOpen(directory)) return;
            foreach (var name in ExchangeFiles)
            {
                var path = Path.Combine(directory, name);
                if (!File.Exists(path)) continue;
                var expected = ExpectedPath(path);
                // Opened without following a link and without deleting; deletion is requested only after the
                // handle's real location is confirmed.
                using var handle = CreateFileW(expected, Delete | ReadAttributes, 7, 0, 3, OpenReparsePoint, 0);
                if (handle.IsInvalid) continue;
                if (!FinalPath(handle).Equals(expected, StringComparison.OrdinalIgnoreCase)) return;
                byte remove = 1;
                if (!SetFileInformationByHandle(handle, 4, ref remove, 1)) return;
            }
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, [System.Runtime.InteropServices.Out] char[] path,
        uint length, uint flags);

    public string? Poll(Action<ProtonConfiguration> import)
    {
        ReadCatalog();
        var notice = RecoverApiCalls(import);
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
