using static AppTunnel.Windows.DesktopTheme;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private const int HomePage = 0, AppsPage = 1, ServersPage = 2, DiagnosticsPage = 3, SettingsPage = 4;

    private void BuildLayout()
    {
        SuspendLayout();
        var frame = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = Background,
            ColumnStyles = { new ColumnStyle(SizeType.Absolute, 220), new ColumnStyle(SizeType.Percent, 100) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        frame.Controls.Add(BuildSidebar(), 0, 0);

        var workspace = Column(0, 56);
        workspace.BackColor = Background;
        workspace.Padding = new Padding(32, 28, 32, 4);
        pages.AddRange([BuildHomePage(), BuildAppsPage(), BuildServersPage(), BuildDiagnosticsPage(), BuildSettingsPage()]);
        // Every page stays in the tree, hidden when inactive, so the window scales all of them for the monitor DPI.
        foreach (var page in pages)
        {
            page.Visible = false;
            pageHost.Controls.Add(page);
        }
        workspace.Controls.Add(pageHost, 0, 0);
        workspace.Controls.Add(footer, 0, 1);
        footer.Dock = DockStyle.Fill;
        footer.Margin = new Padding(0);
        frame.Controls.Add(workspace, 1, 0);
        Controls.Add(frame);
        OpenPage(HomePage);
        RenderProtonAccount();
        WireActions();
        ResumeLayout();
    }

    private Control BuildSidebar()
    {
        var sidebar = new SidebarPanel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(12, 20, 13, 12) };
        var column = Column(62, 48, 48, 48, 48, 48, 0, 40);
        column.BackColor = Surface;
        column.Controls.Add(new BrandMark { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) }, 0, 0);
        navigationButtons = [
            NavigationButton(T("Home", "Início", "Главная"), Glyph.Power, HomePage),
            NavigationButton(T("Apps", "Apps", "Приложения"), Glyph.Apps, AppsPage),
            NavigationButton(T("Servers", "Servidores", "Серверы"), Glyph.Globe, ServersPage),
            NavigationButton(T("Diagnostics", "Diagnóstico", "Диагностика"), Glyph.Pulse, DiagnosticsPage),
            NavigationButton(T("Settings", "Configurações", "Настройки"), Glyph.Sliders, SettingsPage)
        ];
        for (var i = 0; i < navigationButtons.Length; i++)
            column.Controls.Add(navigationButtons[i], 0, i + 1);
        column.Controls.Add(BuildLanguageSelector(), 0, 7);
        sidebar.Controls.Add(column);
        return sidebar;
    }

    private Control BuildHomePage()
    {
        var page = Column(196, 20, 126, 20, 150, 0);
        page.BackColor = Background;
        hero.Dock = DockStyle.Fill;
        hero.Margin = new Padding(0);
        hero.AccessibleName = T("Connection", "Conexão", "Подключение");
        page.Controls.Add(hero, 0, 0);

        var cards = Pair(50);
        cards.BackColor = Background;
        serverCard.Dock = DockStyle.Fill;
        serverCard.Margin = new Padding(0, 0, 10, 0);
        healthCard.Dock = DockStyle.Fill;
        healthCard.Margin = new Padding(10, 0, 0, 0);
        cards.Controls.Add(serverCard, 0, 0);
        cards.Controls.Add(healthCard, 1, 0);
        page.Controls.Add(cards, 0, 2);

        var manage = MakeButton(T("Manage", "Gerenciar", "Управлять"), ButtonKind.Link);
        manage.Click += (_, _) => OpenPage(AppsPage);
        var card = new CaptionCard(T("Apps on the VPN · click to turn on or off", "Apps pela VPN · clique para ligar ou desligar",
            "Приложения через VPN · нажмите, чтобы включить или выключить"), manage)
        {
            Dock = DockStyle.Fill, Margin = new Padding(0)
        };
        chips.Dock = DockStyle.Fill;
        chips.BackColor = Surface;
        chips.AutoScroll = true;
        chips.Margin = new Padding(0);
        chips.AccessibleName = T("Apps on the VPN", "Apps pela VPN", "Приложения через VPN");
        card.Controls.Add(chips);
        page.Controls.Add(card, 0, 4);
        // Row heights are device pixels once the window has scaled, so the fit is measured in device pixels too.
        void Fit()
        {
            if (chips.Width <= 0) return;
            var needed = card.Padding.Vertical + chips.GetPreferredSize(new Size(chips.Width, 0)).Height;
            var room = page.Height - page.GetRowHeights().Take(4).Sum();
            var height = Math.Max(card.Padding.Vertical + 54 * DeviceDpi / 96, Math.Min(needed, room));
            if (Math.Abs(page.RowStyles[4].Height - height) < 1) return;
            page.RowStyles[4].Height = height;
        }
        void Queue()
        {
            if (IsHandleCreated) BeginInvoke(Fit);
        }
        chips.Layout += (_, _) => Queue();
        page.Resize += (_, _) => Queue();
        return page;
    }

    private Control BuildAppsPage()
    {
        var page = Column(84, 18, 0, 14, 96);
        page.BackColor = Background;
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = Background,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        header.Controls.Add(new PageHeader(T("Apps", "Apps", "Приложения"),
            T("Turn on the apps that should use the VPN. Changes apply right away.",
                "Ligue os apps que devem passar pela VPN. As mudanças valem na hora.",
                "Включите приложения, которым нужен VPN. Изменения применяются сразу."))
            { Dock = DockStyle.Fill }, 0, 0);
        var add = MakeButton(T("Add app", "Adicionar app", "Добавить приложение"), ButtonKind.Primary);
        add.Glyph = Glyph.Plus;
        add.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        add.MinimumSize = new Size(0, 46);
        add.Margin = new Padding(16, 0, 0, 4);
        var menu = MakeMenu();
        menu.Add(T("From a running app…", "De um app aberto…", "Из запущенных…"), Glyph.Window, (_, _) => AddRunning());
        menu.Add(T("Browse for an .exe…", "Procurar no PC…", "Выбрать файл .exe…"), Glyph.File, (_, _) => AddExecutable());
        menu.Add(T("Whole folder…", "Pasta inteira…", "Папка целиком…"), Glyph.Folder, (_, _) => AddFolder());
        menu.AddSeparator();
        menu.Add(T("Explore common apps…", "Explorar apps comuns…", "Часто используемые приложения…"), Glyph.Compass,
            async (_, _) => await ExploreCommonApps());
        add.Click += (_, _) => menu.Show(add, new Point(add.Width - menu.GetPreferredSize(Size.Empty).Width, add.Height + 4));
        add.Disposed += (_, _) => menu.Dispose();
        header.Controls.Add(add, 1, 0);
        page.Controls.Add(header, 0, 0);

        appList.Dock = DockStyle.Fill;
        appList.BackColor = Background;
        appList.Margin = new Padding(0);
        appList.AccessibleName = T("Apps on the VPN", "Apps pela VPN", "Приложения через VPN");
        page.Controls.Add(appList, 0, 2);

        dropZone.Dock = DockStyle.Fill;
        dropZone.Margin = new Padding(0);
        page.Controls.Add(dropZone, 0, 4);
        page.AllowDrop = true;
        foreach (var target in new Control[] { page, appList, dropZone })
        {
            target.AllowDrop = true;
            target.DragEnter += (_, e) => dropZone.Active = AcceptDrop(e);
            target.DragOver += (_, e) => AcceptDrop(e);
            target.DragLeave += (_, _) => dropZone.Active = false;
            target.DragDrop += (_, e) =>
            {
                dropZone.Active = false;
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddDropped(paths);
            };
        }
        return page;
    }

    private static bool AcceptDrop(DragEventArgs e)
    {
        var accepted = e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
        e.Effect = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        return accepted;
    }

    private Control BuildServersPage()
    {
        var page = Column(84, 18, 0);
        page.BackColor = Background;
        page.Controls.Add(new PageHeader(T("Servers", "Servidores", "Серверы"),
            T("Latency is measured on its own about once a minute.",
                "A latência é medida sozinha cerca de uma vez por minuto.",
                "Задержка измеряется автоматически примерно раз в минуту."))
            { Dock = DockStyle.Fill }, 0, 0);

        // Only the pinned servers scroll; Automatic above and the account actions below always stay visible.
        serverList.Dock = DockStyle.Fill;
        serverList.BackColor = Background;
        serverList.Margin = new Padding(0);
        serverList.AccessibleName = T("Server", "Servidor", "Сервер");
        serverTop.Dock = DockStyle.Top;
        serverTop.BackColor = Background;
        serverTop.Padding = new Padding(0, 0, 0, serverTop.Gap);
        serverBottom.Dock = DockStyle.Bottom;
        serverBottom.BackColor = Background;
        serverBottom.Padding = new Padding(0, serverBottom.Gap, 0, 0);
        pinCaption.Text = T("Or pin a server", "Ou fixe um servidor", "Или закрепите сервер").ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        pinCaption.Font = Body(9.75f, bold: true);
        pinCaption.Padding = new Padding(4, 8, 0, 0);
        pinCaption.Dock = DockStyle.None;
        pinCaption.Height = 30;
        serverTop.Controls.Add(pinCaption);
        emptyRoutes.Dock = DockStyle.None;
        emptyRoutes.Height = 64;
        emptyRoutes.AutoEllipsis = false;
        emptyRoutes.Padding = new Padding(4, 0, 0, 0);
        emptyRoutes.Text = T("No servers yet. Sign in to Proton to create a WireGuard connection, or import a .conf file.",
            "Nenhum servidor ainda. Entre na Proton para criar uma conexão WireGuard ou importe um arquivo .conf.",
            "Серверов пока нет. Войдите в Proton, чтобы создать подключение WireGuard, или импортируйте файл .conf.");
        serverTop.Controls.Add(emptyRoutes);

        var addServer = MakeButton(T("Add another country", "Adicionar outro país", "Добавить другую страну"), ButtonKind.Link);
        addServer.Glyph = Glyph.Plus;
        addServer.Padding = new Padding(4, 0, 8, 0);
        var choose = MakeButton(T("Choose route", "Escolher rota", "Выбрать маршрут"), ButtonKind.Primary);
        choose.Glyph = Glyph.Globe;
        choose.Margin = new Padding(0, 2, 12, 2);
        choose.Click += async (_, _) => await ChooseRoute();
        var menu = MakeMenu();
        menu.Add(T("Choose route…", "Escolher rota…", "Выбрать маршрут…"), Glyph.Globe, async (_, _) => await ChooseRoute());
        menu.Add(T("Create in the Proton window…", "Criar na janela da Proton…", "Создать в окне Proton…"), Glyph.Plus, (_, _) => OpenProton(create: true));
        menu.Add(T("Proton account profiles…", "Perfis da conta Proton…", "Профили учётной записи Proton…"), Glyph.User, (_, _) => ShowProtonProfiles());
        menu.Add(T("Import a .conf file…", "Importar arquivo .conf…", "Импортировать файл .conf…"), Glyph.Import, (_, _) => ImportProfiles());
        menu.AddSeparator();
        menu.Add(T("Open Proton account", "Abrir conta Proton", "Открыть учётную запись Proton"), Glyph.External, (_, _) => OpenProton());
        addServer.Click += (_, _) => menu.Show(addServer, new Point(0, addServer.Height + 4));
        addServer.Disposed += (_, _) => menu.Dispose();
        addServer.Text = T("More options", "Mais opções", "Другие варианты");
        var addBar = MakeBar(choose, addServer);
        addBar.BackColor = Background;
        addBar.Dock = DockStyle.None;
        addBar.Height = 52;
        // The account hint gets its own line, so it never pushes the buttons or gets cut off beside them.
        protonAccount.Dock = DockStyle.None;
        protonAccount.Height = 22;
        protonAccount.Padding = new Padding(4, 0, 0, 0);
        serverBottom.Controls.Add(addBar);
        serverBottom.Controls.Add(protonAccount);
        serverBottom.Controls.Add(BuildAdvanced());
        var area = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Background };
        area.Controls.Add(serverList);
        area.Controls.Add(serverTop);
        area.Controls.Add(serverBottom);
        // With Advanced open, the space under Automatic is shared: the pinned list keeps half and Advanced
        // scrolls inside the other half once its options no longer fit.
        area.Layout += (_, _) =>
        {
            if (advanced is null) return;
            var fixedBottom = serverBottom.Height - advanced.Height + advanced.HeaderHeight;
            var spare = area.ClientSize.Height - serverTop.Height - fixedBottom;
            advanced.MaxBodyHeight = Math.Max(100 * area.DeviceDpi / 96, spare / 2);
        };
        // Docking runs from the back of the z-order, so the filling list goes in front to take the space left over.
        serverList.BringToFront();
        page.Controls.Add(area, 0, 2);
        return page;
    }

    private Control BuildAdvanced()
    {
        var body = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 4, Margin = new Padding(0), BackColor = Surface, Padding = new Padding(20, 14, 20, 16),
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 50), new ColumnStyle(SizeType.Percent, 50) },
            RowStyles = { new RowStyle(SizeType.Absolute, 66), new RowStyle(SizeType.Absolute, 36),
                new RowStyle(SizeType.Absolute, 36), new RowStyle(SizeType.Absolute, 66) }
        };
        foreach (var check in new[] { lan, guardIpv6, tunnelDns, ipv6 }) check.Margin = new Padding(0, 0, 12, 0);
        body.Controls.Add(LabeledField(T("Protocol", "Protocolo", "Протокол"), protocolMode, new Padding(0, 0, 12, 0)), 0, 0);
        body.Controls.Add(LabeledField(T("Always via VPN (IPs or ranges)", "Sempre pela VPN (IPs ou faixas)", "Всегда через VPN (IP или сети)"),
            InputFrame(include), new Padding(12, 0, 0, 0)), 1, 0);
        body.Controls.Add(lan, 0, 1);
        body.Controls.Add(guardIpv6, 1, 1);
        body.Controls.Add(tunnelDns, 0, 2);
        body.Controls.Add(ipv6, 1, 2);
        body.Controls.Add(LabeledField(T("Never via VPN (IPs or ranges)", "Nunca pela VPN (IPs ou faixas)", "Никогда через VPN (IP или сети)"),
            InputFrame(exclude), new Padding(0, 0, 12, 0)), 0, 3);
        include.AccessibleName = T("Always via VPN", "Sempre pela VPN", "Всегда через VPN");
        exclude.AccessibleName = T("Never via VPN", "Nunca pela VPN", "Никогда через VPN");
        include.AccessibleDescription = T("Optional. Enter IP addresses or CIDR ranges separated by commas.", "Opcional. Use endereços IP ou redes CIDR separados por vírgulas.", "Необязательно. Укажите IP-адреса или сети CIDR через запятую.");
        exclude.AccessibleDescription = include.AccessibleDescription;
        ipv6.AccessibleDescription = T("Available if the selected profile supports IPv6.", "Disponível quando o perfil selecionado inclui IPv6.", "Доступно, если выбранный профиль поддерживает IPv6.");
        protocolMode.AccessibleName = T("VPN protocol", "Protocolo VPN", "Протокол VPN");
        foreach (var protocol in Enum.GetValues<Core.ProtocolPreference>())
        {
            protocolMode.Items.Add(Core.VpnProtocols.Name(protocol));
            protocolMode.Glyphs.Add(protocol switch
            {
                Core.ProtocolPreference.Profile => Glyph.File,
                Core.ProtocolPreference.Automatic => Glyph.Sparkle,
                Core.ProtocolPreference.WireGuardPadding => Glyph.Layers,
                Core.ProtocolPreference.AmneziaWG => Glyph.Shield,
                _ => Glyph.Tunnel
            });
        }

        // The options scroll inside the card when the window leaves too little room for all of them.
        var scroller = new Panel { AutoScroll = true, BackColor = Surface, Margin = new Padding(0) };
        scroller.Controls.Add(body);
        void FitBody()
        {
            // The width leaves room for the vertical bar, so the options never need a horizontal one.
            var height = (14 + 66 + 36 + 36 + 66 + 16) * scroller.DeviceDpi / 96;
            var bar = height > scroller.Height ? SystemInformation.GetVerticalScrollBarWidthForDpi(scroller.DeviceDpi) : 0;
            body.Location = new Point(0, scroller.AutoScrollPosition.Y);
            body.Size = new Size(Math.Max(0, scroller.Width - bar), height);
        }
        scroller.Resize += (_, _) => FitBody();
        scroller.HandleCreated += (_, _) => FitBody();
        advanced = new ExpanderCard(new ExpanderHeader
        {
            Text = T("Advanced", "Avançado", "Дополнительно"),
            Detail = T("protocol, DNS, IPv6, IP filters", "protocolo, DNS, IPv6, filtros de IP", "протокол, DNS, IPv6, IP-фильтры")
        }, scroller, bodyHeight: 14 + 66 + 36 + 36 + 66 + 16);
        return advanced;
    }

    private static Control LabeledField(string name, Control field, Padding margin)
    {
        var content = Column(24, 40);
        content.BackColor = Surface;
        content.Margin = margin;
        content.Controls.Add(MakeLabel(name, muted: true), 0, 0);
        field.Dock = DockStyle.Fill;
        content.Controls.Add(field, 0, 1);
        return content;
    }

    private Control BuildDiagnosticsPage()
    {
        var page = Column(84, 14, 50, 46, 32, 30, 0, 46);
        page.BackColor = Background;
        page.Controls.Add(new PageHeader(T("Diagnostics", "Diagnóstico", "Диагностика"),
            T("WireSock engine, connection health and logs.", "Motor WireSock, saúde da conexão e registros.",
                "Движок WireSock, состояние подключения и журналы.")) { Dock = DockStyle.Fill }, 0, 0);
        page.Controls.Add(engineInfo, 0, 2);
        page.Controls.Add(MakeBar(
            ActionButton(T("Locate WireSock…", "Localizar WireSock…", "Найти WireSock…"), ChooseEngine),
            ActionButton(T("Detect again", "Detectar novamente", "Повторить поиск"), () => Attempt(UpdateEngineInfo)),
            ActionButton(T("Copy install command", "Copiar comando de instalação", "Копировать команду установки"), () => Copy(InstallCommand)),
            ActionButton(T("Copy Proton guide", "Copiar guia da Proton", "Копировать ссылку Proton"), () => Copy(ProtonUrl))), 0, 3);
        page.Controls.Add(diagnostics, 0, 4);
        page.Controls.Add(healthDetails, 0, 5);
        log.ReadOnly = true;
        log.WordWrap = false;
        log.ScrollBars = ScrollBars.Both;
        log.Font = Mono;
        log.AccessibleName = T("WireSock logs", "Registros do WireSock", "Журналы WireSock");
        var shell = new ThemeCard(Field) { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 8, 8), Margin = new Padding(0), Radius = 12 };
        shell.Controls.Add(log);
        page.Controls.Add(shell, 0, 6);
        var logActions = MakeBar(
            ActionButton(T("Copy logs", "Copiar registros", "Копировать журналы"), () => Copy(log.Text), ButtonKind.Quiet),
            ActionButton(T("Clear logs", "Limpar registros", "Очистить журналы"), ClearLog, ButtonKind.Quiet));
        logActions.Padding = new Padding(0, 4, 0, 0);
        page.Controls.Add(logActions, 0, 7);
        return page;
    }

    private ThemeButton NavigationButton(string label, Glyph glyph, int index)
    {
        var button = ActionButton(label, () => OpenPage(index), ButtonKind.Navigation);
        button.Glyph = glyph;
        button.Font = Body();
        button.AutoSize = false;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 2, 0, 2);
        button.Padding = new Padding(12, 0, 8, 0);
        return button;
    }

    private void OpenPage(int index)
    {
        if (index < 0 || index >= pages.Count) return;
        pageHost.SuspendLayout();
        try
        {
            for (var i = 0; i < pages.Count; i++) pages[i].Visible = i == index;
            CurrentPage = index;
            if (index == ServersPage) _ = RefreshLatencyQuietly();
            for (var i = 0; i < navigationButtons.Length; i++) navigationButtons[i].Selected = i == index;
        }
        finally
        {
            pageHost.ResumeLayout(performLayout: true);
        }
    }

    private ThemeButton ActionButton(string label, Action action, ButtonKind kind = ButtonKind.Secondary)
    {
        var button = MakeButton(label, kind);
        button.Click += (_, _) => action();
        return button;
    }

    private static TableLayoutPanel MakePage() => new()
    {
        Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Background, ColumnCount = 1, RowCount = 1
    };

    private static ThemeMenu MakeMenu() => new();

    private static Control InputFrame(TextBox textBox)
    {
        var frame = new ThemeCard(Field)
        {
            Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 6), Margin = new Padding(0), Edge = InputBorder, Radius = 8
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

    private static Label MakeLabel(string caption, bool muted = false) => new SmoothLabel
    {
        Text = caption, Dock = DockStyle.Fill, AutoEllipsis = true, Font = Body(),
        ForeColor = muted ? Muted : DesktopTheme.Text, TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0)
    };

    private static Label SectionTitle(string caption)
    {
        var label = MakeLabel(caption);
        label.Font = Heading(13.5f);
        return label;
    }

    private static ThemeButton MakeButton(string caption, ButtonKind kind = ButtonKind.Secondary) =>
        new() { Text = caption, Kind = kind };

    private static CheckBox MakeCheck(string caption) => new ThemeCheckBox
    {
        Text = caption, Dock = DockStyle.Fill, Font = Body(), FlatStyle = FlatStyle.Flat,
        ForeColor = DesktopTheme.Text, Margin = new Padding(0), Cursor = Cursors.Hand
    };

    private static TextBox MakeText(bool multiline = false, string placeholder = "") => WithEditMenu(new TextBox
    {
        BackColor = Field, ForeColor = DesktopTheme.Text, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
        Font = Body(), Multiline = multiline, PlaceholderText = placeholder, MaxLength = 16000, Margin = new Padding(0)
    });

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBar(Handle);
    }
}

