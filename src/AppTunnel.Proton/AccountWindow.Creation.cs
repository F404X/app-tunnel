using System.Runtime.InteropServices;
using System.Text.Json;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Proton;

internal sealed partial class AccountWindow
{
    private static readonly string CreationScript = LoadScript("create-wireguard.js");
    private readonly TextBox profileName = new() { Text = "AppTunnel", MaxLength = 100, Width = 320 };
    private readonly Button recommended = new();
    private readonly Button chooseServer = new();
    private readonly Label creationStatus = new() { Dock = DockStyle.Fill };
    private TableLayoutPanel creationPanel = null!;
    private (bool Ready, bool CanCreate, bool CanChooseServer, bool LoadingMore) creationForm;
    private IReadOnlyList<ProtonAccountProfile> accountProfiles = [];
    private bool preparing;
    private (string Name, long Deadline)? pendingCreation;
    private string? creationFeedback;

    private Control BuildCreationPanel(bool dark)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 4, Dock = DockStyle.Top,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false,
            Padding = new Padding(16, 7, 16, 5), Margin = new Padding(0),
            BackColor = dark ? Color.FromArgb(26, 34, 51) : Color.FromArgb(229, 235, 244)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 29, 36, 42, 49 })
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        var heading = new Label
        {
            Text = T("New WireGuard profile", "Novo perfil WireGuard", "Новый профиль WireGuard"),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 12, FontStyle.Bold)
        };
        panel.Controls.Add(heading, 0, 0);

        var nameLabel = new Label
        {
            Text = T("&Profile name", "&Nome do perfil", "&Имя профиля"), AutoSize = true,
            Margin = new Padding(0, 6, 10, 0)
        };
        profileName.AccessibleName = nameLabel.Text.Replace("&", "");
        profileName.BackColor = dark ? Color.FromArgb(17, 24, 39) : Color.White;
        profileName.ForeColor = ForeColor;
        var nameRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
        nameRow.Controls.AddRange([nameLabel, profileName]);
        panel.Controls.Add(nameRow, 0, 1);

        recommended.Text = T("Create with recommended server", "Criar com servidor recomendado", "Создать с рекомендованным сервером");
        chooseServer.Text = T("Choose another server…", "Escolher outro servidor…", "Выбрать другой сервер…");
        var closeGuide = new Button { Text = T("Close guide", "Fechar guia", "Закрыть мастер") };
        foreach (var button in new[] { recommended, chooseServer, closeGuide })
        {
            button.AutoSize = true;
            button.Height = 33;
            button.FlatStyle = FlatStyle.Flat;
            button.Padding = new Padding(7, 0, 7, 0);
            button.Margin = new Padding(0, 2, 7, 0);
            button.FlatAppearance.BorderColor = dark ? Color.FromArgb(72, 87, 113) : Color.FromArgb(178, 191, 210);
        }
        recommended.BackColor = dark ? Color.FromArgb(62, 80, 119) : Color.FromArgb(197, 216, 250);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
        actions.Controls.AddRange([recommended, chooseServer, closeGuide]);
        panel.Controls.Add(actions, 0, 2);
        creationStatus.Margin = new Padding(0, 3, 0, 0);
        panel.Controls.Add(creationStatus, 0, 3);
        recommended.Click += async (_, _) => await CreateProfileAsync(useRecommended: true);
        chooseServer.Click += async (_, _) => await CreateProfileAsync(useRecommended: false);
        closeGuide.Click += (_, _) => panel.Hide();
        creationPanel = panel;
        RefreshCreationButtons();
        return panel;
    }

    private void ShowCreationPanel()
    {
        if (creationPanel.Visible) return;
        creationPanel.Show();
        if (pendingCreation is null && !preparing)
        {
            creationFeedback = null;
            profileName.Focus();
            profileName.SelectAll();
        }
        RefreshCreationButtons();
    }

    private void RefreshCreationButtons()
    {
        var waiting = preparing || pendingCreation is not null;
        profileName.Enabled = !waiting;
        recommended.Enabled = !waiting && creationForm.CanCreate && !creationForm.LoadingMore;
        chooseServer.Enabled = !waiting && creationForm.CanChooseServer && !creationForm.LoadingMore;
        if (!creationPanel.Visible) return;
        var message = creationFeedback ?? CreationHint();
        if (creationStatus.Text != message) creationStatus.Text = message;
    }

    private string CreationHint()
    {
        if (!creationForm.Ready)
            return T("Sign in below. Then give the profile a name and choose a server.",
                "Entre na Proton abaixo. Depois, dê um nome ao perfil e escolha um servidor.",
                "Войдите в Proton ниже. Затем назовите профиль и выберите сервер.");
        if (creationForm.LoadingMore)
            return T("Checking your account for existing profiles…",
                "Consultando os perfis da sua conta…", "Проверяем существующие профили…");
        if (!creationForm.CanChooseServer)
            return T("If the guided options stay unavailable, use the Proton form below.",
                "Se as opções do assistente continuarem indisponíveis, use o formulário da Proton abaixo.",
                "Если мастер остаётся недоступен, воспользуйтесь формой Proton ниже.");
        return T("Each creation adds one profile to your Proton account. You can reuse profiles from Routes.",
                "Cada criação adiciona um perfil à sua conta Proton. Você pode reaproveitar perfis em Rotas.",
                "Каждое создание добавляет один профиль в Proton. Имеющиеся профили можно выбрать в разделе «Маршруты».");
    }

    private void ShowCreationFeedback(string message)
    {
        creationFeedback = message;
        RefreshCreationButtons();
    }

    private async Task CreateProfileAsync(bool useRecommended)
    {
        if (!(useRecommended ? recommended.Enabled : chooseServer.Enabled) ||
            web is null || !ProtonPortal.IsAccountPage(web.Source)) return;
        var name = profileName.Text.Trim();
        if (name.Length == 0 || name.Any(char.IsControl))
        {
            ShowCreationFeedback(T("Enter a name for the profile.", "Dê um nome ao perfil.", "Введите имя профиля."));
            profileName.Focus();
            return;
        }
        if (accountProfiles.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowCreationFeedback(T("This name is already in your account. Use that profile or choose a different name.",
                "Esse nome já existe na sua conta. Use o perfil existente ou escolha outro nome.",
                "Такое имя уже есть в учётной записи. Используйте существующий профиль или введите другое имя."));
            profileName.Focus();
            return;
        }

        preparing = true;
        RefreshCreationButtons();
        try
        {
            var prepared = await RunCreationScriptAsync("prepare", name);
            if (IsDisposed || Disposing) return;
            if (prepared && useRecommended)
            {
                // Only this click handler can submit. Keep it locked even if the WebView loses the response.
                pendingCreation = (name, Environment.TickCount64 + 60_000);
                ShowCreationFeedback(T("Waiting for Proton. Keep this window open until the profile is saved.",
                    "Aguardando a Proton. Mantenha a janela aberta até o perfil ser salvo.",
                    "Ожидаем Proton. Оставьте это окно открытым, пока профиль не сохранится."));
                if (await RunCreationScriptAsync("submit", name)) return;
                pendingCreation = null;
            }
            else if (prepared && await RunCreationScriptAsync("choose", name) && !IsDisposed && !Disposing)
            {
                ShowCreationFeedback(T("Find a server below and click Create next to it. AppTunnel will save the new profile.",
                    "Encontre um servidor abaixo e clique em Criar ao lado dele. O AppTunnel salvará o perfil.",
                    "Найдите сервер ниже и нажмите «Создать» рядом с ним. AppTunnel сохранит профиль."));
                return;
            }
        }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        {
            if (pendingCreation is not null) return;
        }
        finally
        {
            preparing = false;
            if (!IsDisposed && !Disposing) RefreshCreationButtons();
        }
        if (!IsDisposed && !Disposing)
            ShowCreationFeedback(T("The form changed. You can still create a profile on the Proton page below.",
                "O formulário mudou. Você ainda pode criar o perfil na página da Proton abaixo.",
                "Форма изменилась. Создайте профиль на странице Proton ниже."));
    }

    private async Task<bool> RunCreationScriptAsync(string action, string name)
    {
        if (web is null || !ProtonPortal.IsAccountPage(web.Source)) return false;
        return await web.ExecuteScriptAsync($"{CreationScript}(\"{action}\", {JsonSerializer.Serialize(name)})") switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidOperationException("The Proton page did not return a creation result.")
        };
    }

    private void FinishCreation()
    {
        if (pendingCreation is not { } request || !accountProfiles.Any(profile => profile.State == ProtonProfileState.Available &&
            profile.Name.Equals(request.Name, StringComparison.Ordinal))) return;
        pendingCreation = null;
        ShowCreationFeedback(T("Profile saved. Select it in Routes in the main AppTunnel window.",
            "Perfil salvo. Selecione-o em Rotas, na janela principal do AppTunnel.",
            "Профиль сохранён. Выберите его на вкладке «Маршруты» в главном окне AppTunnel."));
    }

    private void CheckCreationTimeout()
    {
        if (pendingCreation is not { } request || Environment.TickCount64 < request.Deadline) return;
        pendingCreation = null;
        ShowCreationFeedback(T("Saving was not confirmed. Check Account profiles before trying again.",
            "O salvamento não foi confirmado. Confira Perfis da conta antes de tentar novamente.",
            "Сохранение не подтверждено. Проверьте профили учётной записи перед повторной попыткой."));
    }
}
