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
            }
            catch (Exception error) { failure = error; }
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
