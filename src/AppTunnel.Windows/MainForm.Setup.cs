using System.ComponentModel;
using System.Diagnostics;
using AppTunnel.Core;
using Microsoft.Win32;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private const string ProtonSignUp = "https://protonvpn.com/free-vpn";
    private const string WebViewDownload = "https://developer.microsoft.com/microsoft-edge/webview2/";
    private bool settingUp;

    private bool NeedsSetup => !settings.SetupDone && settings.Profiles.Count == 0;

    // First-run assistant: checks the requirements, signs in to Proton and picks the apps, then connects.
    private void RunSetup()
    {
        if (settingUp || HasPendingOperation || IsDisposed) return;
        settingUp = true;
        try
        {
            using var wizard = new SetupWizard(this);
            var finished = wizard.ShowDialog(this) == DialogResult.OK;
            settings.SetupDone = true;
            if (finished)
            {
                foreach (var target in wizard.Chosen) AddTarget(target);
                for (var i = 0; i < settings.Applications.Count; i++)
                {
                    var item = settings.Applications[i];
                    bool Listed(List<ApplicationTarget> list) =>
                        list.Any(target => target.Match.Equals(item.Match, StringComparison.OrdinalIgnoreCase));
                    if (Listed(wizard.Chosen)) settings.Applications[i] = item with { Enabled = true };
                    else if (Listed(wizard.Declined)) settings.Applications[i] = item with { Enabled = false };
                }
                settings.AutoServer = true;
            }
            Attempt(Save);
            DrawApps();
            DrawRoutes();
            UpdateState();
            // Finishing only starts a connection; when one is already running, the new choices apply to it instead.
            if (finished && activeSession is null && settings.Profiles.Count > 0 && settings.Applications.Any(x => x.Enabled))
                BeginInvoke(async () => { if (activeSession is null) await TogglePower(); });
            else if (finished && activeSession is not null) ScheduleAutoApply();
        }
        finally { settingUp = false; }
    }

    private static bool WebViewInstalled()
    {
        const string client = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        foreach (var key in new[] { $@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\{client}", $@"HKEY_LOCAL_MACHINE\SOFTWARE\{client}",
                     $@"HKEY_CURRENT_USER\Software\{client}" })
        {
            try
            {
                if (Registry.GetValue(key, "pv", null) is string version && version.Length > 0 && version != "0.0.0.0") return true;
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        return false;
    }

    private static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
    }

    private sealed class SetupWizard : Form
    {
        private readonly MainForm owner;
        private readonly StepBar steps = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
        private readonly Panel body = new() { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Background };
        private readonly ThemeButton back = MakeButton(T("Back", "Voltar", "Назад"));
        private readonly ThemeButton next = MakeButton(T("Continue", "Continuar", "Продолжить"), ButtonKind.Primary);
        private readonly System.Windows.Forms.Timer refresh = new() { Interval = 1000 };
        private readonly List<(AppTile Tile, ApplicationTarget[] Targets)> tiles = [];
        private IReadOnlyList<ApplicationTarget>? found;
        private int step;
        private string signature = "";

        public List<ApplicationTarget> Chosen { get; } = [];
        public List<ApplicationTarget> Declined { get; } = [];

        public SetupWizard(MainForm owner)
        {
            this.owner = owner;
            Text = T("AppTunnel · First-time setup", "AppTunnel · Configuração inicial", "AppTunnel · Первоначальная настройка");
            ClientSize = new Size(760, 640);
            MinimumSize = new Size(640, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Background;
            ForeColor = DesktopTheme.Text;
            Font = Body();
            ShowInTaskbar = false;
            MinimizeBox = false;
            Padding = new Padding(48, 36, 48, 24);
            Icon = owner.Icon;
            PrepareDialog(this);

            var layout = Column(44, 24, 0, 72);
            layout.BackColor = Background;
            layout.Controls.Add(steps, 0, 0);
            layout.Controls.Add(body, 0, 2);
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0), BackColor = Background,
                ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize) },
                RowStyles = { new RowStyle(SizeType.Percent, 100) }
            };
            back.Anchor = AnchorStyles.Left;
            back.MinimumSize = new Size(0, 48);
            next.Anchor = AnchorStyles.Right;
            next.MinimumSize = new Size(0, 52);
            next.Font = Body(12, bold: true);
            next.Padding = new Padding(28, 0, 28, 0);
            footer.Controls.Add(back, 0, 0);
            footer.Controls.Add(next, 2, 0);
            footer.Paint += (_, e) =>
            {
                using var pen = new Pen(Divider, Math.Max(1, DeviceDpi / 96));
                e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);

            back.Click += (_, _) => Go(step - 1);
            next.Click += async (_, _) => await Advance();
            refresh.Tick += (_, _) => Render();
            Shown += (_, _) =>
            {
                refresh.Start();
                _ = LoadApps();
            };
            Go(0);
        }

        private async Task LoadApps()
        {
            try { found = await Task.Run(ApplicationDiscovery.FindCommonApps); }
            catch (Exception error) when (Expected(error)) { found = []; }
            if (!IsDisposed && step == 2) Render(force: true);
        }

        private bool LoggedIn => owner.proton.Catalog is { Ready: true } || owner.settings.Profiles.Count > 0;

        private async Task Advance()
        {
            if (step == 1 && owner.settings.Profiles.Count == 0)
            {
                // Lists Proton's servers here; only a missing sign-in opens the Proton window.
                await owner.ChooseRoute(this);
                Render(force: true);
                return;
            }
            if (step < 2)
            {
                Go(step + 1);
                return;
            }
            Chosen.Clear();
            Declined.Clear();
            foreach (var (tile, targets) in tiles)
                (tile.On ? Chosen : Declined).AddRange(targets);
            DialogResult = DialogResult.OK;
        }

        private void Go(int index)
        {
            step = Math.Clamp(index, 0, 2);
            steps.Current = step;
            Render(force: true);
        }

        private void Render(bool force = false)
        {
            var webView = WebViewInstalled();
            var engine = owner.WireSock is not null;
            var profiles = string.Join("|", owner.settings.Profiles.Select(profile => profile.Id));
            var now = $"{step}:{webView}:{engine}:{LoggedIn}:{profiles}:{found?.Count}";
            if (!force && now == signature) return;
            signature = now;

            back.Visible = step > 0;
            next.Enabled = true;
            next.Text = step switch
            {
                1 when owner.settings.Profiles.Count == 0 => T("Choose route", "Escolher rota", "Выбрать маршрут"),
                2 => T("Connect now", "Conectar agora", "Подключиться"),
                _ => T("Continue", "Continuar", "Продолжить")
            };
            AcceptButton = next;

            body.SuspendLayout();
            foreach (Control control in body.Controls.Cast<Control>().ToArray()) control.Dispose();
            body.Controls.Clear();
            var content = step switch { 0 => Prepare(engine, webView), 1 => Account(), _ => Apps() };
            content.Dock = DockStyle.Fill;
            body.Controls.Add(content);
            body.ResumeLayout(performLayout: true);
            ThemeScrollbars(body);
        }

        private Control Page(string title, string text, out TableLayoutPanel rows, params int[] heights)
        {
            var page = Column([46, 12, 52, 20, .. heights]);
            page.BackColor = Background;
            var heading = MakeLabel(title);
            heading.Font = Heading(28.5f);
            page.Controls.Add(heading, 0, 0);
            var lead = MakeLabel(text, muted: true);
            lead.Font = Body(12.75f);
            lead.AutoEllipsis = false;
            lead.TextAlign = ContentAlignment.TopLeft;
            page.Controls.Add(lead, 0, 2);
            rows = page;
            return page;
        }

        private Control Prepare(bool engine, bool webView)
        {
            var page = Page(T("Let's get everything ready", "Vamos deixar tudo pronto", "Подготовим всё к работе"),
                T("AppTunnel checked what it needs to work.", "O AppTunnel conferiu o que precisa para funcionar.",
                    "AppTunnel проверил, что нужно для работы."), out var rows, 160, 0);
            var card = new ThemeCard(Surface) { Dock = DockStyle.Fill, Margin = new Padding(0), Radius = 14 };
            var list = Column(0, 0);
            list.BackColor = Surface;
            list.Padding = new Padding(1);
            var copy = MakeButton(T("Copy install command", "Copiar comando", "Копировать команду"));
            copy.Click += (_, _) => owner.Copy(InstallCommand);
            list.Controls.Add(new CheckRow(T("VPN engine (WireSock)", "Motor de VPN (WireSock)", "VPN-движок (WireSock)"),
                engine ? T("Installed", "Instalado", "Установлен")
                    : T("Not found. Run the command in a terminal, then come back.", "Não encontrado. Rode o comando em um terminal e volte.",
                        "Не найден. Выполните команду в терминале и вернитесь."), engine, engine ? null : copy, divider: true), 0, 0);
            var get = MakeButton(T("Download", "Baixar", "Скачать"));
            get.Click += (_, _) => OpenLink(WebViewDownload);
            list.Controls.Add(new CheckRow(T("Proton sign-in window (WebView2)", "Janela de login da Proton (WebView2)", "Окно входа Proton (WebView2)"),
                webView ? T("Installed", "Instalado", "Установлен") : T("Install the Microsoft Edge WebView2 Runtime.",
                    "Instale o Microsoft Edge WebView2 Runtime.", "Установите Microsoft Edge WebView2 Runtime."), webView, webView ? null : get,
                divider: false), 0, 1);
            card.Controls.Add(list);
            rows.Controls.Add(card, 0, 4);
            return page;
        }

        private Control Account()
        {
            var logged = LoggedIn;
            var count = owner.settings.Profiles.Count;
            var page = Page(logged ? T("Account connected", "Conta conectada", "Учётная запись подключена")
                    : T("Sign in to your Proton account", "Entre na sua conta Proton", "Войдите в учётную запись Proton"),
                logged ? count > 0
                        ? T("Your connection is saved. You can add a second country later for a backup.",
                            "Sua conexão está salva. Depois você pode adicionar outro país como reserva.",
                            "Подключение сохранено. Позже можно добавить вторую страну как резерв.")
                        : T("Now choose a route. AppTunnel lists the Proton servers and creates the connection for you.",
                            "Agora é só escolher a rota. O AppTunnel lista os servidores da Proton e cria a conexão para você.",
                            "Теперь выберите маршрут. AppTunnel покажет серверы Proton и создаст подключение.")
                    : T("Proton's official page opens in its own window. AppTunnel never sees your password.",
                        "A página oficial da Proton abre em uma janela própria. O AppTunnel nunca vê sua senha.",
                        "Официальная страница Proton откроется в отдельном окне. AppTunnel не видит ваш пароль."),
                out var rows, 0);
            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0), BackColor = Background
            };
            if (!logged)
            {
                var signIn = MakeButton(T("Sign in with Proton", "Entrar com a conta Proton", "Войти через Proton"));
                signIn.Font = Body(12, bold: true);
                signIn.MinimumSize = new Size(0, 52);
                signIn.Padding = new Padding(24, 0, 24, 0);
                signIn.Margin = new Padding(0, 0, 0, 12);
                signIn.Click += (_, _) => owner.OpenProton();
                stack.Controls.Add(signIn);
                stack.Controls.Add(LinkButton(T("No account? Create one for free", "Não tem conta? Crie uma grátis", "Нет учётной записи? Создайте бесплатно"),
                    () => OpenLink(ProtonSignUp)));
            }
            else
            {
                var note = new NoteCard(
                    count > 0 ? T($"{count} connection(s) ready", $"{count} conexão(ões) pronta(s)", $"Готово подключений: {count}")
                        : T("Automatic connection (recommended)", "Conexão automática (recomendado)", "Автоматическое подключение (рекомендуется)"),
                    count > 0 ? string.Join(", ", owner.settings.Profiles.Select(profile => profile.Name).Take(4))
                        : T("AppTunnel connects to the fastest saved server and moves to another one if it fails. Connections stay saved in your Proton account.",
                            "O AppTunnel usa o servidor salvo mais rápido e troca para outro se ele falhar. As conexões ficam salvas na sua conta Proton.",
                            "AppTunnel подключается к самому быстрому сохранённому серверу и переключается, если он отказал. Подключения хранятся в учётной записи Proton."))
                { Width = 640, Height = 96, Margin = new Padding(0, 0, 0, 12) };
                stack.Resize += (_, _) => note.Width = stack.ClientSize.Width - 2;
                stack.Controls.Add(note);
                if (count > 0)
                    // Same native server list as the Continue button; only an expired sign-in opens the Proton window.
                    stack.Controls.Add(LinkButton(T("Create another connection", "Criar outra conexão", "Создать ещё одно подключение"),
                        () => _ = ChooseAnother()));
            }
            stack.Controls.Add(LinkButton(T("I already have a .conf file", "Já tenho um arquivo .conf", "У меня уже есть файл .conf"),
                () => { owner.ImportProfiles(); Render(force: true); }, muted: true));
            rows.Controls.Add(stack, 0, 4);
            return page;
        }

        private Control Apps()
        {
            var page = Page(T("Which apps use the VPN?", "Quais apps usam a VPN?", "Каким приложениям нужен VPN?"),
                T("Only these go through Proton. The browser and the rest of the PC stay direct.",
                    "Só eles passam pela Proton. O navegador e o resto do PC continuam direto.",
                    "Через Proton пойдут только они. Браузер и остальное останутся напрямую."), out var rows, 0, 44);
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0), BackColor = Background,
                ColumnStyles = { new ColumnStyle(SizeType.Percent, 33.3f), new ColumnStyle(SizeType.Percent, 33.3f), new ColumnStyle(SizeType.Percent, 33.4f) }
            };
            var previous = tiles.ToDictionary(item => item.Tile.Text, item => item.Tile.On, StringComparer.OrdinalIgnoreCase);
            tiles.Clear();
            if (found is null)
            {
                var loading = MakeLabel(T("Looking for installed apps…", "Procurando apps instalados…", "Поиск установленных приложений…"), muted: true);
                rows.Controls.Add(loading, 0, 4);
                return page;
            }
            var groups = found.ToLookup(target => target.Label, StringComparer.OrdinalIgnoreCase);
            var detected = CommonApps.Catalog.Where(app => !app.Browser && groups[app.Name].Any()).ToList();
            var missing = CommonApps.Catalog.Where(app => !groups[app.Name].Any())
                .Where(app => app.Name is "Discord" or "Telegram" or "Signal" or "Steam" or "Spotify" or "WhatsApp")
                .Take(Math.Max(0, 6 - detected.Count));
            var index = 0;
            foreach (var app in detected.Concat(missing))
            {
                var targets = groups[app.Name].ToArray();
                var installed = targets.Length > 0;
                var already = owner.settings.Applications.Any(existing => existing.Enabled &&
                    targets.Any(target => target.Match.Equals(existing.Match, StringComparison.OrdinalIgnoreCase)));
                var tile = new AppTile
                {
                    Text = app.Name, Enabled = installed,
                    Detail = installed ? T("Detected", "Detectado", "Найдено") : T("Not installed", "Não instalado", "Не установлено"),
                    On = installed && (previous.TryGetValue(app.Name, out var was) ? was : already || DiscordClients.IsKnown(app.Name)),
                    Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 12), AccessibleName = app.Name
                };
                tiles.Add((tile, targets));
                if (grid.RowCount <= index / 3) grid.RowCount = index / 3 + 1;
                grid.Controls.Add(tile, index % 3, index / 3);
                index++;
            }
            for (var row = 0; row < grid.RowCount; row++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            // At its default size the assistant has about 260 px for the tiles; with more detected apps it grows taller
            // instead of hiding rows. The grid still scrolls automatically, so a bar appears only if the rows really do
            // not fit (a small screen, or a window made smaller by hand).
            grid.AutoScroll = true;
            var extra = Math.Max(0, grid.RowCount * 76 - 260);
            if (extra > 0)
            {
                var limit = (int)(Screen.FromControl(this).WorkingArea.Height / DpiScale(this) * .9f);
                var wanted = Math.Min(640 + extra, limit);
                if (ClientSize.Height * 96 / DeviceDpi < wanted)
                    ClientSize = new Size(ClientSize.Width, wanted * DeviceDpi / 96);
            }
            // A filler row takes the leftover height so the tiles keep their size.
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rows.Controls.Add(grid, 0, 4);
            // Apps picked here go straight into the list, switched on, like the Add app button on the Apps page.
            var other = LinkButton(T("Other app…", "Outro app…", "Другое приложение…"), owner.AddExecutable);
            other.Glyph = Glyph.Plus;
            rows.Controls.Add(MakeBar(other), 0, 5);
            return page;
        }

        private async Task ChooseAnother()
        {
            await owner.ChooseRoute(this);
            if (!IsDisposed) Render(force: true);
        }

        private static ThemeButton LinkButton(string text, Action action, bool muted = false)
        {
            var button = MakeButton(text, muted ? ButtonKind.Quiet : ButtonKind.Link);
            button.Padding = new Padding(2, 0, 6, 0);
            button.Margin = new Padding(0, 0, 0, 4);
            button.Font = Body(10.5f);
            button.Click += (_, _) => action();
            return button;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) refresh.Dispose();
            base.Dispose(disposing);
        }
    }

    // Progress bar with the three named steps of the first-run assistant.
    private sealed class StepBar : Control
    {
        private int current;

        public StepBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            AccessibleRole = AccessibleRole.ProgressBar;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Current
        {
            get => current;
            set
            {
                current = value;
                AccessibleName = T($"Step {value + 1} of 3", $"Etapa {value + 1} de 3", $"Шаг {value + 1} из 3");
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var canvas = new SolidBrush(Background);
            e.Graphics.FillRectangle(canvas, ClientRectangle);
            string[] names = [T("Prepare", "Preparar", "Подготовка"), T("Proton account", "Conta Proton", "Учётная запись Proton"), T("Apps", "Apps", "Приложения")];
            var s = DpiScale(this);
            var gap = (int)(8 * s);
            var width = (Width - gap * 2) / 3;
            for (var i = 0; i < 3; i++)
            {
                var x = i * (width + gap);
                var done = i <= current;
                PaintRounded(e.Graphics, new Rectangle(x, 0, width, Math.Max(3, (int)(4 * s))), 2 * s, done ? Accent : Border);
                var font = i == current ? Body(9.75f, bold: true) : Medium(9.75f);
                PaintText(e.Graphics, $"{i + 1}. {names[i]}", font, new Rectangle(x, (int)(12 * s), width, Height - (int)(12 * s)),
                    done ? DesktopTheme.Text : Muted, line: StringAlignment.Near);
            }
        }
    }

    // A requirement in the first step: a green check or an alert, the name, its state and an optional fix.
    private sealed class CheckRow : Control
    {
        private readonly string title;
        private readonly string detail;
        private readonly bool ok;
        private readonly bool divider;
        private readonly ThemeButton? action;

        public CheckRow(string title, string detail, bool ok, ThemeButton? action, bool divider)
        {
            this.title = title;
            this.detail = detail;
            this.ok = ok;
            this.divider = divider;
            this.action = action;
            Dock = DockStyle.Fill;
            Margin = new Padding(0);
            BackColor = Surface;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = $"{title}: {detail}";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            if (action is not null) Controls.Add(action);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (action is null) return;
            var s = DpiScale(this);
            action.Location = new Point(Width - (int)(20 * s) - action.Width, (Height - action.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var canvas = new SolidBrush(Surface);
            e.Graphics.FillRectangle(canvas, ClientRectangle);
            var s = DpiScale(this);
            var badge = new RectangleF(20 * s, (Height - 32 * s) / 2, 32 * s, 32 * s);
            PaintCircle(e.Graphics, badge, ok ? GreenWash : DangerWash);
            PaintIcon(e.Graphics, ok ? Glyph.Check : Glyph.Alert, RectangleF.Inflate(badge, -8 * s, -8 * s), ok ? Green : Danger, 2.6f);
            var left = (int)(badge.Right + 14 * s);
            var right = action is null ? Width - (int)(20 * s) : action.Left - (int)(14 * s);
            var bold = Body(10.5f, bold: true);
            var body = Body(10.5f);
            var top = (Height - bold.LineHeight(this) - body.LineHeight(this)) / 2;
            PaintText(e.Graphics, title, bold, new Rectangle(left, top, right - left, bold.LineHeight(this)), DesktopTheme.Text);
            PaintText(e.Graphics, detail, body, new Rectangle(left, top + bold.LineHeight(this), right - left, body.LineHeight(this) + (int)(2 * s)), Muted);
            if (!divider) return;
            using var pen = new Pen(Border, Math.Max(1, s));
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    // Accent-bordered note with a shield, used to explain the automatic connection.
    private sealed class NoteCard : ThemeCard
    {
        private readonly string title;
        private readonly string detail;

        public NoteCard(string title, string detail) : base(Surface)
        {
            this.title = title;
            this.detail = detail;
            Edge = Accent;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = $"{title}. {detail}";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var s = DpiScale(this);
            var badge = new Rectangle((int)(20 * s), (int)(20 * s), (int)(36 * s), (int)(36 * s));
            PaintRounded(e.Graphics, badge, 10 * s, AccentWash);
            PaintIcon(e.Graphics, Glyph.Shield, Rectangle.Inflate(badge, -(int)(9 * s), -(int)(9 * s)), Accent, 2.2f);
            var left = badge.Right + (int)(16 * s);
            var bold = Body(12.75f, bold: true);
            PaintText(e.Graphics, title, bold, new Rectangle(left, badge.Y - (int)(2 * s), Width - left - (int)(20 * s), bold.LineHeight(this)), DesktopTheme.Text);
            PaintText(e.Graphics, detail, Body(10.5f), new Rectangle(left, badge.Y + bold.LineHeight(this) + (int)(2 * s), Width - left - (int)(20 * s),
                Height - badge.Y - bold.LineHeight(this) - (int)(16 * s)), Muted, line: StringAlignment.Near, wrap: true);
        }
    }

    // Toggle tile for one app in the first-run assistant.
    private sealed class AppTile : PaintedButton
    {
        private bool on;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Detail { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool On
        {
            get => on;
            set
            {
                on = value;
                AccessibleDescription = value ? T("Selected", "Selecionado", "Выбрано") : T("Not selected", "Não selecionado", "Не выбрано");
                Invalidate();
            }
        }

        public AppTile()
        {
            AccessibleRole = AccessibleRole.CheckButton;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) On = !On;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PaintCanvas(g);
            var bounds = ClientRectangle;
            PaintRounded(g, bounds, 12 * S, on ? AccentWash : Hovering && Enabled ? Hover : Surface, on ? Accent : Border);
            PaintFocus(g, Rectangle.Inflate(bounds, -Px(3), -Px(3)), 9 * S);
            var box = new Rectangle(Px(14), (Height - Px(22)) / 2, Px(22), Px(22));
            PaintCheck(g, box, on, Enabled);
            var left = box.Right + Px(12);
            var bold = Body(12, bold: true);
            var small = Body(9.75f);
            var top = (Height - bold.LineHeight(this) - small.LineHeight(this)) / 2;
            PaintText(g, Text, bold, new Rectangle(left, top, Width - left - Px(10), bold.LineHeight(this)), Enabled ? DesktopTheme.Text : Disabled);
            PaintText(g, Detail, small, new Rectangle(left, top + bold.LineHeight(this), Width - left - Px(10), small.LineHeight(this) + Px(2)), Enabled ? Muted : Disabled);
        }
    }
}
