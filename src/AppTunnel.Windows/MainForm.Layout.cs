using static AppTunnel.Windows.DesktopTheme;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private void BuildLayout()
    {
        SuspendLayout();
        var frame = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0),
            ColumnStyles = {
                new ColumnStyle(SizeType.Absolute, 176), new ColumnStyle(SizeType.Absolute, 1),
                new ColumnStyle(SizeType.Percent, 100)
            },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        frame.Controls.Add(BuildSidebar(), 0, 0);
        frame.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Border, Margin = new Padding(0) }, 1, 0);

        var workspace = Column(140, 14, 0, 33);
        workspace.Padding = new Padding(20, 18, 20, 6);
        workspace.Controls.Add(BuildConnectionCard(), 0, 0);
        pages.AddRange([BuildProgramsPage(), BuildRoutesPage(), BuildDiagnosticsPage(), BuildSettingsPage()]);
        workspace.Controls.Add(pageHost, 0, 2);
        footer.ForeColor = Muted;
        workspace.Controls.Add(footer, 0, 3);
        frame.Controls.Add(workspace, 2, 0);
        Controls.Add(frame);
        OpenPage(settings.Profiles.Count == 0 ? 1 : 0);
        RenderProtonAccount();
        WireActions();
        ResumeLayout();
    }

    private Control BuildSidebar()
    {
        var sidebar = Column(62, 42, 42, 42, 42, 0, 42);
        sidebar.BackColor = Surface;
        sidebar.Padding = new Padding(11, 17, 11, 9);
        var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        var mark = Icon?.ToBitmap();
        var picture = new PictureBox
        {
            Size = new Size(31, 31), Image = mark, SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(1, 6, 4, 0), TabStop = false
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
            NavigationButton(T("Apps", "Programas", "Приложения"), 0),
            NavigationButton(T("Routes", "Rotas", "Маршруты"), 1),
            NavigationButton(T("Diagnostics", "Diagnóstico", "Диагностика"), 2),
            NavigationButton(T("Settings", "Configurações", "Настройки"), 3)
        ];
        for (var i = 0; i < navigationButtons.Length; i++)
            sidebar.Controls.Add(navigationButtons[i], 0, i + 1);

        sidebar.Controls.Add(BuildLanguageSelector(), 0, 6);
        return sidebar;
    }

    private Control BuildConnectionCard()
    {
        var card = new ThemeCard(Raised)
        {
            Dock = DockStyle.Fill, Padding = new Padding(19, 15, 19, 13), Margin = new Padding(0)
        };
        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0),
            BackColor = Raised,
            ColumnStyles = {
                new ColumnStyle(SizeType.Percent, 53), new ColumnStyle(SizeType.Absolute, 1),
                new ColumnStyle(SizeType.Percent, 47)
            },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };

        var current = Column(34, 44, 28);
        current.Margin = new Padding(0, 0, 15, 0);
        current.Controls.Add(pill, 0, 0);
        liveTitle.Font = Heading(26);
        liveTitle.AccessibleName = T("Server in use", "Servidor em uso", "Текущий сервер");
        current.Controls.Add(liveTitle, 0, 1);
        liveDetails.Font = Mono;
        liveDetails.AccessibleName = T("Active server endpoint", "Endpoint do servidor em uso", "Адрес текущего сервера");
        current.Controls.Add(liveDetails, 0, 2);
        columns.Controls.Add(current, 0, 0);
        columns.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Border, Margin = new Padding(0, 4, 0, 4) }, 1, 0);

        var next = Column(21, 28, 23, 0);
        next.Margin = new Padding(18, 0, 0, 0);
        routeCaption.Font = Body(9, bold: true);
        routeTitle.Font = Body(14, bold: true);
        routeDetails.Font = Body(9);
        next.Controls.Add(routeCaption, 0, 0);
        next.Controls.Add(routeTitle, 0, 1);
        next.Controls.Add(routeDetails, 0, 2);
        var actions = MakeBar(start, apply, stop);
        actions.AccessibleName = T("Connection controls", "Controles da conexão", "Управление подключением");
        next.Controls.Add(actions, 0, 3);
        columns.Controls.Add(next, 2, 0);
        card.Controls.Add(columns);
        return card;
    }

    private Control BuildProgramsPage()
    {
        var page = MakePage();
        var content = Column(32, 24, 43, 0, 42);
        content.Controls.Add(SectionTitle(T("Apps on the VPN", "Programas na VPN", "Приложения через VPN")), 0, 0);
        content.Controls.Add(appCount, 0, 1);
        content.Controls.Add(MakeBar(
            ActionButton(T("Add .exe", "Adicionar .exe", "Добавить .exe"), AddExecutable),
            ActionButton(T("Add folder", "Adicionar pasta", "Добавить папку"), AddFolder),
            ActionButton(T("Running apps…", "Em execução…", "Запущенные…"), AddRunning),
            ActionButton(T("Explore Common Apps", "Explorar aplicativos comuns", "Часто используемые приложения"),
                async () => await ExploreCommonApps())), 0, 2);

        apps.AccessibleName = T("Apps on the VPN", "Programas na VPN", "Приложения через VPN");
        AddCheckColumn(apps);
        AddColumn(apps, T("App", "Programa", "Приложение"), 200, font: Body(bold: true));
        AddColumn(apps, T("Rule", "Regra", "Правило"), 91);
        AddColumn(apps, T("Executable or folder path", "Caminho do executável ou pasta", "Путь к исполняемому файлу или папке"), 220, fill: true, font: Mono);
        content.Controls.Add(GridFrame(apps, emptyApps, T("No apps added. Explore common apps or add an executable.", "Nenhum programa adicionado. Explore os aplicativos comuns ou adicione um executável.", "Добавьте приложение из каталога или выберите исполняемый файл.")), 0, 3);
        content.Controls.Add(MakeBar(
            ActionButton(T("Select all", "Marcar todos", "Выбрать всё"), () => SetChecks(true), ButtonKind.Quiet),
            ActionButton(T("Clear all", "Desmarcar todos", "Снять всё"), () => SetChecks(false), ButtonKind.Quiet),
            ActionButton(T("Remove selected", "Remover selecionados", "Удалить выбранное"), RemoveApps, ButtonKind.Quiet)), 0, 4);
        page.Controls.Add(content);
        return page;
    }

    private Control BuildRoutesPage()
    {
        var page = MakePage();
        var content = Column(34, 42, 36, 42, 37, 27, 0, 13, 145);
        content.Controls.Add(SectionTitle(T("VPN routes", "Rotas VPN", "Маршруты VPN")), 0, 0);
        content.Controls.Add(MakeBar(
            ActionButton(T("Account profiles", "Perfis da conta", "Профили учётной записи"), ShowProtonProfiles, ButtonKind.Primary),
            ActionButton(T("New WireGuard…", "Novo WireGuard…", "Новый профиль WireGuard…"), () => OpenProton(create: true)),
            ActionButton(T("Import .conf", "Importar .conf", "Импорт .conf"), ImportProfiles)), 0, 1);

        var accountRow = Pair(65);
        accountRow.Controls.Add(protonAccount, 0, 0);
        var accountLink = MakeBar(ActionButton(T("Open Proton account", "Abrir conta Proton", "Открыть учётную запись Proton"), () => OpenProton(), ButtonKind.Quiet));
        accountLink.FlowDirection = FlowDirection.RightToLeft;
        accountRow.Controls.Add(accountLink, 1, 0);
        content.Controls.Add(accountRow, 0, 2);
        content.Controls.Add(MakeBar(fastest, rename, remove), 0, 3);
        content.Controls.Add(BuildProtocolControl(), 0, 4);
        content.Controls.Add(MakeLabel(T(
            "For automatic switching, add two routes with different servers. One is enough to connect.",
            "Para troca automática, adicione duas rotas em servidores diferentes. Uma já permite conectar.",
            "Для автопереключения добавьте два маршрута с разными серверами. Для подключения достаточно одного."), muted: true), 0, 5);

        routes.AccessibleName = T("Server profiles", "Perfis de servidor", "Профили серверов");
        routes.MultiSelect = false;
        AddColumn(routes, T("Profile", "Perfil", "Профиль"), 126, fill: true, font: Body(bold: true));
        AddColumn(routes, T("Server address", "Endereço do servidor", "Адрес сервера"), 190, font: Mono);
        AddColumn(routes, "IPv6", 83);
        AddColumn(routes, T("Latency", "Latência", "Задержка"), 90);
        AddColumn(routes, T("Protocol", "Protocolo", "Протокол"), 112);
        AddColumn(routes, T("Session", "Sessão", "Сеанс"), 101);
        content.Controls.Add(GridFrame(routes, emptyRoutes, T("Add a Proton WireGuard route or import an existing profile.",
            "Crie uma rota WireGuard na Proton ou importe um perfil existente.", "Создайте профиль WireGuard в Proton или импортируйте имеющийся.")), 0, 6);
        content.Controls.Add(BuildDestinationFilters(), 0, 8);
        routesContent = content;
        page.Controls.Add(content);
        return page;
    }

    private TableLayoutPanel? routesContent;
    private ThemeButton? filtersToggle;
    private readonly List<Control> filterBody = [];
    private bool filtersExpanded = true;
    private const int RoutesTitleRow = 34, FiltersExpandedRow = 145, FiltersCollapsedRow = 34;

    // Collapsing the advanced filters gives the server list most of the Routes page.
    private void SetFiltersExpanded(bool expanded)
    {
        filtersExpanded = expanded;
        foreach (var control in filterBody) control.Visible = expanded;
        if (filtersToggle is not null)
        {
            var caption = T("Destination filters", "Filtros de destino", "Фильтры адресов назначения");
            // Plain text state instead of arrow glyphs, which the bundled UI fonts may not contain.
            filtersToggle.Text = caption + " · " + (expanded ? T("Hide", "Ocultar", "Скрыть") : T("Show", "Mostrar", "Показать"));
            filtersToggle.AccessibleName = caption;
            filtersToggle.AccessibleDescription = expanded ? T("Expanded", "Expandido", "Развёрнуто") : T("Collapsed", "Recolhido", "Свёрнуто");
        }
        if (routesContent is null) return;
        // Row heights are in device pixels once scaled; derive the factor from the fixed title row.
        var factor = routesContent.RowStyles[0].Height / RoutesTitleRow;
        routesContent.RowStyles[8].Height = (expanded ? FiltersExpandedRow : FiltersCollapsedRow) * factor;
        routesContent.PerformLayout();
    }

    private Control BuildDestinationFilters()
    {
        var filter = Column(27, 29, 29, 0);
        filtersToggle = MakeButton(T("Destination filters", "Filtros de destino", "Фильтры адресов назначения"), ButtonKind.Quiet);
        filtersToggle.Font = Body(11, bold: true);
        filtersToggle.Margin = new Padding(0);
        filtersToggle.Padding = new Padding(4, 0, 8, 0);
        filtersToggle.MinimumSize = new Size(0, 27);
        filtersToggle.Click += (_, _) => SetFiltersExpanded(!filtersExpanded);
        filter.Controls.Add(filtersToggle, 0, 0);

        var options = Pair(50);
        options.Controls.Add(lan, 0, 0);
        options.Controls.Add(ipv6, 1, 0);
        filter.Controls.Add(options, 0, 1);
        filterBody.Add(options);
        var privacy = Pair(50);
        privacy.Controls.Add(tunnelDns, 0, 0);
        privacy.Controls.Add(guardIpv6, 1, 0);
        filter.Controls.Add(privacy, 0, 2);
        filterBody.Add(privacy);
        var fields = Pair(50);
        fields.Controls.Add(LabeledInput(T("Via VPN", "Pela VPN", "Через VPN"), include, new Padding(0, 0, 7, 0)), 0, 0);
        fields.Controls.Add(LabeledInput(T("Via direct connection", "Pela conexão direta", "Через прямое подключение"), exclude, new Padding(7, 0, 0, 0)), 1, 0);
        filter.Controls.Add(fields, 0, 3);
        filterBody.Add(fields);
        include.AccessibleDescription = T("Optional. Enter IP addresses or CIDR ranges separated by commas.", "Opcional. Use endereços IP ou redes CIDR separados por vírgulas.", "Необязательно. Укажите IP-адреса или сети CIDR через запятую.");
        exclude.AccessibleDescription = include.AccessibleDescription;
        ipv6.AccessibleDescription = T("Available if the selected profile supports IPv6.", "Disponível quando o perfil selecionado inclui IPv6.", "Доступно, если выбранный профиль поддерживает IPv6.");
        return filter;
    }

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
            if (index == 1) _ = RefreshLatencyQuietly();
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
