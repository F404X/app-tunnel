using AppTunnel.Core;
using AppTunnel.Windows;

// Lays out the "Explore common apps" dialog off screen and checks its cards form an even grid: every row but the last
// is full, all cards share one width, and the grid has the same space on its left and right.
internal static class CatalogChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var found = new[] { new ApplicationTarget("Discord", @"C:\Apps\Discord.exe", true, false) };
                var choices = new List<(CheckBox Check, bool InitiallySelected, ApplicationTarget[] Routes)>();
                using var dialog = MainForm.BuildAppCatalog(found, _ => false, choices);
                _ = dialog.Handle;
                dialog.PerformLayout();
                var cards = dialog.Controls[0].Controls.OfType<FlowLayoutPanel>().First(panel => panel.Controls.Count == CommonApps.Catalog.Count);
                cards.PerformLayout();
                // The dialog as it opens, then scaled like a 150% display, then made narrower by hand.
                CheckGrid(cards, expectColumns: 4);
                dialog.Scale(new SizeF(1.5f, 1.5f));
                dialog.PerformLayout();
                CheckGrid(cards, expectColumns: 4);
                foreach (var width in new[] { 900, 760, 640 })
                {
                    dialog.ClientSize = new Size(width, dialog.ClientSize.Height);
                    dialog.PerformLayout();
                    CheckGrid(cards, expectColumns: null);
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("The common apps dialog failed: " + failure.Message, failure);
    }

    private static void CheckGrid(FlowLayoutPanel cards, int? expectColumns)
    {
        cards.PerformLayout();
        var boxes = cards.Controls.Cast<Control>().Select(card => card.Bounds).ToArray();
        var columns = boxes.Count(box => box.Top == boxes[0].Top);
        var left = boxes.Min(box => box.Left);
        var right = cards.ClientSize.Width - boxes.Max(box => box.Right);
        Require(columns >= 1 && (expectColumns is null || columns == expectColumns));
        Require(boxes.All(box => box.Width == boxes[0].Width));
        // Every row but the last is full.
        Require(boxes.GroupBy(box => box.Top).SkipLast(1).All(row => row.Count() == columns));
        Require(Math.Abs(left - right) <= 1);
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("A verificação do catálogo falhou.");
    }
}