// The sidebar background with its divider on the right edge.
internal sealed class SidebarPanel : Panel
{
    public SidebarPanel()
    {
        BackColor = Surface;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Border, Math.Max(1, DeviceDpi / 96));
        e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}

internal sealed class BrandMark : Control
{
    public BrandMark()
    {
        Text = "AppTunnel";
        AccessibleRole = AccessibleRole.StaticText;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var canvas = new SolidBrush(Parent?.BackColor ?? Surface);
        e.Graphics.FillRectangle(canvas, ClientRectangle);
        var s = DpiScale(this);
        var side = (int)(40 * s);
        var mark = new Rectangle((int)(8 * s), (Height - side) / 2, side, side);
        PaintRounded(e.Graphics, mark, 14 * s, Accent);
        PaintIcon(e.Graphics, Glyph.Intersect, Rectangle.Inflate(mark, -(int)(3 * s), -(int)(3 * s)), AccentInk);
        PaintText(e.Graphics, Text, Heading(13.5f), new Rectangle(mark.Right + (int)(10 * s), mark.Y - (int)(4 * s),
            Width - mark.Right - (int)(10 * s), side + (int)(8 * s)), DesktopTheme.Text);
    }
}

// A card with a clickable header that shows or hides the body below it. Heights follow the monitor DPI.
internal sealed class ExpanderCard : ThemeCard
{
    private readonly ExpanderHeader header;
    private readonly Control body;
    private readonly int bodyHeight;
    public event Action? Toggled;

