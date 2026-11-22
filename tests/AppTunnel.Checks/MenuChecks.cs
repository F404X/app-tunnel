using AppTunnel.Windows;

// Builds and paints the themed menus and selection fields off screen, in both themes, so a renderer that throws
// is caught before release. Set APPTUNNEL_PREVIEW_DIR to also save the pictures for a visual review.
internal static class MenuChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var preview = Environment.GetEnvironmentVariable("APPTUNNEL_PREVIEW_DIR");
                foreach (var dark in new[] { false, true })
                {
                    DesktopTheme.Apply(dark ? ThemeMode.Dark : ThemeMode.Light);
                    var theme = dark ? "dark" : "light";
                    using (var menu = new ThemeMenu())
                    {
                        menu.Add("From a running app…", Glyph.Window);
                        menu.Add("Browse for an .exe…", Glyph.File);
                        menu.Add("Whole folder…", Glyph.Folder).Checked = true;
                        menu.Add("Unavailable option", Glyph.Import).Enabled = false;
                        menu.AddSeparator();
                        menu.Add("Remove", Glyph.Trash).Destructive = true;
                        Paint(menu, preview, $"menu-{theme}");
                    }
                    using (var flags = new ThemeMenu { IconSize = new Size(24, 18) })
                    {
                        using var flag = new Bitmap(24, 18);
                        using (var g = Graphics.FromImage(flag)) g.Clear(Color.SteelBlue);
                        flags.Add("English", Glyph.None).Image = flag;
                        var picked = flags.Add("Português", Glyph.None);
                        picked.Image = flag;
                        picked.Checked = true;
                        Paint(flags, preview, $"flags-{theme}");
                    }
                    using (var icons = new ThemeMenu())
                    {
                        foreach (var glyph in Enum.GetValues<Glyph>().Where(glyph => glyph != Glyph.None)) icons.Add(glyph.ToString(), glyph);
                        Paint(icons, preview, $"glyphs-{theme}");
                    }
                    using var combo = new ThemeCombo { Width = 260 };
                    combo.Items.AddRange(["Stable", "Beta · previews of the next version"]);
                    combo.Glyphs.AddRange([Glyph.CheckCircle, Glyph.Flask]);
                    combo.SelectedIndex = 1;
                    using var picture = new Bitmap(combo.Width, combo.Height);
                    combo.DrawToBitmap(picture, new Rectangle(Point.Empty, combo.Size));
                    if (preview is not null) picture.Save(Path.Combine(preview, $"combo-{theme}.png"));
                }
            }
            catch (Exception error) { failure = error; }
            finally { DesktopTheme.Apply(ThemeMode.Light); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("A menu failed to paint: " + failure.Message, failure);
    }

    private static void Paint(ThemeMenu menu, string? preview, string name)
    {
        _ = menu.Handle;
        menu.PerformLayout();
        var size = menu.GetPreferredSize(Size.Empty);
        Require(size.Width > 0 && size.Height > 0);
        menu.Size = size;
        // A dropdown lays out its items only when shown; run the same layout here so the picture matches.
        menu.LayoutEngine.Layout(menu, new LayoutEventArgs(menu, "Bounds"));
        // Every row spans the menu, so the check column at the right is never cut off.
        foreach (ToolStripItem item in menu.Items)
            if (item is ToolStripMenuItem) Require(item.Bounds.Right <= size.Width && item.Bounds.Width >= size.Width - 4);
        using var picture = new Bitmap(size.Width, size.Height);
        menu.DrawToBitmap(picture, new Rectangle(Point.Empty, size));
        if (preview is not null) picture.Save(Path.Combine(preview, name + ".png"));
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("A verificação de menu falhou.");
    }
}
