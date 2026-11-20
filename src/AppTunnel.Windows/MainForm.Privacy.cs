using AppTunnel.Core;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private readonly CheckBox statisticsConsent = MakeCheck(T("Send anonymous statistics", "Enviar estatísticas anônimas", "Отправлять анонимную статистику"));
    private readonly CheckBox errorConsent = MakeCheck(T("Automatic error reports", "Relatórios automáticos de erros", "Автоматические отчёты об ошибках"));
    private readonly CheckBox detailedConsent = MakeCheck(T("Include detailed diagnostics", "Incluir diagnósticos detalhados", "Включать подробную диагностику"));
    private readonly ThemeButton sendDiagnostic = MakeButton(T("Review a diagnostic report", "Revisar relatório de diagnóstico", "Проверить диагностический отчёт"));
    private readonly Label privacyStatus = MakeLabel("", muted: true);
    private bool showingConsent;

    private void AddPrivacyControls(TableLayoutPanel content, int row)
    {
        content.Controls.Add(SectionTitle(T("Privacy and feedback", "Privacidade e feedback", "Конфиденциальность и отчёты")), 0, row++);
        content.Controls.Add(statisticsConsent, 0, row++);
        content.Controls.Add(errorConsent, 0, row++);
        content.Controls.Add(detailedConsent, 0, row++);
        content.Controls.Add(MakeBar(ActionButton(TelemetryWindow.Caption, () => TelemetryWindow.Show(this, telemetry)), sendDiagnostic), 0, row++);
        content.Controls.Add(privacyStatus, 0, row);

        statisticsConsent.CheckedChanged += (_, _) => UpdateConsent();
        errorConsent.CheckedChanged += (_, _) => UpdateConsent();
        detailedConsent.CheckedChanged += (_, _) => UpdateConsent();
        sendDiagnostic.Click += async (_, _) => await ReviewDiagnostic();
        LoadConsent();
        if (!telemetry.Configured)
            privacyStatus.Text = T("This build has no telemetry endpoint.", "Esta compilação está sem um endpoint de telemetria.",
                "В этой сборке не задан адрес отправки отчётов.");
    }

    private void LoadConsent()
    {
        var consent = WindowsTelemetry.Consent;
        showingConsent = true;
        statisticsConsent.Checked = consent.HasFlag(TelemetryConsent.Statistics);
        errorConsent.Checked = consent.HasFlag(TelemetryConsent.Errors);
        detailedConsent.Checked = consent.HasFlag(TelemetryConsent.Diagnostics);
        sendDiagnostic.Enabled = detailedConsent.Checked;
        showingConsent = false;
    }

    private void UpdateConsent()
    {
        if (showingConsent) return;
        var consent = (statisticsConsent.Checked ? TelemetryConsent.Statistics : TelemetryConsent.Off) |
            (errorConsent.Checked ? TelemetryConsent.Errors : TelemetryConsent.Off) |
            (detailedConsent.Checked ? TelemetryConsent.Diagnostics : TelemetryConsent.Off);
        try
        {
            WindowsTelemetry.Consent = consent;
            telemetry.ApplyConsent(consent);
            sendDiagnostic.Enabled = detailedConsent.Checked;
        }
        catch (Exception error) when (Expected(error))
        {
            LoadConsent();
            Failure(error);
        }
    }

    private async Task ReviewDiagnostic()
    {
        var preview = telemetry.PreviewDiagnostics();
        if (preview is null)
        {
            privacyStatus.Text = T("There are no recent diagnostic events.", "Ainda não há eventos recentes para revisar.",
                "Недавних событий диагностики нет.");
            return;
        }
        if (!TelemetryWindow.ConfirmDiagnostic(this, preview)) return;
        if (!telemetry.Configured)
        {
            privacyStatus.Text = T("This build has no telemetry endpoint.", "Esta compilação está sem um endpoint de telemetria.",
                "В этой сборке не задан адрес отправки отчётов.");
            return;
        }
        sendDiagnostic.Enabled = false;
        var accepted = await telemetry.SendApprovedDiagnosticsAsync(preview);
        if (IsDisposed) return;
        sendDiagnostic.Enabled = detailedConsent.Checked;
        privacyStatus.Text = accepted
            ? T("Diagnostic report accepted.", "Relatório de diagnóstico aceito.", "Диагностический отчёт принят.")
            : T("Delivery was not confirmed. See the local history.", "Envio sem confirmação. Consulte o histórico local.",
                "Отправка не подтверждена. Проверьте локальную историю.");
    }
}