    public ExpanderCard(ExpanderHeader header, Control body, int bodyHeight) : base(Surface)
    {
        this.header = header;
        this.body = body;
        this.bodyHeight = bodyHeight;
        Radius = 14;
        header.Click += (_, _) => Expanded = !Expanded;
        Controls.Add(header);
        Controls.Add(body);
        body.Visible = false;
        Height = 56;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Expanded
    {
        get => header.Expanded;
        set
        {
            if (header.Expanded == value && body.Visible == value) return;
            header.Expanded = value;
            body.Visible = value;
            FitHeight();
            Toggled?.Invoke();
        }
    }

    private int Px(int logical) => logical * DeviceDpi / 96;

    private int maxBodyHeight;

    public int HeaderHeight => Px(56) + Px(1);

    // Caps the open body in pixels (0 = no cap); a body taller than the cap scrolls inside it.
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int MaxBodyHeight
    {
        get => maxBodyHeight;
        set
        {
            if (maxBodyHeight == value) return;
            maxBodyHeight = value;
            FitHeight();
        }
    }

    private void FitHeight()
    {
        var full = Px(bodyHeight);
        var shown = maxBodyHeight > 0 ? Math.Min(full, maxBodyHeight) : full;
        Height = Px(56) + (Expanded ? Px(1) + shown : 0);
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Height);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FitHeight();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        FitHeight();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        var inset = Math.Max(1, DeviceDpi / 96);
        header.SetBounds(inset, inset, Width - inset * 2, Px(56) - inset);
        body.SetBounds(inset, Px(56) + Px(1), Width - inset * 2, Math.Max(0, Height - Px(56) - Px(1) - inset));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!Expanded) return;
        using var pen = new Pen(Border, Math.Max(1, DeviceDpi / 96));
        e.Graphics.DrawLine(pen, 0, Px(56), Width, Px(56));
    }
}
