using AppTunnel.Windows;

// Builds the app's message dialogs without showing them. v0.13.0 and v0.13.1 shipped a question dialog that threw
// while it was being put together, so "Check for updates" downloaded every update and then failed to install it.
internal static class DialogChecks
{
    public static void Run()
    {
        Exception? failure = null;
        // Windows Forms expects a single-threaded apartment, as on the app's UI thread.
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var question in new[] { true, false })
                foreach (var standalone in new[] { true, false })
                {
                    using var dialog = ThemedMessage.Build(standalone, "Install AppTunnel 9.9.9 now? " + new string('x', 400), question);
                    var buttons = dialog.Controls.OfType<FlowLayoutPanel>().SelectMany(panel => panel.Controls.OfType<Button>()).ToArray();
                    Require(buttons.Length == (question ? 2 : 1));
                    // A question starts on "No", so Enter never confirms by accident.
                    if (question) Require(dialog.ActiveControl is Button { DialogResult: DialogResult.No });
                }
                // The changelog that asks before installing, and the one shown after an update, with Read more.
                foreach (var question in new[] { true, false })
                foreach (var standalone in new[] { true, false })
                {
                    var notes = string.Join('\n', Enumerable.Range(1, 80).Select(line => $"• Change {line} " + new string('x', line)));
                    using var changelog = ChangelogWindow.Build(standalone, "AppTunnel 9.9.9 is available",
                        question ? "Install AppTunnel 9.9.9 now? The setup will close the AppTunnel interface." : "", "• Short change", notes, question);
                    var buttons = changelog.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<FlowLayoutPanel>())
                        .SelectMany(bar => bar.Controls.OfType<Button>()).ToArray();
                    Require(buttons.Count(button => button.DialogResult != DialogResult.None) == (question ? 2 : 1));
                    if (question) Require(changelog.ActiveControl is Button { DialogResult: DialogResult.No });
                    var before = changelog.ClientSize.Height;
                    // PerformClick ignores a window that is not shown, so the click is raised directly.
                    var readMore = buttons.Single(button => button.DialogResult == DialogResult.None);
                    typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(readMore, [EventArgs.Empty]);
                    var text = changelog.Controls.OfType<ThemeCard>().Single().Controls.OfType<RichTextBox>().Single();
                    Require(text.Text.Contains("Change 80") && changelog.ClientSize.Height >= before);
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("A dialog failed to build: " + failure.Message, failure);

        // A language picked from the language menu is applied in a BeginInvoke callback; windows opened afterwards,
        // such as Choose route, must use it although .NET restores the thread culture when the callback ends.
        thread = new Thread(() =>
        {
            try
            {
                AppTunnel.Core.Localized.SetLanguage("en");
                using var form = new Form();
                _ = form.Handle;
                form.BeginInvoke(() => AppTunnel.Core.Localized.SetLanguage("pt"));
                Application.DoEvents();
                Require(AppTunnel.Core.Localized.T("Choose route", "Escolher rota", "Выбор маршрута") == "Escolher rota");
            }
            catch (Exception error) { failure = error; }
            finally { AppTunnel.Core.Localized.SetLanguage("en"); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("A dialog failed to build: " + failure.Message, failure);
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("A verificação de diálogo falhou.");
    }
}
