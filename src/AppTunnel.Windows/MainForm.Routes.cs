using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private bool choosingRoute;

    // Lists the Proton servers the account can use and creates a WireGuard route for the one picked, through the
    // signed-in Proton session in the background. Only a missing sign-in opens the Proton window.
    private async Task ChooseRoute(IWin32Window? parent = null)
    {
        if (choosingRoute || HasPendingOperation) return;
        choosingRoute = true;
        StoredProfile? saved;
        bool signIn;
        try
        {
            using var dialog = new RouteChooser(this);
            dialog.ShowDialog(parent ?? this);
            saved = dialog.Saved;
            signIn = dialog.SignInRequired;
        }
        finally { choosingRoute = false; }

        if (signIn)
        {
            footer.Text = T("Sign in to Proton once in the window that opened, then choose the route again.",
                "Entre na Proton uma vez na janela que abriu e depois escolha a rota de novo.",
                "Войдите в Proton один раз в открывшемся окне, затем снова выберите маршрут.");
            OpenProton();
            return;
        }
        if (saved is null) return;
        // A route chosen by hand is the one the user wants next, unless Automatic is on.
        if (!settings.AutoServer)
        {
            settings.SelectedProfile = saved.Id;
            DrawRoutes();
            Changed();
        }
        footer.Text = T($"Route {saved.Name} created and saved.", $"Rota {saved.Name} criada e salva.", $"Маршрут {saved.Name} создан и сохранён.");
        await RefreshLatencyQuietly();
    }

    private sealed class RouteChooser : Form
    {
        private readonly MainForm owner;
        private readonly CancellationTokenSource lifetime = new();
        private readonly ThemeCombo countries = new() { Width = 260, Margin = new Padding(0, 4, 12, 0) };
        private readonly StackPanel list = new() { Dock = DockStyle.Fill, BackColor = Background, Margin = new Padding(0) };
        private readonly Label status = MakeLabel("", muted: true);
        private readonly Dictionary<string, long?> pings = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> probing = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim pingSlots = new(8);
        private ProtonServerChoice[] servers = [];
        private bool creating;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool SignInRequired { get; private set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public StoredProfile? Saved { get; private set; }

        public RouteChooser(MainForm owner)
        {
            this.owner = owner;
            Text = T("Choose route", "Escolher rota", "Выбор маршрута");
            ClientSize = new Size(760, 640);
            MinimumSize = new Size(560, 460);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Background;
            ForeColor = DesktopTheme.Text;
            Font = Body();
            ShowInTaskbar = false;
            MinimizeBox = false;
            Padding = new Padding(28, 24, 28, 16);
            Icon = owner.Icon;
            PrepareDialog(this);

            var layout = Column(76, 52, 0, 34);
            layout.BackColor = Background;
            layout.Controls.Add(new PageHeader(Text, T("Pick a server. AppTunnel creates the WireGuard route in your Proton account.",
                "Escolha um servidor. O AppTunnel cria a rota WireGuard na sua conta Proton.",
                "Выберите сервер. AppTunnel создаст маршрут WireGuard в вашей учётной записи Proton.")) { Dock = DockStyle.Fill }, 0, 0);
            countries.AccessibleName = T("Country", "País", "Страна");
            countries.Enabled = false;
            countries.SelectedIndexChanged += (_, _) => Fill();
            var bar = MakeBar(countries);
            bar.BackColor = Background;
            layout.Controls.Add(bar, 0, 1);
            list.AccessibleName = T("Servers", "Servidores", "Серверы");
            layout.Controls.Add(list, 0, 2);
            status.Text = T("Loading servers from Proton…", "Carregando servidores da Proton…", "Загрузка серверов Proton…");
            layout.Controls.Add(status, 0, 3);
            Controls.Add(layout);
            Shown += async (_, _) => await LoadServers();
            FormClosing += (_, e) =>
            {
                // Closing mid-creation could leave a route in Proton that AppTunnel never saved; loading can stop any time.
                if (creating) e.Cancel = true;
                else lifetime.Cancel();
            };
        }

        private async Task LoadServers()
        {
            try
            {
                var result = await owner.proton.RunApiAsync(new ProtonApiCommand(ProtonApiCommand.ListServers), null, lifetime.Token);
                if (IsDisposed) return;
                if (result.SignInRequired)
                {
                    SignInRequired = true;
                    Close();
                    return;
                }
                if (result.Error is { } error)
                {
                    status.Text = error;
                    return;
                }
                servers = result.Servers ?? [];
                var codes = servers.Select(server => server.Country).Where(code => code.Length == 2).Distinct()
                    .Select(code => (Code: code, Name: ProtonServerNames.CountryName(code)))
                    .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                countries.Items.Add(T("All countries", "Todos os países", "Все страны"));
                foreach (var item in codes) countries.Items.Add(new CountryItem(item.Code, item.Name));
                countries.SelectedIndex = 0;
                countries.Enabled = servers.Length > 0;
                status.Text = servers.Length == 0
                    ? T("Proton listed no servers for this account.", "A Proton não listou servidores para esta conta.", "Proton не показал серверов для этой учётной записи.")
                    : result.MaxTier == 0
                        ? T($"{servers.Length} free servers · sorted by Proton's recommendation", $"{servers.Length} servidores gratuitos · ordenados pela recomendação da Proton",
                            $"Бесплатных серверов: {servers.Length} · по рекомендации Proton")
                        : T($"{servers.Length} servers · sorted by Proton's recommendation", $"{servers.Length} servidores · ordenados pela recomendação da Proton",
                            $"Серверов: {servers.Length} · по рекомендации Proton");
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (Expected(error))
            {
                if (!IsDisposed) status.Text = error.Message;
            }
        }

        private void Fill()
        {
            var country = countries.SelectedItem is CountryItem item ? item.Code : null;
            var shown = servers.Where(server => country is null || server.Country == country).Take(country is null ? 60 : 200).ToArray();
            list.SuspendLayout();
            foreach (Control row in list.Controls.Cast<Control>().ToArray()) row.Dispose();
            list.Controls.Clear();
            foreach (var server in shown)
            {
                var row = new ServerRow(automatic: false)
                {
                    Choice = true, Code = server.Country.Length == 2 ? server.Country : ProtonServerNames.CountryOf(server.Name),
                    Title = server.Name, Tag = server,
                    Detail = string.Join(" · ", new[] { server.City, T($"{server.Load}% load", $"{server.Load}% de carga", $"нагрузка {server.Load}%") }
                        .Where(part => !string.IsNullOrEmpty(part))),
                    Measured = pings.ContainsKey(server.EntryIp), Latency = pings.GetValueOrDefault(server.EntryIp)
                };
                row.Click += async (_, _) => await Create(server);
                list.Controls.Add(row);
            }
            list.ResumeLayout(performLayout: true);
            foreach (var server in shown) _ = Measure(server.EntryIp);
        }

        // One dialog-wide limit, and each address is probed once however often the filter changes.
        private async Task Measure(string address)
        {
            if (pings.ContainsKey(address) || !probing.Add(address)) return;
            long? latency = null;
            try
            {
                await pingSlots.WaitAsync(lifetime.Token);
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(1500), new byte[24], null, lifetime.Token);
                    if (reply.Status == IPStatus.Success) latency = reply.RoundtripTime;
                }
                finally { pingSlots.Release(); }
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception error) when (error is PingException or SocketException or InvalidOperationException) { }
            finally { probing.Remove(address); }
            if (IsDisposed) return;
            pings[address] = latency;
            foreach (var row in list.Controls.OfType<ServerRow>().Where(row => row.Tag is ProtonServerChoice item && item.EntryIp == address))
            {
                row.Measured = true;
                row.Latency = latency;
                row.Refresh();
            }
        }

        private async Task Create(ProtonServerChoice server)
        {
            if (creating) return;
            creating = true;
            list.Enabled = false;
            countries.Enabled = false;
            status.Text = T($"Creating {server.Name} in your Proton account…", $"Criando {server.Name} na sua conta Proton…",
                $"Создание {server.Name} в учётной записи Proton…");
            try
            {
                // The route is saved inside the request, before its key is removed from the exchange folder.
                var result = await owner.proton.RunApiAsync(new ProtonApiCommand(ProtonApiCommand.CreateRoute, server, $"AppTunnel {server.Name}"),
                    config => Saved = owner.AddProtonProfile(config) ?? throw new InvalidOperationException(
                        T("This route is already saved.", "Esta rota já está salva.", "Этот маршрут уже сохранён.")), CancellationToken.None);
                if (IsDisposed) return;
                if (result.SignInRequired) SignInRequired = true;
                else if (Saved is null)
                {
                    status.Text = result.Error ?? T("Proton did not create the route. Try another server.",
                        "A Proton não criou a rota. Tente outro servidor.", "Proton не создал маршрут. Попробуйте другой сервер.");
                    return;
                }
                creating = false;
                DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (Expected(error))
            {
                if (!IsDisposed) status.Text = error.Message;
            }
            finally
            {
                creating = false;
                if (!IsDisposed && Saved is null && !SignInRequired)
                {
                    list.Enabled = true;
                    countries.Enabled = servers.Length > 0;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lifetime.Cancel();
                lifetime.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed record CountryItem(string Code, string Name)
        {
            public override string ToString() => Name;
        }
    }
}
