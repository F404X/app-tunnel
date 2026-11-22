using System.Diagnostics;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private bool onboarding;

    // First run: check the components, get one Proton server and pick the apps, then connect.
    private async void ShowOnboarding()
    {
        if (onboarding || IsDisposed || !Visible || HasPendingOperation ||
            Application.OpenForms.Cast<Form>().Any(form => form.Modal)) return;
        onboarding = true;
        try
        {
            var connect = RunOnboarding();
            settings.SetupDone = true;
            if (settings.Profiles.Count > 1) settings.AutoServer = true;
            DrawApps();
            DrawRoutes();
            Changed();
            if (connect && !IsDisposed)
            {
                if (settings.AutoServer) SelectFastestRoute();
                if (SelectedRoute() is not null && settings.Applications.Any(x => x.Enabled))
                    await ChangeConnection(starting: true);
            }
        }
        catch (Exception error) when (Expected(error)) { Failure(error); }
        finally { onboarding = false; }
    }

    private bool RunOnboarding()
    {
        var heading = T("Set up AppTunnel", "Configurar o AppTunnel", "Настройка AppTunnel");
        using var window = new Form
        {
            Text = heading, ClientSize = new Size(780, 580), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            BackColor = Background, ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(32, 24, 32, 20)
        };
        PrepareDialog(window);
        var layout = Column(52, 0, 60);
        window.Controls.Add(layout);

        var names = new[] { T("Prepare", "Preparar", "Подготовка"), T("Proton account", "Conta Proton", "Учётная запись Proton"), T("Apps", "Apps", "Приложения") };
        var stepper = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        var bars = new Panel[3];
        var labels = new Label[3];
        for (var i = 0; i < 3; i++)
        {
            stepper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            var cell = Column(6, 0);
            cell.Margin = new Padding(0, 0, i < 2 ? 10 : 0, 0);
            bars[i] = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            labels[i] = MakeLabel($"{i + 1}. {names[i]}");
            labels[i].Font = Body(9, bold: true);
            cell.Controls.Add(bars[i], 0, 0);
            cell.Controls.Add(labels[i], 0, 1);
            stepper.Controls.Add(cell, i, 0);
        }
        stepper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(stepper, 0, 0);

        var host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(host, 0, 1);

        var back = MakeButton(T("Back", "Voltar", "Назад"));
        var next = MakeButton("", ButtonKind.Primary);
        var later = MakeButton(T("Finish without connecting", "Concluir sem conectar", "Завершить без подключения"), ButtonKind.Quiet);
        var footerBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 12, 0, 0),
            ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.AutoSize) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        back.Anchor = later.Anchor = next.Anchor = AnchorStyles.None;
        footerBar.Controls.Add(back, 0, 0);
        footerBar.Controls.Add(later, 2, 0);
        footerBar.Controls.Add(next, 3, 0);
        layout.Controls.Add(footerBar, 0, 2);

        var step = 0;
        var connect = false;
        var serverWaiting = new Label();
        var choices = new List<(CheckBox Check, bool Initial, ApplicationTarget[] Matches)>();
        Task<IReadOnlyList<ApplicationTarget>>? discovery = null;

        Control StatusRow(bool ready, string title, string detail, params Control[] actions)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0),
                ColumnStyles = { new ColumnStyle(SizeType.Absolute, 56), new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize) },
                RowStyles = { new RowStyle(SizeType.Percent, 100) }
            };
            row.Controls.Add(new Badge(ready ? GreenWash : AmberWash, ready ? Green : Amber)
            {
                Text = ready ? "OK" : "!", Anchor = AnchorStyles.Left, Size = new Size(40, 40), Font = Body(10, bold: true)
            }, 0, 0);
            var texts = Column(0, 0);
            var name = MakeLabel(title);
            name.Font = Body(11, bold: true);
            name.TextAlign = ContentAlignment.BottomLeft;
            texts.Controls.Add(name, 0, 0);
            var info = MakeLabel(detail, muted: true);
            info.Font = Body(9);
            info.TextAlign = ContentAlignment.TopLeft;
            texts.Controls.Add(info, 0, 1);
            row.Controls.Add(texts, 1, 0);
            if (actions.Length > 0)
            {
                var bar = HeaderActions(actions);
                row.Controls.Add(bar, 2, 0);
            }
            return row;
        }

        Control Intro(Control body, string title, string text, int bodyHeight)
        {
            var page = bodyHeight == 0 ? Column(50, 52, 0) : Column(50, 52, bodyHeight, 0);
            var heading = MakeLabel(title);
            heading.Font = Heading(24);
            page.Controls.Add(heading, 0, 0);
            var subtitle = MakeLabel(text, muted: true);
            subtitle.Font = Body(11);
            subtitle.AutoEllipsis = false;
            subtitle.TextAlign = ContentAlignment.TopLeft;
            page.Controls.Add(subtitle, 0, 1);
            page.Controls.Add(body, 0, 2);
            return page;
        }

        Control PrepareStep()
        {
            var located = settings.EnginePath.Length > 0 && File.Exists(settings.EnginePath) ? settings.EnginePath : WireSockEngine.Locate();
            var helper = File.Exists(Path.Combine(AppContext.BaseDirectory, "AppTunnel.Proton.exe"));
            var card = new RoundCard(Surface) { Dock = DockStyle.Fill, Padding = new Padding(18, 8, 18, 8), Radius = 14 };
            var rows = Column(0, 0);
            // The button is rebuilt by ShowStep, so rebuild after its click has finished.
            var check = ActionButton(T("Check again", "Verificar de novo", "Проверить снова"), () => window.BeginInvoke(new Action(() => ShowStep(0))));
            rows.Controls.Add(located is null
                ? StatusRow(false, T("VPN engine (WireSock) missing", "Motor de VPN (WireSock) ausente", "Движок VPN (WireSock) не найден"),
                    T("Install it with the command, then check again.", "Instale com o comando e verifique de novo.", "Установите его командой и проверьте снова."),
                    ActionButton(T("Copy command", "Copiar comando", "Копировать команду"), () => Copy(InstallCommand)), check)
                : StatusRow(true, T("VPN engine (WireSock)", "Motor de VPN (WireSock)", "Движок VPN (WireSock)"),
                    T($"Installed · version {FileVersionInfo.GetVersionInfo(located).FileVersion}", $"Instalado · versão {FileVersionInfo.GetVersionInfo(located).FileVersion}",
                        $"Установлен · версия {FileVersionInfo.GetVersionInfo(located).FileVersion}")), 0, 0);
            rows.Controls.Add(StatusRow(helper, T("Proton sign-in window", "Janela de login da Proton", "Окно входа Proton"),
                helper ? T("Ready. AppTunnel never sees your password.", "Pronta. O AppTunnel nunca vê sua senha.", "Готово. AppTunnel не видит ваш пароль.")
                    : T("Missing. Reinstall AppTunnel or import a .conf file in the next step.", "Ausente. Reinstale o AppTunnel ou importe um .conf na próxima etapa.",
                        "Не найдено. Переустановите AppTunnel или импортируйте .conf на следующем шаге.")), 0, 1);
            card.Controls.Add(rows);
            return Intro(card, T("Let's get everything ready", "Vamos deixar tudo pronto", "Всё подготовим"),
                T("AppTunnel checked what it needs to work.", "O AppTunnel conferiu o que precisa para funcionar.", "AppTunnel проверил, что нужно для работы."), 160);
        }

        Control ProtonStep()
        {
            var card = new RoundCard(Surface) { Dock = DockStyle.Fill, Padding = new Padding(18, 8, 18, 8), Radius = 14 };
            serverWaiting = MakeLabel("");
            card.Controls.Add(serverWaiting);
            var actions = MakeBar(
                ActionButton(T("Open Proton and create a connection", "Abrir a Proton e criar conexão", "Открыть Proton и создать подключение"),
                    () => OpenProton(create: true), ButtonKind.Primary),
                ActionButton(T("I already have a .conf file", "Já tenho um arquivo .conf", "У меня есть файл .conf"), () => ImportProfiles(window)));
            actions.Padding = new Padding(0, 14, 0, 0);
            var body = Column(84, 60, 0);
            body.Controls.Add(card, 0, 0);
            body.Controls.Add(actions, 0, 1);
            var tip = MakeLabel(T("Tip: create a second connection on another server to have a backup.",
                "Dica: crie uma segunda conexão em outro servidor para ter uma reserva.",
                "Совет: создайте второе подключение на другом сервере как резерв."), muted: true);
            tip.TextAlign = ContentAlignment.TopLeft;
            body.Controls.Add(tip, 0, 2);
            RefreshServerWaiting();
            return Intro(body, T("Connect your Proton account", "Conecte sua conta Proton", "Подключите учётную запись Proton"),
                T("Proton's own page opens in its window. Create a WireGuard connection there and AppTunnel saves it by itself.",
                    "A página oficial da Proton abre numa janela própria. Crie lá uma conexão WireGuard e o AppTunnel salva sozinho.",
                    "Официальная страница Proton откроется в отдельном окне. Создайте там подключение WireGuard — AppTunnel сохранит его сам."), 0);
        }

        void RefreshServerWaiting()
        {
            var first = settings.Profiles.FirstOrDefault();
            var text = first is null
                ? T("Waiting for a server…", "Aguardando um servidor…", "Ожидание сервера…")
                : T($"Ready: {string.Join(", ", settings.Profiles.Select(x => x.Name))}", $"Pronto: {string.Join(", ", settings.Profiles.Select(x => x.Name))}",
                    $"Готово: {string.Join(", ", settings.Profiles.Select(x => x.Name))}");
            if (serverWaiting.Text == text) return;
            serverWaiting.Text = text;
            serverWaiting.Font = Body(12, bold: true);
            serverWaiting.ForeColor = first is null ? Muted : Green;
        }

        Control AppsStep()
        {
            var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0) };
            var waiting = MakeLabel(T("Looking for installed apps…", "Procurando apps instalados…", "Поиск установленных приложений…"), muted: true);
            waiting.Dock = DockStyle.None;
            waiting.AutoSize = true;
            cards.Controls.Add(waiting);
            discovery ??= Task.Run(ApplicationDiscovery.FindCommonApps);
            _ = FillApps(cards);
            var more = ActionButton(T("Other app…", "Outro app…", "Другое приложение…"), () =>
            {
                AddExecutable(window);
                UpdateButtons();
            }, ButtonKind.Quiet);
            var body = Column(0, 44);
            body.Controls.Add(cards, 0, 0);
            body.Controls.Add(MakeBar(more), 0, 1);
            return Intro(body, T("Which apps use the VPN?", "Quais apps usam a VPN?", "Какие приложения используют VPN?"),
                T("Only they go through Proton. Your browser and the rest of the PC stay direct.",
                    "Só eles passam pela Proton. O navegador e o resto do PC continuam direto.",
                    "Только они идут через Proton. Браузер и остальной компьютер — напрямую."), 0);
        }

        async Task FillApps(FlowLayoutPanel cards)
        {
            IReadOnlyList<ApplicationTarget> found;
            try { found = await discovery!; }
            catch (Exception error) when (Expected(error)) { found = []; }
            if (cards.IsDisposed) return;
            ClearList(cards);
            choices.Clear();
            var groups = found.ToLookup(target => target.Label, StringComparer.OrdinalIgnoreCase);
            var anyEnabled = settings.Applications.Any(x => x.Enabled);
            foreach (var app in CommonApps.Catalog)
            {
                var matches = groups[app.Name].ToArray();
                if (matches.Length == 0) continue;
                var selected = anyEnabled
                    ? matches.Any(match => settings.Applications.Any(existing => existing.Enabled && (CoversTarget(existing, match) || CoversTarget(match, existing))))
                    : app.Name.StartsWith("Discord", StringComparison.Ordinal) || app.Name == "Serein";
                var card = new RoundCard(Surface) { Size = new Size(222, 64), Margin = new Padding(0, 0, 10, 10), Padding = new Padding(14, 6, 10, 6), Radius = 12 };
                var check = MakeCheck(app.Name);
                check.Checked = selected;
                check.Font = Body(11, bold: true);
                check.CheckedChanged += (_, _) =>
                {
                    card.BackColor = check.Checked ? AmberWash : Surface;
                    card.Edge = check.Checked ? Accent : Border;
                    UpdateButtons();
                };
                card.BackColor = selected ? AmberWash : Surface;
                card.Edge = selected ? Accent : Border;
                card.Controls.Add(check);
                ScaleBy(card, ScaleOf(layout, 52));
                cards.Controls.Add(card);
                choices.Add((check, selected, matches));
            }
            if (choices.Count == 0)
            {
                var none = MakeLabel(T("No common apps found. Use Other app to pick an .exe.", "Nenhum app comum encontrado. Use Outro app para escolher um .exe.",
                    "Популярные приложения не найдены. Выберите .exe через «Другое приложение»."), muted: true);
                none.Dock = DockStyle.None;
                none.AutoSize = true;
                cards.Controls.Add(none);
            }
            UpdateButtons();
        }

        void ApplyChoices()
        {
            foreach (var (check, initial, matches) in choices)
            {
                if (check.Checked == initial && !check.Checked) continue;
                for (var i = 0; i < settings.Applications.Count; i++)
                {
                    var item = settings.Applications[i];
                    if (matches.Any(match => CoversTarget(match, item) || CoversTarget(item, match)))
                        settings.Applications[i] = item with { Enabled = check.Checked };
                }
                if (check.Checked)
                    foreach (var match in matches) AddTarget(match);
            }
        }

        void UpdateButtons()
        {
            for (var i = 0; i < 3; i++)
            {
                bars[i].BackColor = i <= step ? Accent : Border;
                labels[i].ForeColor = i <= step ? DesktopTheme.Text : Muted;
            }
            back.Visible = step > 0;
            later.Visible = step == 2;
            next.Text = step == 2 ? T("Connect now", "Conectar agora", "Подключиться") : T("Continue", "Continuar", "Далее");
            next.Enabled = step switch
            {
                1 => settings.Profiles.Count > 0,
                2 => choices.Any(choice => choice.Check.Checked) || settings.Applications.Any(x => x.Enabled),
                _ => true
            };
        }

        void ShowStep(int index)
        {
            step = index;
            host.SuspendLayout();
            ClearList(host);
            var page = index switch { 0 => PrepareStep(), 1 => ProtonStep(), _ => AppsStep() };
            ScaleBy(page, ScaleOf(layout, 52));
            host.Controls.Add(page);
            host.ResumeLayout();
            UpdateButtons();
        }

        back.Click += (_, _) => ShowStep(Math.Max(0, step - 1));
        next.Click += (_, _) =>
        {
            if (step < 2)
            {
                ShowStep(step + 1);
                return;
            }
            ApplyChoices();
            connect = true;
            window.DialogResult = DialogResult.OK;
        };
        later.Click += (_, _) =>
        {
            ApplyChoices();
            window.DialogResult = DialogResult.OK;
        };

        // Profiles created in the Proton window arrive through this form's own polling; mirror them here.
        using var refresh = new System.Windows.Forms.Timer { Interval = 500 };
        refresh.Tick += (_, _) =>
        {
            if (step != 1) return;
            RefreshServerWaiting();
            UpdateButtons();
        };
        refresh.Start();
        ShowStep(0);
        window.ShowDialog(this);
        return connect;
    }
}
