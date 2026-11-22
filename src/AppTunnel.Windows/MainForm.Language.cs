using System.Reflection;
using static AppTunnel.Core.Localized;
using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private Control BuildLanguageSelector()
    {
        var languages = new[] { ("en", "us", "English"), ("pt", "br", "Português"), ("ru", "ru", "Русский") };
        var (_, flag, _) = languages.First(language => language.Item1 == Language);
        var title = T("Language", "Idioma", "Язык");
        var current = languages.First(language => language.Item1 == Language).Item3;
        var button = MakeButton($"{current} · {title}", ButtonKind.Quiet);
        button.Font = Body(9);
        button.AutoSize = false;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 2, 0, 2);
        button.Padding = new Padding(12, 0, 8, 0);
        button.AccessibleName = title;
        button.Image = LoadFlag(flag);

        var menuFont = new Font("Segoe UI", 10);
        var menu = new ContextMenuStrip
        {
            Font = menuFont, BackColor = Raised, ForeColor = DesktopTheme.Text, Renderer = new ThemeMenuRenderer(),
            ShowImageMargin = true, ShowCheckMargin = true, ImageScalingSize = new Size(24, 18), Padding = new Padding(4)
        };
        foreach (var (code, country, name) in languages)
        {
            var icon = LoadFlag(country);
            var item = new ToolStripMenuItem(name, icon, (_, _) => BeginInvoke(() => LanguageRequested?.Invoke(code)))
            {
                Checked = code == Language, ForeColor = DesktopTheme.Text, Padding = new Padding(3, 4, 8, 4)
            };
            item.Disposed += (_, _) => icon.Dispose();
            menu.Items.Add(item);
        }
        button.ContextMenuStrip = menu;
        button.Click += (_, _) => menu.Show(button, new Point(0, -menu.GetPreferredSize(Size.Empty).Height - 4));
        button.Disposed += (_, _) =>
        {
            button.Image?.Dispose();
            menu.Dispose();
            menuFont.Dispose();
        };
        return button;
    }

    private static Image LoadFlag(string country)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"AppTunnel.Flags.{country}.png")!;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
