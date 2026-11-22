using AppTunnel.Core;
using static AppTunnel.Windows.DesktopTheme;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private const int HomePage = 0, AppsPage = 1, ServersPage = 2, DiagnosticsPage = 3, SettingsPage = 4;

    // Home
    private readonly PowerButton power = new();
    private readonly Label heroTitle = MakeLabel("");
    private readonly Label heroDetail = MakeLabel("", muted: true);
    private readonly Badge serverBadge = new(Raised, DesktopTheme.Text);
    private readonly Label serverTitle = MakeLabel("");
    private readonly Label serverDetail = MakeLabel("", muted: true);
    private readonly ThemeButton serverAction = MakeButton(T("Change", "Trocar", "Сменить"));
    private readonly Badge healthBadge = new(Raised, Muted);
    private readonly Label healthTitle = MakeLabel("");
    private readonly Label healthDetail = MakeLabel("", muted: true);
    private readonly ThemeButton healthAction = MakeButton(T("Test", "Testar", "Проверить"));
    private readonly FlowLayoutPanel chips = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };

    // Apps
    private readonly Panel appList = ScrollList();

    // Servers
    private readonly RoundCard autoCard = new(Surface);
    private readonly ThemeRadio autoRadio = new();
    private readonly Label autoDetail = MakeLabel("", muted: true);
    private readonly Panel serverList = ScrollList();
    private readonly ThemeButton switchServer = MakeButton(T("Switch to this server", "Trocar para este servidor", "Перейти на этот сервер"), ButtonKind.Primary);
    private readonly Dictionary<Guid, ServerRowParts> serverRows = new();
    private TableLayoutPanel? serversContent;
    private TableLayoutPanel? appsContent;
    private TableLayoutPanel? homeContent;
    private const int HomeHeroRow = 184, AppsHeaderRow = 76;
    private ThemeButton? advancedToggle;
    private readonly List<Control> advancedBody = [];
    private bool advancedExpanded;
    private const int ServersHeaderRow = 76, AdvancedExpandedRow = 222, AdvancedCollapsedRow = 62;

    private sealed record ServerRowParts(RoundCard Card, ThemeRadio Radio, Label Status, LatencyBar Bar, Label Latency);

    private void BuildLayout()
    {
        SuspendLayout();
        var frame = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0),
            ColumnStyles = {
                new ColumnStyle(SizeType.Absolute, 196), new ColumnStyle(SizeType.Absolute, 1),
                new ColumnStyle(SizeType.Percent, 100)
            },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        frame.Controls.Add(BuildSidebar(), 0, 0);
        frame.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Border, Margin = new Padding(0) }, 1, 0);

        var workspace = Column(0, 33);
        workspace.Padding = new Padding(24, 20, 24, 6);
        pages.AddRange([BuildHomePage(), BuildAppsPage(), BuildServersPage(), BuildDiagnosticsPage(), BuildSettingsPage()]);
        workspace.Controls.Add(pageHost, 0, 0);
        footer.ForeColor = Muted;
        workspace.Controls.Add(footer, 0, 1);
        frame.Controls.Add(workspace, 2, 0);
        Controls.Add(frame);
        OpenPage(HomePage);
        RenderProtonAccount();
        WireActions();
        ResumeLayout();
    }

    private Control BuildSidebar()
    {
        var sidebar = Column(62, 44, 44, 44, 44, 44, 0, 42);
        sidebar.BackColor = Surface;
        sidebar.Padding = new Padding(12, 17, 12, 9);
        var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        var mark = Icon?.ToBitmap();
        var picture = new PictureBox
        {
            Size = new Size(31, 31), Image = mark, SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(1, 6, 6, 0), TabStop = false
        };
        picture.Disposed += (_, _) => mark?.Dispose();
        brand.Controls.Add(picture);
        brand.Controls.Add(new Label
        {
            Text = "AppTunnel", AutoSize = true, ForeColor = DesktopTheme.Text, Font = Heading(15),
            UseCompatibleTextRendering = true, Margin = new Padding(0, 7, 0, 0)
        });
        sidebar.Controls.Add(brand, 0, 0);

        navigationButtons = [
            NavigationButton(T("Home", "Início", "Главная"), HomePage),
            NavigationButton(T("Apps", "Apps", "Приложения"), AppsPage),
            NavigationButton(T("Servers", "Servidores", "Серверы"), ServersPage),
            NavigationButton(T("Diagnostics", "Diagnóstico", "Диагностика"), DiagnosticsPage),
            NavigationButton(T("Settings", "Configurações", "Настройки"), SettingsPage)
        ];
        for (var i = 0; i < navigationButtons.Length; i++)
            sidebar.Controls.Add(navigationButtons[i], 0, i + 1);

        sidebar.Controls.Add(BuildLanguageSelector(), 0, 7);
        return sidebar;
    }

    private Control BuildHomePage()
    {
        var page = BlankPage();
        var content = Column(HomeHeroRow, 16, 132, 16, 0);
        homeContent = content;

        var hero = new RoundCard(Surface) { Dock = DockStyle.Fill, Padding = new Padding(26, 20, 28, 20), Radius = 16 };
        var heroLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
            ColumnStyles = { new ColumnStyle(SizeType.Absolute, 172), new ColumnStyle(SizeType.Percent, 100) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        power.Anchor = AnchorStyles.None;
        power.Size = new Size(136, 136);
        heroLayout.Controls.Add(power, 0, 0);
        var text = Column(30, 50, 0, 42);
        pill.Margin = new Padding(0, 2, 0, 2);
        text.Controls.Add(pill, 0, 0);
        heroTitle.Font = Heading(28);
        text.Controls.Add(heroTitle, 0, 1);
        heroDetail.Font = Body(11);
        heroDetail.AutoEllipsis = false;
        heroDetail.TextAlign = ContentAlignment.TopLeft;
        text.Controls.Add(heroDetail, 0, 2);
        text.Controls.Add(MakeBar(apply), 0, 3);
        heroLayout.Controls.Add(text, 1, 0);
        hero.Controls.Add(heroLayout);
        content.Controls.Add(hero, 0, 0);

        var pair = Pair(50);
        pair.Controls.Add(InfoCard(T("Server", "Servidor", "Сервер"), serverBadge, serverTitle, serverDetail, serverAction,
            new Padding(0, 0, 8, 0)), 0, 0);
        pair.Controls.Add(InfoCard(T("Connection", "Conexão", "Подключение"), healthBadge, healthTitle, healthDetail, healthAction,
            new Padding(8, 0, 0, 0)), 1, 0);
        content.Controls.Add(pair, 0, 2);

        var appsCard = new RoundCard(Surface) { Dock = DockStyle.Fill, Padding = new Padding(22, 14, 18, 10), Radius = 16 };
        var appsLayout = Column(36, 0);
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        header.Controls.Add(CardCaption(T("Apps on the VPN · click to turn on or off", "Apps pela VPN · clique para ligar ou desligar",
            "Приложения через VPN · нажмите, чтобы включить или выключить")), 0, 0);
        var manage = ActionButton(T("Manage", "Gerenciar", "Управление"), () => OpenPage(AppsPage), ButtonKind.Quiet);
        manage.Anchor = AnchorStyles.Right;
        header.Controls.Add(manage, 1, 0);
        appsLayout.Controls.Add(header, 0, 0);
        chips.AccessibleName = T("Apps on the VPN", "Apps pela VPN", "Приложения через VPN");
        appsLayout.Controls.Add(chips, 0, 1);
        appsCard.Controls.Add(appsLayout);
        content.Controls.Add(appsCard, 0, 4);

        page.Controls.Add(content);
        return page;
    }

    private Control InfoCard(string caption, Badge badge, Label title, Label detail, ThemeButton action, Padding margin)
    {
        var card = new RoundCard(Surface) { Dock = DockStyle.Fill, Margin = margin, Padding = new Padding(20, 12, 16, 12), Radius = 16 };
        var layout = Column(28, 0);
        layout.Controls.Add(CardCaption(caption), 0, 0);
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0),
            ColumnStyles = { new ColumnStyle(SizeType.Absolute, 58), new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        badge.Anchor = AnchorStyles.Left;
        row.Controls.Add(badge, 0, 0);
        var texts = Column(0, 0);
        title.Font = Body(12, bold: true);
        title.TextAlign = ContentAlignment.BottomLeft;
        texts.Controls.Add(title, 0, 0);
        detail.Font = Body(9);
        detail.TextAlign = ContentAlignment.TopLeft;
        texts.Controls.Add(detail, 0, 1);
        row.Controls.Add(texts, 1, 0);
        action.Anchor = AnchorStyles.Right;
        action.Margin = new Padding(8, 0, 0, 0);
        row.Controls.Add(action, 2, 0);
        layout.Controls.Add(row, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildAppsPage()
    {
        var page = BlankPage();
        var content = Column(AppsHeaderRow, 0);
        appsContent = content;
        var header = PageHeader(T("Apps", "Apps", "Приложения"), appCount);
        var add = MakeButton(T("Add app", "Adicionar app", "Добавить приложение"), ButtonKind.Primary);
        var menu = ThemedMenu();
        menu.Items.Add(T("Explore common apps…", "Explorar aplicativos comuns…", "Часто используемые приложения…"), null,
            async (_, _) => await ExploreCommonApps());
        menu.Items.Add(T("From a running app…", "De um app aberto…", "Из запущенных…"), null, (_, _) => AddRunning());
        menu.Items.Add(T("Browse for an .exe…", "Procurar um .exe…", "Выбрать .exe…"), null, (_, _) => AddExecutable());
        menu.Items.Add(T("Whole folder…", "Pasta inteira…", "Папка целиком…"), null, (_, _) => AddFolder());
        add.Click += (_, _) => menu.Show(add, new Point(0, add.Height + 4));
        add.Disposed += (_, _) => menu.Dispose();
        header.Controls.Add(HeaderActions(
            ActionButton(T("Explore common apps", "Explorar aplicativos comuns", "Часто используемые приложения"), async () => await ExploreCommonApps()),
            add), 1, 0);
        content.Controls.Add(header, 0, 0);
        appList.AccessibleName = T("Apps", "Apps", "Приложения");
        content.Controls.Add(appList, 0, 1);
        page.Controls.Add(content);
        return page;
    }

    private Control BuildServersPage()
    {
        var page = BlankPage();
        var content = Column(ServersHeaderRow, 98, 34, 0, 48, AdvancedCollapsedRow);
        var subtitle = MakeLabel(T("Latency updates by itself, at most once a minute.", "A latência é atualizada sozinha, no máximo uma vez por minuto.",
            "Задержка обновляется автоматически, не чаще раза в минуту."), muted: true);
        var header = PageHeader(T("Servers", "Servidores", "Серверы"), subtitle);
        var add = MakeButton(T("Add server", "Adicionar servidor", "Добавить сервер"), ButtonKind.Primary);
        var menu = ThemedMenu();
        menu.Items.Add(T("Create on Proton…", "Criar na Proton…", "Создать в Proton…"), null, (_, _) => OpenProton(create: true));
        menu.Items.Add(T("Proton account profiles…", "Perfis da conta Proton…", "Профили учётной записи Proton…"), null, (_, _) => ShowProtonProfiles());
        menu.Items.Add(T("Import a .conf file…", "Importar arquivo .conf…", "Импорт файла .conf…"), null, (_, _) => ImportProfiles());
        menu.Items.Add(T("Open Proton account", "Abrir conta Proton", "Открыть учётную запись Proton"), null, (_, _) => OpenProton());
        add.Click += (_, _) => menu.Show(add, new Point(0, add.Height + 4));
        add.Disposed += (_, _) => menu.Dispose();
        header.Controls.Add(HeaderActions(fastest, add), 1, 0);
        content.Controls.Add(header, 0, 0);

        autoCard.Dock = DockStyle.Fill;
        autoCard.Margin = new Padding(0, 0, 0, 12);
        autoCard.Padding = new Padding(14, 10, 18, 10);
        autoCard.Radius = 14;
        var autoLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
            ColumnStyles = { new ColumnStyle(SizeType.Absolute, 46), new ColumnStyle(SizeType.Percent, 100) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        autoRadio.Anchor = AnchorStyles.None;
        autoRadio.AccessibleName = T("Automatic server", "Servidor automático", "Автоматический выбор сервера");
        autoRadio.Click += (_, _) => SelectAutoServer();
        autoLayout.Controls.Add(autoRadio, 0, 0);
        var autoTexts = Column(0, 0);
        var autoTitle = MakeLabel(T("Automatic (recommended)", "Automático (recomendado)", "Автоматически (рекомендуется)"));
        autoTitle.Font = Body(12, bold: true);
        autoTitle.TextAlign = ContentAlignment.BottomLeft;
        autoTexts.Controls.Add(autoTitle, 0, 0);
        autoDetail.TextAlign = ContentAlignment.TopLeft;
        autoTexts.Controls.Add(autoDetail, 0, 1);
        autoLayout.Controls.Add(autoTexts, 1, 0);
        autoCard.Controls.Add(autoLayout);
        ClickThrough(autoCard, SelectAutoServer);
        content.Controls.Add(autoCard, 0, 1);

        var caption = Pair(50);
        caption.Controls.Add(CardCaption(T("Or pin a server", "Ou fixe um servidor", "Или закрепите сервер")), 0, 0);
        protonAccount.TextAlign = ContentAlignment.MiddleRight;
        caption.Controls.Add(protonAccount, 1, 0);
        content.Controls.Add(caption, 0, 2);
        serverList.AccessibleName = T("Servers", "Servidores", "Серверы");
        content.Controls.Add(serverList, 0, 3);
        var actions = MakeBar(switchServer, rename, remove);
        actions.Padding = new Padding(0, 6, 0, 0);
        content.Controls.Add(actions, 0, 4);
        content.Controls.Add(BuildAdvancedSection(), 0, 5);
        serversContent = content;
        page.Controls.Add(content);
        return page;
    }

    private Control BuildAdvancedSection()
    {
        var card = new RoundCard(Surface) { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 16, 8), Radius = 14 };
        var layout = Column(44, 40, 29, 29, 0);
        advancedToggle = MakeButton("", ButtonKind.Quiet);
        advancedToggle.Font = Body(11, bold: true);
        advancedToggle.Margin = new Padding(0, 4, 0, 4);
        advancedToggle.Anchor = AnchorStyles.Left;
        advancedToggle.Click += (_, _) => SetAdvancedExpanded(!advancedExpanded);
        layout.Controls.Add(advancedToggle, 0, 0);

        var protocol = BuildProtocolControl();
        layout.Controls.Add(protocol, 0, 1);
        var options = Pair(50);
        options.Controls.Add(lan, 0, 0);
        options.Controls.Add(ipv6, 1, 0);
        layout.Controls.Add(options, 0, 2);
        var privacy = Pair(50);
        privacy.Controls.Add(tunnelDns, 0, 0);
        privacy.Controls.Add(guardIpv6, 1, 0);
        layout.Controls.Add(privacy, 0, 3);
        var fields = Pair(50);
        fields.Controls.Add(LabeledInput(T("Always through the VPN", "Sempre pela VPN", "Всегда через VPN"), include, new Padding(0, 0, 7, 0)), 0, 0);
        fields.Controls.Add(LabeledInput(T("Never through the VPN", "Nunca pela VPN", "Никогда через VPN"), exclude, new Padding(7, 0, 0, 0)), 1, 0);
        layout.Controls.Add(fields, 0, 4);
        advancedBody.AddRange([protocol, options, privacy, fields]);
        include.AccessibleDescription = T("Optional. Enter IP addresses or CIDR ranges separated by commas.", "Opcional. Use endereços IP ou redes CIDR separados por vírgulas.", "Необязательно. Укажите IP-адреса или сети CIDR через запятую.");
        exclude.AccessibleDescription = include.AccessibleDescription;
        ipv6.AccessibleDescription = T("Available if the selected profile supports IPv6.", "Disponível quando o perfil selecionado inclui IPv6.", "Доступно, если выбранный профиль поддерживает IPv6.");
        card.Controls.Add(layout);
        return card;
    }

    // Protocol and address filters stay out of the way until asked for.
    private void SetAdvancedExpanded(bool expanded)
    {
        advancedExpanded = expanded;
        foreach (var control in advancedBody) control.Visible = expanded;
        if (advancedToggle is not null)
        {
            var caption = T("Advanced", "Avançado", "Дополнительно");
            advancedToggle.Text = caption + " · " + T("protocol, DNS, IPv6, IP filters", "protocolo, DNS, IPv6, filtros de IP", "протокол, DNS, IPv6, фильтры IP") +
                " · " + (expanded ? T("Hide", "Ocultar", "Скрыть") : T("Show", "Mostrar", "Показать"));
            advancedToggle.AccessibleName = caption;
            advancedToggle.AccessibleDescription = expanded ? T("Expanded", "Expandido", "Развёрнуто") : T("Collapsed", "Recolhido", "Свёрнуто");
        }
        if (serversContent is null) return;
        // Row heights are in device pixels once scaled; derive the factor from the fixed header row.
        var factor = serversContent.RowStyles[0].Height / ServersHeaderRow;
        serversContent.RowStyles[5].Height = (expanded ? AdvancedExpandedRow : AdvancedCollapsedRow) * factor;
        serversContent.PerformLayout();
    }

    private void DrawApps()
    {
        var ordered = settings.Applications.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        appList.SuspendLayout();
        chips.SuspendLayout();
        try
        {
            ClearList(appList);
            ClearList(chips);
            if (ordered.Length == 0)
                AddListItem(appList, EmptyCard(T("No apps yet. Use Explore common apps or Add app.",
                    "Nenhum app ainda. Use Explorar aplicativos comuns ou Adicionar app.",
                    "Приложений пока нет. Откройте каталог или добавьте приложение.")), 78, ScaleOf(appsContent, AppsHeaderRow));
            foreach (var target in ordered)
            {
                AddListItem(appList, AppRow(target), 84, ScaleOf(appsContent, AppsHeaderRow));
                var chip = new ChipToggle { Text = target.Label, Checked = target.Enabled, AccessibleName = target.Label };
                ScaleBy(chip, ScaleOf(homeContent, HomeHeroRow));
                var match = target.Match;
                chip.CheckedChanged += (_, _) => SetAppEnabled(match, chip.Checked);
                chips.Controls.Add(chip);
            }
            var more = ActionButton(T("Add app", "Adicionar app", "Добавить приложение"), () => OpenPage(AppsPage));
            more.MinimumSize = new Size(0, 40);
            more.Margin = new Padding(0, 0, 10, 10);
            ScaleBy(more, ScaleOf(homeContent, HomeHeroRow));
            chips.Controls.Add(more);
        }
        finally
        {
            chips.ResumeLayout();
            appList.ResumeLayout();
        }
    }

    private Control AppRow(ApplicationTarget target)
    {
        var on = target.Enabled;
        var card = new RoundCard(Surface, on ? InputBorder : Border) { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 14, 8), Radius = 14 };
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0),
            ColumnStyles = {
                new ColumnStyle(SizeType.Absolute, 60), new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.Absolute, 122),
                new ColumnStyle(SizeType.Absolute, 68), new ColumnStyle(SizeType.AutoSize)
            },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        row.Controls.Add(new Badge(on ? AmberWash : Raised, on ? Accent : Muted) { Text = Initials(target.Label), Anchor = AnchorStyles.Left }, 0, 0);
        var texts = Column(0, 0);
        var name = MakeLabel(target.Label);
        name.Font = Body(12, bold: true);
        name.TextAlign = ContentAlignment.BottomLeft;
        texts.Controls.Add(name, 0, 0);
        var path = MakeLabel(target.Folder ? T("Folder", "Pasta", "Папка") + " · " + target.Match : target.Match, muted: true);
        path.Font = Body(9);
        path.TextAlign = ContentAlignment.TopLeft;
        texts.Controls.Add(path, 0, 1);
        row.Controls.Add(texts, 1, 0);
        var state = new StatusPill { Anchor = AnchorStyles.Left, Size = new Size(116, 26) };
        if (on) state.ShowStatus(T("On the VPN", "Pela VPN", "Через VPN"), Green, GreenWash);
        else state.ShowStatus(T("Direct", "Direto", "Напрямую"), Muted, Raised);
        row.Controls.Add(state, 2, 0);
        var toggle = new ToggleSwitch
        {
            Checked = on, Anchor = AnchorStyles.None,
            AccessibleName = T($"Use the VPN for {target.Label}", $"Usar a VPN no {target.Label}", $"VPN для {target.Label}")
        };
        var match = target.Match;
        toggle.CheckedChanged += (_, _) => SetAppEnabled(match, toggle.Checked);
        row.Controls.Add(toggle, 3, 0);
        var remove = ActionButton(T("Remove", "Remover", "Удалить"), () => RemoveApp(match), ButtonKind.Quiet);
        remove.Anchor = AnchorStyles.Right;
        remove.Margin = new Padding(8, 0, 0, 0);
        remove.AccessibleName = T($"Remove {target.Label}", $"Remover {target.Label}", $"Удалить {target.Label}");
        row.Controls.Add(remove, 4, 0);
        card.Controls.Add(row);
        return card;
    }

    private void DrawRoutes()
    {
        serverList.SuspendLayout();
        try
        {
            ClearList(serverList);
            serverRows.Clear();
            if (settings.Profiles.Count == 0)
                AddListItem(serverList, EmptyCard(T("No servers yet. Use Add server to sign in to Proton or import a .conf file.",
                    "Nenhum servidor ainda. Use Adicionar servidor para entrar na Proton ou importar um arquivo .conf.",
                    "Серверов пока нет. Нажмите «Добавить сервер», чтобы войти в Proton или импортировать файл .conf.")), 74,
                    ScaleOf(serversContent, ServersHeaderRow));
            foreach (var profile in settings.Profiles) AddListItem(serverList, ServerRow(profile), 72, ScaleOf(serversContent, ServersHeaderRow));
        }
        finally
        {
            serverList.ResumeLayout();
        }
        DrawRouteStatuses(activeSession?.Profile.Id, settings.SelectedProfile);
    }

    private Control ServerRow(StoredProfile profile)
    {
        var card = new RoundCard(Surface) { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 16, 6), Radius = 14 };
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0),
            ColumnStyles = {
                new ColumnStyle(SizeType.Absolute, 40), new ColumnStyle(SizeType.Absolute, 60), new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 186), new ColumnStyle(SizeType.Absolute, 104)
            },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        var id = profile.Id;
        var radio = new ThemeRadio { Anchor = AnchorStyles.None, AccessibleName = profile.Name };
        radio.Click += (_, _) => SelectServer(id);
        row.Controls.Add(radio, 0, 0);
        row.Controls.Add(new Badge(Raised, DesktopTheme.Text)
        {
            Text = CountryCode(profile.Name), Anchor = AnchorStyles.Left, Size = new Size(46, 34), Font = Body(10, bold: true)
        }, 1, 0);
        var texts = Column(0, 0);
        var name = MakeLabel(profile.Name);
        name.Font = Body(11, bold: true);
        name.TextAlign = ContentAlignment.BottomLeft;
        texts.Controls.Add(name, 0, 0);
        var endpoint = MakeLabel($"{profile.Endpoint} · {VpnProtocols.Name(profile.Protocol)}" +
            (profile.HasIpv6 ? " · IPv6" : ""), muted: true);
        endpoint.Font = Body(9);
        endpoint.TextAlign = ContentAlignment.TopLeft;
        texts.Controls.Add(endpoint, 0, 1);
        row.Controls.Add(texts, 2, 0);
        var latency = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.Absolute, 86) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        var bar = new LatencyBar { Dock = DockStyle.Fill };
        latency.Controls.Add(bar, 0, 0);
        var ms = MakeLabel("—");
        ms.TextAlign = ContentAlignment.MiddleRight;
        ms.AccessibleName = T("Latency", "Latência", "Задержка");
        latency.Controls.Add(ms, 1, 0);
        row.Controls.Add(latency, 3, 0);
        var status = MakeLabel("");
        status.Font = Body(9, bold: true);
        status.TextAlign = ContentAlignment.MiddleRight;
        row.Controls.Add(status, 4, 0);
        card.Controls.Add(row);
        ClickThrough(card, () => SelectServer(id));
        serverRows[id] = new ServerRowParts(card, radio, status, bar, ms);
        return card;
    }

    private void DrawRouteStatuses(Guid? live, Guid selected)
    {
        foreach (var (id, parts) in serverRows)
        {
            var target = id == selected;
            parts.Radio.Checked = target && !settings.AutoServer;
            parts.Card.BackColor = target ? AmberWash : Surface;
            parts.Card.Edge = target ? Accent : Border;
            parts.Status.ForeColor = id == live ? Green : Accent;
            parts.Status.Text = id == live ? T("●  In use", "●  Em uso", "●  Активен")
                : target ? settings.AutoServer ? T("Fastest", "Mais rápido", "Быстрейший") : T("Selected", "Selecionado", "Выбран") : "";
            var known = latencies.TryGetValue(id, out var ms);
            parts.Bar.Milliseconds = ms;
            parts.Latency.Text = !known ? "—" : ms is { } value ? $"{value} ms" : T("No reply", "Sem resposta", "Нет ответа");
        }
        autoRadio.Checked = settings.AutoServer;
        autoCard.BackColor = settings.AutoServer ? AmberWash : Surface;
        autoCard.Edge = settings.AutoServer ? Accent : Border;
        autoDetail.Text = settings.AutoServer && SelectedRoute() is { } pick
            ? T($"Connects through the lowest-latency server. Now: {pick.Name}{LatencySuffix(pick.Id)}.",
                $"Conecta pelo servidor com menor latência. Agora: {pick.Name}{LatencySuffix(pick.Id)}.",
                $"Подключается через сервер с наименьшей задержкой. Сейчас: {pick.Name}{LatencySuffix(pick.Id)}.")
            : T("Connects through the server with the lowest latency when you turn the VPN on.",
                "Conecta pelo servidor com menor latência quando você liga a VPN.",
                "При включении VPN выбирает сервер с наименьшей задержкой.");
    }

    private void SelectServer(Guid id)
    {
        if (busy || (!settings.AutoServer && settings.SelectedProfile == id)) return;
        settings.AutoServer = false;
        settings.SelectedProfile = id;
        Changed();
    }

    private void SelectAutoServer()
    {
        if (busy || settings.AutoServer) return;
        settings.AutoServer = true;
        SelectFastestRoute();
        Changed();
    }

    // Picks the route with the lowest measured latency; returns whether the selection changed.
    private bool SelectFastestRoute()
    {
        var best = settings.Profiles
            .Select(profile => (profile, ms: latencies.TryGetValue(profile.Id, out var value) ? value : null))
            .Where(item => item.ms.HasValue).OrderBy(item => item.ms).Select(item => item.profile).FirstOrDefault();
        if (best is null || best.Id == settings.SelectedProfile) return false;
        settings.SelectedProfile = best.Id;
        return true;
    }

    private string LatencySuffix(Guid id) => latencies.TryGetValue(id, out var ms) && ms is { } value ? $" · {value} ms" : "";

    private void SetAppEnabled(string match, bool enabled)
    {
        var index = settings.Applications.FindIndex(x => x.Match.Equals(match, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || settings.Applications[index].Enabled == enabled) return;
        settings.Applications[index] = settings.Applications[index] with { Enabled = enabled };
        // The toggle that raised this event is rebuilt, so redraw after the event has finished.
        BeginInvoke(new Action(DrawApps));
        Changed();
    }

    private void RemoveApp(string match)
    {
        if (busy || settings.Applications.RemoveAll(x => x.Match.Equals(match, StringComparison.OrdinalIgnoreCase)) == 0) return;
        BeginInvoke(new Action(DrawApps));
        Changed();
    }

    private static string Initials(string label)
    {
        var words = label.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        var first = char.ToUpperInvariant(words[0][0]).ToString();
        return words.Length > 1 && char.IsLetterOrDigit(words[1][0]) ? first + char.ToUpperInvariant(words[1][0]) : first;
    }

    // Proton names start with the country code ("NL-FREE#12", "BR#3"); other names fall back to their first letters.
    private static string CountryCode(string name)
    {
        var letters = new string(name.TakeWhile(char.IsLetter).ToArray());
        if (letters.Length == 2) return letters.ToUpperInvariant();
        var fallback = new string(name.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
        return fallback.Length > 0 ? fallback : "VPN";
    }

    private static Panel ScrollList() => new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0), Padding = new Padding(0, 0, 4, 0) };

    // Rows are docked to the top in the order they are added; the holder's bottom padding is the gap between cards.
    private static void AddListItem(Control list, Control item, int height, float scale)
    {
        var holder = new Panel { Dock = DockStyle.Top, Height = height, Padding = new Padding(0, 0, 0, 10), Margin = new Padding(0) };
        holder.Controls.Add(item);
        ScaleBy(holder, scale);
        list.Controls.Add(holder);
        holder.BringToFront();
    }

    // Controls created after their page was DPI-scaled are not scaled by WinForms. A fixed row of the page,
    // written in 96-DPI units, tells how far the page itself was scaled, so new rows match it.
    private static float ScaleOf(TableLayoutPanel? reference, int designHeight) =>
        reference is null || reference.RowStyles.Count == 0 ? 1f : reference.RowStyles[0].Height / designHeight;

    private static void ScaleBy(Control control, float factor)
    {
        if (Math.Abs(factor - 1f) > .01f) control.Scale(new SizeF(factor, factor));
    }

    private static void ClearList(Control list)
    {
        var old = list.Controls.Cast<Control>().ToArray();
        list.Controls.Clear();
        foreach (var control in old) control.Dispose();
    }

    private static Control EmptyCard(string message)
    {
        var card = new RoundCard(Surface) { Dock = DockStyle.Fill, Padding = new Padding(20, 6, 20, 6), Radius = 14 };
        var label = MakeLabel(message, muted: true);
        label.AutoEllipsis = false;
        card.Controls.Add(label);
        return card;
    }

    // Lets a whole card act as one button without stealing clicks from the controls inside it.
    private static void ClickThrough(Control root, Action action)
    {
        if (root is ButtonBase or TextBoxBase or ComboBox) return;
        root.Cursor = Cursors.Hand;
        root.Click += (_, _) => action();
        foreach (Control child in root.Controls) ClickThrough(child, action);
    }

    private static TableLayoutPanel PageHeader(string title, Label subtitle)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 10),
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        var titles = Column(40, 0);
        var heading = MakeLabel(title);
        heading.Font = Heading(22);
        titles.Controls.Add(heading, 0, 0);
        subtitle.TextAlign = ContentAlignment.TopLeft;
        titles.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(titles, 0, 0);
        return header;
    }

    private static FlowLayoutPanel HeaderActions(params Control[] buttons)
    {
        var bar = MakeBar(buttons);
        bar.Dock = DockStyle.None;
        bar.AutoSize = true;
        bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        bar.Anchor = AnchorStyles.Right;
        return bar;
    }

    private static ContextMenuStrip ThemedMenu() => new()
    {
        Font = Body(10), BackColor = Surface, ForeColor = DesktopTheme.Text, ShowImageMargin = false,
        Renderer = new ToolStripProfessionalRenderer(new ThemeMenuColors())
    };

    private static Label CardCaption(string caption)
    {
        var label = MakeLabel(caption, muted: true);
        label.Font = Body(9, bold: true);
        return label;
    }

    private static Panel BlankPage() => new() { Dock = DockStyle.Fill, BackColor = Background, Margin = new Padding(0) };

    private Control LabeledInput(string name, TextBox input, Padding margin)
    {
        var content = Column(22, 32);
        content.Margin = margin;
        var label = MakeLabel(name, muted: true);
        label.Font = Body(9, bold: true);
        content.Controls.Add(label, 0, 0);
        input.AccessibleName = name;
        content.Controls.Add(InputFrame(input), 0, 1);
        return content;
    }

    private Control BuildDiagnosticsPage()
    {
        var page = MakePage();
        var content = Column(32, 51, 41, 29, 30, 0, 42);
        content.Controls.Add(SectionTitle(T("WireSock and logs", "WireSock e registros", "WireSock и журналы")), 0, 0);
        content.Controls.Add(engineInfo, 0, 1);
        content.Controls.Add(MakeBar(
            ActionButton(T("Locate WireSock…", "Localizar WireSock…", "Найти WireSock…"), ChooseEngine),
            ActionButton(T("Detect again", "Detectar novamente", "Повторить поиск"), () => Attempt(UpdateEngineInfo)),
            ActionButton(T("Copy command", "Copiar comando", "Копировать команду"), () => Copy(InstallCommand)),
            ActionButton(T("Copy Proton guide", "Copiar guia do Proton", "Копировать ссылку Proton"), () => Copy(ProtonUrl))), 0, 2);
        content.Controls.Add(diagnostics, 0, 3);
        content.Controls.Add(healthDetails, 0, 4);
        log.ReadOnly = true;
        log.WordWrap = false;
        log.ScrollBars = ScrollBars.Both;
        log.Font = Mono;
        log.AccessibleName = T("WireSock logs", "Registros do WireSock", "Журналы WireSock");
        var shell = new ThemeCard(Field)
        {
            Dock = DockStyle.Fill, Padding = new Padding(12, 9, 10, 9), Margin = new Padding(0)
        };
        shell.Controls.Add(log);
        content.Controls.Add(shell, 0, 5);
        content.Controls.Add(MakeBar(
            ActionButton(T("Copy logs", "Copiar registros", "Копировать журналы"), () => Copy(log.Text), ButtonKind.Quiet),
            ActionButton(T("Clear logs", "Limpar registros", "Очистить журналы"), ClearLog, ButtonKind.Quiet)), 0, 6);
        page.Controls.Add(content);
        return page;
    }

    private ThemeButton NavigationButton(string label, int index)
    {
        var button = ActionButton(label, () => OpenPage(index), ButtonKind.Navigation);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 1, 0, 3);
        button.Padding = new Padding(15, 0, 9, 0);
        return button;
    }

    private void OpenPage(int index)
    {
        if (index < 0 || index >= pages.Count) return;
        var active = pages[index];
        if (active.Parent == pageHost) return;
        pageHost.SuspendLayout();
        try
        {
            pageHost.Controls.Clear();
            pageHost.Controls.Add(active);
            CurrentPage = index;
            if (index is HomePage or ServersPage) _ = RefreshLatencyQuietly();
            for (var i = 0; i < navigationButtons.Length; i++) navigationButtons[i].Selected = i == index;
        }
        finally
        {
            pageHost.ResumeLayout(performLayout: true);
            pageHost.Invalidate(invalidateChildren: true);
        }
    }

    private ThemeButton ActionButton(string label, Action action, ButtonKind kind = ButtonKind.Secondary)
    {
        var button = MakeButton(label, kind);
        button.Click += (_, _) => action();
        return button;
    }

    private static Control MakePage() => new ThemeCard(Surface)
    {
        Dock = DockStyle.Fill, Padding = new Padding(17, 12, 17, 11), Margin = new Padding(0)
    };

    private static Control InputFrame(TextBox textBox)
    {
        var frame = new ThemeCard(Field)
        {
            Dock = DockStyle.Fill, Padding = new Padding(9, 6, 9, 4), Margin = new Padding(0), Edge = InputBorder
        };
        // A stronger border marks the field boundary, and the accent shows which field has focus.
        textBox.Enter += (_, _) => frame.Edge = Accent;
        textBox.Leave += (_, _) => frame.Edge = InputBorder;
        frame.Controls.Add(textBox);
        return frame;
    }

    private static TableLayoutPanel Column(params int[] heights)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length, Margin = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in heights)
            table.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        return table;
    }

    private static TableLayoutPanel Pair(int left) => new()
    {
        Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
        ColumnStyles = { new ColumnStyle(SizeType.Percent, left), new ColumnStyle(SizeType.Percent, 100 - left) },
        RowStyles = { new RowStyle(SizeType.Percent, 100) }
    };

    private static FlowLayoutPanel MakeBar(params Control[] controls)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false
        };
        bar.Controls.AddRange(controls);
        return bar;
    }

    private static Label MakeLabel(string caption, bool muted = false) => new()
    {
        Text = caption, Dock = DockStyle.Fill, AutoEllipsis = true, Font = Body(),
        ForeColor = muted ? Muted : DesktopTheme.Text, TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0), UseCompatibleTextRendering = true
    };

    private static Label SectionTitle(string caption)
    {
        var label = MakeLabel(caption);
        label.Font = Heading(15);
        return label;
    }

    private static ThemeButton MakeButton(string caption, ButtonKind kind = ButtonKind.Secondary) =>
        new() { Text = caption, Kind = kind };

    private static CheckBox MakeCheck(string caption) => new ThemeCheckBox
    {
        Text = caption, Dock = DockStyle.Fill, Font = Body(), FlatStyle = FlatStyle.Flat,
        ForeColor = DesktopTheme.Text, Margin = new Padding(0), Cursor = Cursors.Hand
    };

    private static TextBox MakeText(bool multiline = false, string placeholder = "") => new()
    {
        BackColor = Field, ForeColor = DesktopTheme.Text, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
        Font = Body(), Multiline = multiline, PlaceholderText = placeholder, MaxLength = 16000, Margin = new Padding(0)
    };

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBar(Handle);
    }
}
