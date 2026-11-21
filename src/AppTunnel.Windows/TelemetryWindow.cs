using System.Reflection;
using AppTunnel.Core;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal static class TelemetryWindow
{
    public static string Caption => T("View data sent", "Visualizar dados enviados", "Просмотреть отправленные данные");

    public static void Show(IWin32Window? owner, Telemetry telemetry, bool allowChanges = true)
    {
        using var dialog = new Form
        {
            Text = Caption, ClientSize = new Size(760, 530), MinimumSize = new Size(560, 360),
            BackColor = Surface, ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(14),
            StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent
        };
        PrepareDialog(dialog);
        var summary = new Label
        {
            Dock = DockStyle.Top, Height = 50, ForeColor = Muted,
            Text = T("Examples below describe the complete schema. History shows the exact UTF-8 JSON body of each attempt.",
                "Os exemplos mostram o esquema completo. O histórico exibe o JSON UTF-8 exato de cada tentativa.",
                "Примеры описывают полную схему. В истории показывается точное тело JSON каждой попытки.")
        };
        var body = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = Field, ForeColor = DesktopTheme.Text,
            Font = Mono, ScrollBars = ScrollBars.Both, WordWrap = false, AccessibleName = Caption,
            BorderStyle = BorderStyle.FixedSingle
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 8, 0, 0)
        };
        void Render()
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppTunnel.TelemetryExamples")!;
            using var reader = new StreamReader(resource);
            var items = new List<string>
            {
                T("Schema examples (sample data)", "Exemplos do esquema (dados fictícios)", "Примеры схемы (вымышленные данные)"),
                reader.ReadToEnd()
            };
            if (telemetry.PreviewStatistics() is { } statistics)
            {
                items.Add(T("Next statistics body", "Próximo envio de estatísticas", "Тело следующей отправки статистики"));
                items.Add(statistics);
            }
            if (telemetry.PreviewDiagnostics() is { } diagnostics)
            {
                items.Add(T("Diagnostic preview", "Prévia dos diagnósticos", "Предпросмотр диагностики"));
                items.Add(diagnostics.Json);
            }
            items.Add(T("Local history", "Histórico local", "Локальная история"));
            foreach (var entry in telemetry.GetHistory())
            {
                var status = entry.Status == Delivery.Delivered
                    ? T("Accepted", "Aceito", "Принято")
                    : T("Delivery unconfirmed", "Envio sem confirmação", "Доставка не подтверждена");
                var timestamp = entry.At.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentUICulture);
                items.Add($"{timestamp} · {status}{Environment.NewLine}{entry.Json}");
            }
            body.Text = string.Join(Environment.NewLine + Environment.NewLine, items);
        }
        var clear = new ThemeButton { Text = T("Clear local data", "Apagar dados locais", "Удалить локальные данные") };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(dialog, T("Delete the local history and unsent data?", "Apagar o histórico local e os dados ainda não enviados?",
                "Удалить локальную историю и неотправленные данные?"), Caption, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (telemetry.ClearLocalData()) Render();
        };
        var close = new ThemeButton { Text = T("Close", "Fechar", "Закрыть"), DialogResult = DialogResult.Cancel };
        if (allowChanges) actions.Controls.Add(clear);
        actions.Controls.Add(close);
        dialog.Controls.Add(body);
        dialog.Controls.Add(actions);
        dialog.Controls.Add(summary);
        dialog.CancelButton = close;
        Render();
        // Read-only text should open unselected, with the caret at the top.
        dialog.Shown += (_, _) => body.Select(0, 0);
        dialog.ShowDialog(owner);
    }

    public static bool ConfirmDiagnostic(IWin32Window owner, TelemetryPreview preview)
    {
        using var dialog = new Form
        {
            Text = T("Review diagnostics", "Revisar diagnósticos", "Проверить диагностику"),
            ClientSize = new Size(675, 292), MinimumSize = new Size(530, 245),
            BackColor = Surface, ForeColor = DesktopTheme.Text, Font = Body(), Padding = new Padding(14),
            StartPosition = FormStartPosition.CenterParent
        };
        PrepareDialog(dialog);
        var explanation = new Label
        {
            Dock = DockStyle.Top, Height = 46,
            Text = T("Approve transmission of this exact report?", "Autorizar o envio deste relatório exato?",
                "Разрешить отправку именно этого отчёта?")
        };
        var body = new TextBox
        {
            Text = preview.Json, ReadOnly = true, Multiline = true, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical, BackColor = Field, ForeColor = DesktopTheme.Text, Font = Mono,
            BorderStyle = BorderStyle.FixedSingle
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0)
        };
        var approve = new ThemeButton { Text = T("Approve and send", "Autorizar e enviar", "Разрешить отправку"), DialogResult = DialogResult.OK, Kind = ButtonKind.Primary };
        var cancel = new ThemeButton { Text = T("Cancel", "Cancelar", "Отмена"), DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([approve, cancel]);
        dialog.Controls.Add(body);
        dialog.Controls.Add(actions);
        dialog.Controls.Add(explanation);
        dialog.CancelButton = cancel;
        dialog.ActiveControl = cancel;
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}
