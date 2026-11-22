using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private readonly Label emptyApps = MakeLabel("", muted: true);
    private Guid? drawnLive;
    private DateTime? lastTest;

    private IEnumerable<ApplicationTarget> OrderedApps() =>
        settings.Applications.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase);

    // Rows are updated in place when the list keeps the same apps, so a toggle keeps keyboard focus on its row.
    private void DrawApps()
    {
        var ordered = OrderedApps().ToArray();
        var rows = appList.Controls.OfType<AppRow>().ToArray();
        var same = rows.Length == ordered.Length && rows.Zip(ordered).All(pair => Equals(pair.First.Tag, pair.Second.Match));
        appList.SuspendLayout();
        try
        {
            if (!same)
            {
                foreach (var row in rows) row.Dispose();
                appList.Controls.Clear();
                foreach (var target in ordered)
                {
                    var match = target.Match;
                    var row = new AppRow(match) { Tag = match };
                    row.ToggleRequested += () => ToggleApp(match);
                    row.RemoveRequested += () => RemoveApp(match);
                    appList.Controls.Add(row);
                }
                if (ordered.Length == 0)
                {
                    emptyApps.Text = T("No apps yet. Add one with the button above or drag an .exe here.",
                        "Nenhum app ainda. Adicione pelo botão acima ou arraste um .exe para cá.",
                        "Приложений пока нет. Добавьте кнопкой выше или перетащите сюда файл .exe.");
                    emptyApps.Dock = DockStyle.None;
                    emptyApps.Height = 56;
                    emptyApps.Padding = new Padding(4, 0, 0, 0);
                    appList.Controls.Add(emptyApps);
                }
            }
            var current = appList.Controls.OfType<AppRow>().ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                var target = ordered[i];
                var row = current[i];
                var exists = target.Folder ? Directory.Exists(target.Match) : File.Exists(target.Match);
                row.Title = target.Label;
                row.On = target.Enabled;
                row.Missing = !exists;
                row.Note = !exists ? T("Not found on this PC · ", "Não encontrado neste PC · ", "Не найдено на этом ПК · ") + target.Match
                    : target.Folder ? T("Folder · ", "Pasta · ", "Папка · ") + target.Match : target.Match;
                row.Refresh(target.Match);
            }
        }
        finally { appList.ResumeLayout(performLayout: true); }
        DrawChips(ordered);
    }

    private void DrawChips(ApplicationTarget[] ordered)
    {
        var existing = chips.Controls.OfType<ChipButton>().Where(chip => !chip.Dashed).ToArray();
        var same = existing.Length == ordered.Length && existing.Zip(ordered).All(pair => Equals(pair.First.Tag, pair.Second.Match));
        chips.SuspendLayout();
        try
        {
            if (!same)
            {
                foreach (Control chip in chips.Controls.Cast<Control>().ToArray()) chip.Dispose();
                chips.Controls.Clear();
                foreach (var target in ordered)
                {
                    var match = target.Match;
                    var chip = new ChipButton { Text = target.Label, Tag = match, AccessibleName = target.Label };
                    chip.Click += (_, _) => ToggleApp(match);
                    chips.Controls.Add(chip);
                }
                var add = new ChipButton { Dashed = true, Text = T("Add app", "Adicionar app", "Добавить приложение") };
                add.Click += (_, _) => OpenPage(AppsPage);
                chips.Controls.Add(add);
            }
            var current = chips.Controls.OfType<ChipButton>().Where(chip => !chip.Dashed).ToArray();
            for (var i = 0; i < ordered.Length; i++) current[i].On = ordered[i].Enabled;
        }
        finally { chips.ResumeLayout(performLayout: true); }
    }

    private void ToggleApp(string match)
    {
        if (busy) return;
        var index = settings.Applications.FindIndex(x => x.Match.Equals(match, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        var previous = settings.Applications[index];
        settings.Applications[index] = previous with { Enabled = !previous.Enabled };
        DrawApps();
        Changed();
        var applying = activeSession is not null && settings.AutoApply;
        var message = previous.Enabled
            ? T($"{previous.Label} now goes direct.", $"{previous.Label} agora segue direto.", $"{previous.Label} теперь работает напрямую.")
            : T($"{previous.Label} now uses the VPN.", $"{previous.Label} agora passa pela VPN.", $"{previous.Label} теперь работает через VPN.");
        if (activeSession is not null && !settings.Applications.Any(x => x.Enabled))
            message = T("No app is on, so the VPN keeps its current apps until you turn one on or disconnect.",
                "Nenhum app ligado: a VPN mantém os apps atuais até você ligar um ou desconectar.",
                "Нет включённых приложений: VPN сохраняет текущие, пока вы не включите другое или не отключитесь.");
        else if (applying) message += T(" Applying automatically…", " Aplicando automaticamente…", " Применяется автоматически…");
        footer.ShowAction(message, T("Undo", "Desfazer", "Отменить"), () =>
        {
            var at = settings.Applications.FindIndex(x => x.Match.Equals(match, StringComparison.OrdinalIgnoreCase));
            if (at < 0 || busy) return;
            settings.Applications[at] = settings.Applications[at] with { Enabled = previous.Enabled };
            DrawApps();
            Changed();
        });
    }

    private void RemoveApp(string match)
    {
        if (busy) return;
        var index = settings.Applications.FindIndex(x => x.Match.Equals(match, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        var removed = settings.Applications[index];
        settings.Applications.RemoveAt(index);
        DrawApps();
        Changed();
        footer.ShowAction(T($"Removed {removed.Label}.", $"{removed.Label} removido.", $"{removed.Label} удалено."),
            T("Undo", "Desfazer", "Отменить"), () =>
            {
                if (busy || settings.Applications.Any(x => x.Match.Equals(match, StringComparison.OrdinalIgnoreCase))) return;
                settings.Applications.Insert(Math.Min(index, settings.Applications.Count), removed);
                DrawApps();
                Changed();
            });
    }

    // Dropped executables, shortcuts to executables and folders join the list switched on.
    private void AddDropped(string[] paths)
    {
        var added = 0;
        Exception? first = null;
        foreach (var dropped in paths)
        {
            try
            {
                var path = dropped.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(dropped) : dropped;
                if (Directory.Exists(path))
                {
                    var folder = ApplicationTarget.Validate(Path.TrimEndingDirectorySeparator(path) + "\\");
                    AddTarget(new ApplicationTarget(Path.GetFileName(folder.TrimEnd('\\')), folder, Folder: true));
                }
                else
                {
                    var exe = ApplicationTarget.Validate(path);
                    var label = (CommonApps.FindByPath(exe) ?? CommonApps.Find(Path.GetFileNameWithoutExtension(exe)))?.Name ??
                        Path.GetFileNameWithoutExtension(exe);
                    AddTarget(new ApplicationTarget(label, exe));
                }
                added++;
            }
            catch (Exception error) when (Expected(error) || error is COMException) { first ??= error; }
        }
        if (added > 0)
        {
            DrawApps();
            Changed();
            footer.Text = added == 1 ? T("App added and switched on.", "App adicionado e ligado.", "Приложение добавлено и включено.")
                : T($"{added} apps added and switched on.", $"{added} apps adicionados e ligados.", $"Добавлено и включено приложений: {added}.");
        }
        if (first is not null) Failure(first is COMException
            ? new InvalidOperationException(T("This shortcut could not be read.", "Não foi possível ler este atalho.", "Не удалось прочитать ярлык."))
            : first);
    }

    private static string ResolveShortcut(string path)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, 0, 0);
            if (target.Length == 0)
                throw new FormatException(T("This shortcut does not point to an app on this PC.", "Este atalho não aponta para um app neste PC.",
                    "Этот ярлык не указывает на приложение на этом ПК."));
            return target.ToString();
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, nint data, uint flags);
    }

    private IEnumerable<ServerRow> ServerRows() =>
        serverTop.Controls.OfType<ServerRow>().Concat(serverList.Controls.OfType<ServerRow>());

    private void DrawRoutes()
    {
        var live = activeSession?.Profile.Id;
        drawnLive = live;
        var rows = serverList.Controls.OfType<ServerRow>().ToArray();
        var hasAuto = serverTop.Controls.OfType<ServerRow>().Any();
        var same = hasAuto == settings.Profiles.Count > 0 && rows.Length == settings.Profiles.Count &&
            rows.Zip(settings.Profiles).All(pair => Equals(pair.First.Tag, pair.Second.Id));
        var focused = ServerRows().FirstOrDefault(row => row.ContainsFocus);
        var refocus = focused is null ? null : focused.Automatic ? (object)"auto" : focused.Tag;
        serverTop.SuspendLayout();
        serverList.SuspendLayout();
        try
        {
            if (!same)
            {
                foreach (var row in ServerRows().ToArray()) row.Dispose();
                if (settings.Profiles.Count > 0)
                {
                    var auto = new ServerRow(automatic: true) { Title = T("Automatic", "Automático", "Автоматически") };
                    auto.Click += (_, _) => PickAuto();
                    serverTop.Controls.Add(auto);
                    serverTop.Controls.SetChildIndex(auto, 0);
                }
                foreach (var profile in settings.Profiles)
                {
                    var id = profile.Id;
                    var row = new ServerRow(automatic: false) { Tag = id };
                    row.Click += (_, _) => PickServer(id);
                    row.MenuRequested += point => ShowServerMenu(row, id, point);
                    serverList.Controls.Add(row);
                }
            }
            pinCaption.Visible = settings.Profiles.Count > 0;
            emptyRoutes.Visible = settings.Profiles.Count == 0;
            foreach (var row in ServerRows())
            {
                if (row.Automatic)
                {
                    var fastest = Fastest();
                    row.Picked = settings.AutoServer;
                    row.Detail = fastest is { } best
                        ? T($"Uses the fastest server right now ({best.Profile.Name}, {best.Latency} ms) and moves to another one if it fails or slows down.",
                            $"Usa o mais rápido agora ({best.Profile.Name}, {best.Latency} ms) e troca para outro se ele falhar ou ficar lento.",
                            $"Использует самый быстрый сервер ({best.Profile.Name}, {best.Latency} мс) и переключается, если он отказал или замедлился.")
                        : T("Uses the fastest server and moves to another one if it fails or slows down.",
                            "Usa o servidor mais rápido e troca para outro se ele falhar ou ficar lento.",
                            "Использует самый быстрый сервер и переключается, если он отказал или замедлился.");
                    row.Refresh();
                    continue;
                }
                if (row.Tag is not Guid id || settings.Profiles.Find(x => x.Id == id) is not { } profile) continue;
                row.Code = CountryCode(profile);
                row.Flag = CountryFlags.Get(row.Code);
                row.Title = profile.Name;
                row.Detail = $"{profile.Endpoint} · {VpnProtocols.Name(profile.Protocol)}" + (profile.HasIpv6 ? " · IPv6" : "");
                row.Status = id == live ? T("● In use", "● Em uso", "● Активен") : "";
                row.Picked = !settings.AutoServer && id == settings.SelectedProfile;
                row.Measured = latencies.TryGetValue(id, out var latency);
                row.Latency = latency;
                row.Refresh();
            }
        }
        finally
        {
            serverList.ResumeLayout(performLayout: true);
            serverTop.ResumeLayout(performLayout: true);
        }
        if (!same && refocus is not null)
            ServerRows()
                .FirstOrDefault(row => refocus is "auto" ? row.Automatic : Equals(row.Tag, refocus))?.Focus();
    }

    private (StoredProfile Profile, long Latency)? Fastest()
    {
        var best = settings.Profiles
            .Select(profile => (profile, latency: latencies.TryGetValue(profile.Id, out var ms) ? ms : null))
            .Where(item => item.latency.HasValue).OrderBy(item => item.latency).FirstOrDefault();
        return best.profile is null ? null : (best.profile, best.latency!.Value);
    }

    private void PickServer(Guid id)
    {
        if (HasPendingOperation || (!settings.AutoServer && settings.SelectedProfile == id)) return;
        settings.AutoServer = false;
        settings.SelectedProfile = id;
        DrawRoutes();
        Changed();
    }

    private void PickAuto()
    {
        if (HasPendingOperation || settings.AutoServer) return;
        settings.AutoServer = true;
        // While disconnected, Automatic points at the fastest measured server. A running session keeps its server,
        // so a pinned choice that was never applied is dropped instead of being applied later by accident.
        if (activeSession is { } session && settings.Profiles.Any(x => x.Id == session.Profile.Id)) settings.SelectedProfile = session.Profile.Id;
        else if (activeSession is null && Fastest() is { } best) settings.SelectedProfile = best.Profile.Id;
        DrawRoutes();
        Changed();
    }

    private void ShowServerMenu(Control row, Guid id, Point point)
    {
        if (busy || pingBusy) return;
        var menu = MakeMenu();
        menu.Add(T("Rename…", "Renomear…", "Переименовать…"), Glyph.Pencil, (_, _) => RenameRoute(id));
        menu.AddSeparator();
        menu.Add(T("Remove", "Remover", "Удалить"), Glyph.Trash, (_, _) => RemoveRoute(id)).Destructive = true;
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(row, point);
    }

    // The country saved with the server picks its flag; without one, Proton names like "NL-FREE#12" or
    // "US-NY#3" still give a short badge from their leading letters.
    private static string CountryCode(StoredProfile profile)
    {
        if (profile.Country is { Length: 2 } country) return country.ToUpperInvariant();
        var match = Regex.Match(profile.Name, @"^\s*([A-Za-z]{2})(?:[-#_\s]|$)");
        if (match.Success) return match.Groups[1].Value.ToUpperInvariant();
        var letters = new string(profile.Name.Where(char.IsLetterOrDigit).Take(2).ToArray());
        return letters.Length == 0 ? "VPN" : letters.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
    }

    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + T(" and ", " e ", " и ") + names[^1]
    };

    private async Task TogglePower()
    {
        if (HasPendingOperation) return;
        await Power();
    }

    private async Task Power()
    {
        if (activeSession is not null)
        {
            await ChangeConnection(starting: false);
            return;
        }
        if (settings.Profiles.Count == 0)
        {
            OpenPage(ServersPage);
            footer.Text = T("Add a server first.", "Adicione um servidor primeiro.", "Сначала добавьте сервер.");
            return;
        }
        if (!settings.Applications.Any(x => x.Enabled))
        {
            OpenPage(AppsPage);
            footer.Text = T("Turn on at least one app first.", "Ligue pelo menos um app primeiro.", "Сначала включите хотя бы одно приложение.");
            return;
        }
        if (SelectedRoute() is null) settings.SelectedProfile = settings.Profiles[0].Id;
        if (settings.AutoServer && settings.Profiles.Count > 1) await MeasureLatency(chooseFastest: true);
        await ChangeConnection(starting: true);
    }

    private async Task TestConnection()
    {
        if (HasPendingOperation) return;
        var session = activeSession;
        var profile = session?.Profile ?? SelectedRoute();
        if (profile is null)
        {
            OpenPage(ServersPage);
            return;
        }
        pingBusy = true;
        UpdateState();
        try
        {
            if (session is not null) ReadActiveDiagnostics(session);
            var latency = await PingRouteAsync(profile, 2, 1, latencyLifetime.Token);
            if (IsDisposed || Disposing) return;
            latencies[profile.Id] = latency;
            lastTest = DateTime.Now;
            DrawRoutes();
            footer.Text = latency is { } ms
                ? T($"{profile.Name} answered in {ms} ms.", $"{profile.Name} respondeu em {ms} ms.", $"{profile.Name} ответил за {ms} мс.")
                : T($"{profile.Name} did not answer the ping. The VPN can still work; check the apps.",
                    $"{profile.Name} não respondeu ao ping. A VPN ainda pode funcionar; confira os apps.",
                    $"{profile.Name} не ответил на пинг. VPN всё равно может работать; проверьте приложения.");
        }
        catch (OperationCanceledException) when (latencyLifetime.IsCancellationRequested) { }
        finally
        {
            pingBusy = false;
            if (!IsDisposed && !Disposing) UpdateState();
        }
    }

    private void UpdateHome(TunnelSession? session, bool pending, bool canApply, bool applyingSoon)
    {
        var running = session is not null;
        var names = OrderedApps().Where(x => x.Enabled).Select(x => x.Label).ToArray();
        var list = JoinNames(names);
        hero.Power.State = busy ? PowerState.Busy : running ? PowerState.On : PowerState.Off;
        hero.Power.Enabled = !HasPendingOperation;
        hero.Power.AccessibleName = running ? T("Disconnect", "Desconectar", "Отключить") : T("Connect", "Conectar", "Подключить");

        if (busy && operation.Length > 0)
            hero.Show(T("Please wait", "Aguarde", "Подождите"), Amber, AmberWash, operation,
                T("This usually takes a few seconds.", "Isso costuma levar alguns segundos.", "Обычно это занимает несколько секунд."));
        else if (running)
        {
            string detail = pending
                ? applyingSoon ? T("Applying your changes in a moment…", "Aplicando suas mudanças em instantes…", "Изменения скоро применятся…")
                : canApply ? T("Apply your changes to update the connection.", "Aplique as mudanças para atualizar a conexão.", "Примените изменения, чтобы обновить подключение.")
                : T("Turn on an app to apply your changes.", "Ligue um app para aplicar as mudanças.", "Включите приложение, чтобы применить изменения.")
                : names.Length == 0 ? T("The VPN is on.", "A VPN está ligada.", "VPN включён.")
                : names.Length == 1
                    ? T($"{list} goes through the VPN. The rest of the PC goes direct.", $"{list} está passando pela VPN. O resto do PC segue direto.",
                        $"{list} работает через VPN. Остальной трафик ПК идёт напрямую.")
                    : T($"{list} go through the VPN. The rest of the PC goes direct.", $"{list} estão passando pela VPN. O resto do PC segue direto.",
                        $"{list} работают через VPN. Остальной трафик ПК идёт напрямую.");
            if (names.Length == 0)
                detail = T("No app is on. The VPN keeps the apps it had until you turn one on or disconnect.",
                    "Nenhum app ligado. A VPN mantém os apps que tinha até você ligar um ou desconectar.",
                    "Нет включённых приложений. VPN сохраняет прежние, пока вы не включите другое или не отключитесь.");
            hero.Show(T("Connected", "Conectado", "Подключено"), Green, GreenWash, T("Protected", "Protegido", "Защищено"), detail);
        }
        else
        {
            var detail = settings.Profiles.Count == 0
                ? T("Add a server first: sign in to Proton on the Servers page.", "Adicione um servidor primeiro: entre na Proton na página Servidores.",
                    "Сначала добавьте сервер: войдите в Proton на странице «Серверы».")
                : names.Length == 0 ? T("Choose which apps use the VPN on the Apps page.", "Escolha na página Apps quais apps usam a VPN.",
                    "Выберите на странице «Приложения», что пойдёт через VPN.")
                : names.Length == 1
                    ? T($"Press the button. {list} goes through the VPN; the rest of the PC goes direct.",
                        $"Clique no botão. {list} vai pela VPN; o resto do PC segue direto.",
                        $"Нажмите кнопку. {list} пойдёт через VPN, остальное — напрямую.")
                    : T($"Press the button. {list} go through the VPN; the rest of the PC goes direct.",
                        $"Clique no botão. {list} vão pela VPN; o resto do PC segue direto.",
                        $"Нажмите кнопку. {list} пойдут через VPN, остальное — напрямую.");
            hero.Show(T("Disconnected", "Desconectado", "Отключено"), Muted, Raised,
                T("Ready to connect", "Pronto para conectar", "Готово к подключению"), detail);
        }

        var pick = SelectedRoute();
        var live = session is null ? null : settings.Profiles.Find(x => x.Id == session.Profile.Id) ?? session.Profile;
        var shown = live ?? pick;
        string LatencyOf(StoredProfile profile) => latencies.TryGetValue(profile.Id, out var ms) && ms is { } value
            ? T($" · {value} ms", $" · {value} ms", $" · {value} мс") : "";
        var title = settings.Profiles.Count == 0 ? T("No server yet", "Nenhum servidor ainda", "Сервера пока нет")
            : settings.AutoServer ? T("Automatic (fastest)", "Automático (mais rápido)", "Автоматически (самый быстрый)")
            : pick?.Name ?? T("Choose a server", "Escolha um servidor", "Выберите сервер");
        var serverDetail = settings.Profiles.Count == 0 ? T("Sign in to Proton to add one.", "Entre na Proton para adicionar.", "Войдите в Proton, чтобы добавить.")
            : live is not null ? T($"Now: {live.Name}", $"Agora: {live.Name}", $"Сейчас: {live.Name}") + LatencyOf(live) +
                (settings.Profiles.Count > 1 ? T(" · backup ready", " · reserva pronta", " · резерв готов") : "")
            : shown is not null ? (settings.AutoServer ? T($"Next: {shown.Name}", $"Próximo: {shown.Name}", $"Следующий: {shown.Name}") : shown.Endpoint) + LatencyOf(shown)
            : "";
        serverCard.Action.Text = settings.Profiles.Count == 0 ? T("Add", "Adicionar", "Добавить") : T("Change", "Trocar", "Сменить");
        if (shown is null) serverCard.Show(title, serverDetail, "", Glyph.Globe, Muted, Raised);
        else serverCard.Show(title, serverDetail, CountryCode(shown), Glyph.None, DesktopTheme.Text, Raised);

        healthCard.Action.Enabled = !HasPendingOperation && shown is not null;
        if (!running)
        {
            var engineReady = settings.EnginePath.Length > 0 && File.Exists(settings.EnginePath) || WireSock is not null;
            if (engineReady)
                healthCard.Show(T("Not connected", "Não conectado", "Не подключено"),
                    T("WireSock is installed and ready.", "O WireSock está instalado e pronto.", "WireSock установлен и готов."), "", Glyph.Power, Muted, Raised);
            else
                healthCard.Show(T("WireSock missing", "WireSock ausente", "WireSock не найден"),
                    T("Install the WireSock CLI from Diagnostics.", "Instale o WireSock CLI em Diagnóstico.", "Установите WireSock CLI в разделе «Диагностика»."),
                    "", Glyph.Alert, Danger, DangerWash);
            return;
        }
        var tested = lastTest is { } at
            ? T($"Last test {Ago(at)}", $"Último teste {Ago(at)}", $"Последняя проверка {Ago(at)}") : "";
        switch (lastHealth)
        {
            case TunnelHealth.HandshakeReceived:
                healthCard.Show(T("All working", "Tudo funcionando", "Всё работает"),
                    tested.Length > 0 ? tested + T(" · handshake confirmed", " · handshake confirmado", " · рукопожатие подтверждено")
                        : T($"Handshake confirmed · {VpnProtocols.Name(session!.Protocol)}", $"Handshake confirmado · {VpnProtocols.Name(session!.Protocol)}",
                            $"Рукопожатие подтверждено · {VpnProtocols.Name(session!.Protocol)}"),
                    "", Glyph.Check, Green, GreenWash);
                break;
            case TunnelHealth.HandshakeTimedOut or TunnelHealth.EndpointResolutionFailed or TunnelHealth.RoutingFailed:
                healthCard.Show(T("Connection problem", "Problema na conexão", "Проблема с подключением"), TunnelDiagnostics.Describe(lastHealth),
                    "", Glyph.Alert, Danger, DangerWash);
                break;
            default:
                healthCard.Show(T("Waiting for the server", "Aguardando o servidor", "Ожидание сервера"),
                    T("Use a selected app so the tunnel can confirm the handshake.", "Use um app selecionado para o túnel confirmar o handshake.",
                        "Используйте выбранное приложение, чтобы туннель подтвердил рукопожатие."),
                    "", Glyph.Pulse, Amber, AmberWash);
                break;
        }
    }

    private string? wireSock;
    private DateTime wireSockChecked;

    // Locating WireSock reads the file system, so the result is reused for a few seconds.
    private string? WireSock
    {
        get
        {
            if (DateTime.UtcNow - wireSockChecked < TimeSpan.FromSeconds(10)) return wireSock;
            wireSockChecked = DateTime.UtcNow;
            try { wireSock = WireSockEngine.Locate(); }
            catch (Exception error) when (Expected(error)) { wireSock = null; }
            return wireSock;
        }
    }

    private static string Ago(DateTime at)
    {
        var minutes = (int)(DateTime.Now - at).TotalMinutes;
        return minutes < 1 ? T("just now", "agora há pouco", "только что")
            : T($"{minutes} min ago", $"há {minutes} min", $"{minutes} мин назад");
    }
}
