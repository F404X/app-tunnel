using System.Runtime.InteropServices;
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
            NavigationButton(T("Apps", "Programas"), 0),
            NavigationButton(T("Routes", "Rotas"), 1),
            NavigationButton(T("Diagnostics", "Diagnóstico"), 2),
            NavigationButton(T("Settings", "Configurações"), 3)
        ];
        for (var i = 0; i < navigationButtons.Length; i++)
            sidebar.Controls.Add(navigationButtons[i], 0, i + 1);

        var language = MakeButton(T("Language · EN", "Idioma · PT"), ButtonKind.Secondary);
        language.Dock = DockStyle.Fill;
        language.Margin = new Padding(0, 2, 0, 3);
        var menu = new ContextMenuStrip { Font = Body() };
        var english = menu.Items.Add("English", null, (_, _) => BeginInvoke(() => LanguageRequested?.Invoke("en")));
        var portuguese = menu.Items.Add("Português", null, (_, _) => BeginInvoke(() => LanguageRequested?.Invoke("pt")));
        ((ToolStripMenuItem)english).Checked = !Portuguese;
        ((ToolStripMenuItem)portuguese).Checked = Portuguese;
        language.ContextMenuStrip = menu;
        language.Disposed += (_, _) => menu.Dispose();
        language.Click += (_, _) => menu.Show(language, new Point(0, -menu.GetPreferredSize(Size.Empty).Height));
        sidebar.Controls.Add(language, 0, 6);
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

        var current = Column(25, 49, 32);
        current.Margin = new Padding(0, 0, 15, 0);
        current.Controls.Add(pill, 0, 0);
        liveTitle.Font = Heading(26);
        liveTitle.AccessibleName = T("Server in use", "Servidor em uso");
        current.Controls.Add(liveTitle, 0, 1);
        liveDetails.Font = Mono;
        liveDetails.AccessibleName = T("Active server endpoint", "Endpoint do servidor em uso");
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
        actions.AccessibleName = T("Connection controls", "Controles da conexão");
        next.Controls.Add(actions, 0, 3);
        columns.Controls.Add(next, 2, 0);
        card.Controls.Add(columns);
        return card;
    }

    private Control BuildProgramsPage()
    {
        var page = MakePage();
        var content = Column(32, 24, 43, 0, 42);
        content.Controls.Add(SectionTitle(T("Apps on the VPN", "Programas na VPN")), 0, 0);
        content.Controls.Add(appCount, 0, 1);
        content.Controls.Add(MakeBar(
            ActionButton(T("Add .exe", "Adicionar .exe"), AddExecutable),
            ActionButton(T("Add folder", "Adicionar pasta"), AddFolder),
            ActionButton(T("Running apps…", "Em execução…"), AddRunning),
            ActionButton(T("Detect Discord", "Detectar Discord"), AddKnown)), 0, 2);

        apps.AccessibleName = T("Apps on the VPN", "Programas na VPN");
        AddCheckColumn(apps);
        AddColumn(apps, T("App", "Programa"), 200, font: Body(bold: true));
        AddColumn(apps, T("Rule", "Regra"), 91);
        AddColumn(apps, T("Executable or folder path", "Caminho do executável ou pasta"), 220, fill: true, font: Mono);
        content.Controls.Add(GridFrame(apps, emptyApps, T("No apps added. Use Detect Discord or add an executable.", "Nenhum programa adicionado. Use Detectar Discord ou adicione um executável.")), 0, 3);
        content.Controls.Add(MakeBar(
            ActionButton(T("Select all", "Marcar todos"), () => SetChecks(true), ButtonKind.Quiet),
            ActionButton(T("Clear all", "Desmarcar todos"), () => SetChecks(false), ButtonKind.Quiet),
            ActionButton(T("Remove selected", "Remover selecionados"), RemoveApps, ButtonKind.Quiet)), 0, 4);
        page.Controls.Add(content);
        return page;
    }

    private Control BuildRoutesPage()
    {
        var page = MakePage();
        var content = Column(34, 42, 36, 42, 0, 13, 113);
        content.Controls.Add(SectionTitle(T("WireGuard routes", "Rotas WireGuard")), 0, 0);
        content.Controls.Add(MakeBar(
            ActionButton(T("Proton account", "Conta Proton"), () => OpenProton(), ButtonKind.Primary),
            ActionButton(T("New WireGuard", "Novo WireGuard"), () => OpenProton(create: true)),
            ActionButton(T("Import .conf", "Importar .conf"), ImportProfiles)), 0, 1);

        var accountRow = Pair(65);
        accountRow.Controls.Add(protonAccount, 0, 0);
        var copy = MakeBar(ActionButton(T("Account profiles", "Perfis da conta"), ShowProtonProfiles, ButtonKind.Quiet));
        copy.FlowDirection = FlowDirection.RightToLeft;
        accountRow.Controls.Add(copy, 1, 0);
        content.Controls.Add(accountRow, 0, 2);
        content.Controls.Add(MakeBar(ping, fastest, rename, remove), 0, 3);

        routes.AccessibleName = T("Server profiles", "Perfis de servidor");
        routes.MultiSelect = false;
        AddColumn(routes, T("Profile", "Perfil"), 126, fill: true, font: Body(bold: true));
        AddColumn(routes, "Endpoint", 190, font: Mono);
        AddColumn(routes, "IPv6", 83);
        AddColumn(routes, T("Latency", "Latência"), 90);
        AddColumn(routes, T("Session", "Sessão"), 101);
        content.Controls.Add(GridFrame(routes, emptyRoutes, T("Add a Proton WireGuard route or import an existing profile.",
            "Crie uma rota WireGuard na Proton ou importe um perfil existente.")), 0, 4);
        content.Controls.Add(BuildDestinationFilters(), 0, 6);
        page.Controls.Add(content);
        return page;
    }

    private Control BuildDestinationFilters()
    {
        var filter = Column(27, 29, 0);
        var title = SectionTitle(T("Destination filters", "Filtros de destino"));
        title.Font = Body(11, bold: true);
        filter.Controls.Add(title, 0, 0);

        var options = Pair(50);
        options.Controls.Add(lan, 0, 0);
        options.Controls.Add(ipv6, 1, 0);
        filter.Controls.Add(options, 0, 1);
        var fields = Pair(50);
        fields.Controls.Add(LabeledInput(T("Via VPN", "Pela VPN"), include, new Padding(0, 0, 7, 0)), 0, 0);
        fields.Controls.Add(LabeledInput(T("Via direct connection", "Pela conexão direta"), exclude, new Padding(7, 0, 0, 0)), 1, 0);
        filter.Controls.Add(fields, 0, 2);
        include.AccessibleDescription = T("Optional. Enter IP addresses or CIDR ranges separated by commas.", "Opcional. Use endereços IP ou redes CIDR separados por vírgulas.");
        exclude.AccessibleDescription = include.AccessibleDescription;
        ipv6.AccessibleDescription = T("Available if the selected profile supports IPv6.", "Disponível quando o perfil selecionado inclui IPv6.");
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
        var content = Column(32, 51, 41, 29, 0, 42);
        content.Controls.Add(SectionTitle(T("WireSock and logs", "WireSock e registros")), 0, 0);
        content.Controls.Add(engineInfo, 0, 1);
        content.Controls.Add(MakeBar(
            ActionButton(T("Locate WireSock…", "Localizar WireSock…"), ChooseEngine),
            ActionButton(T("Detect again", "Detectar novamente"), () => Attempt(UpdateEngineInfo)),
            ActionButton(T("Copy command", "Copiar comando"), () => Copy(InstallCommand)),
            ActionButton(T("Copy Proton guide", "Copiar guia do Proton"), () => Copy(ProtonUrl))), 0, 2);
        content.Controls.Add(diagnostics, 0, 3);
        log.ReadOnly = true;
        log.WordWrap = false;
        log.ScrollBars = ScrollBars.Both;
        log.Font = Mono;
        log.AccessibleName = T("WireSock logs", "Registros do WireSock");
        var shell = new ThemeCard(Field)
        {
            Dock = DockStyle.Fill, Padding = new Padding(12, 9, 10, 9), Margin = new Padding(0)
        };
        shell.Controls.Add(log);
        content.Controls.Add(shell, 0, 4);
        content.Controls.Add(MakeBar(
            ActionButton(T("Copy logs", "Copiar registros"), () => Copy(log.Text), ButtonKind.Quiet),
            ActionButton(T("Clear logs", "Limpar registros"), ClearLog, ButtonKind.Quiet)), 0, 5);
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
            Dock = DockStyle.Fill, Padding = new Padding(9, 6, 9, 4), Margin = new Padding(0)
        };
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
        var enabled = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
