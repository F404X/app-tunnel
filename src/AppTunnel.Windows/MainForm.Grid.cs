using static AppTunnel.Windows.DesktopTheme;

namespace AppTunnel.Windows;

internal sealed partial class MainForm
{
    private static DataGridView MakeGrid() => new()
    {
        Dock = DockStyle.Fill, Margin = new Padding(0), BorderStyle = BorderStyle.None,
        AccessibleRole = AccessibleRole.Table, BackgroundColor = Field, GridColor = Border,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false, EnableHeadersVisualStyles = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
        ColumnHeadersHeight = 32, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = DesktopTheme.Text, SelectionBackColor = Selection, SelectionForeColor = DesktopTheme.Text,
            Font = Body(), Padding = new Padding(7, 0, 5, 0), WrapMode = DataGridViewTriState.False,
        },
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Raised, ForeColor = Muted, SelectionBackColor = Raised, SelectionForeColor = Muted,
            Font = Body(9, bold: true), Padding = new Padding(7, 0, 5, 0)
        },
        RowTemplate = { Height = 35 },
        ShowCellToolTips = true
    };

    private static void AddColumn(DataGridView grid, string caption, int width, bool fill = false, Font? font = null)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = caption,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            MinimumWidth = fill ? width : 38,
            Width = width,
            DefaultCellStyle = new DataGridViewCellStyle { Font = font }
        });
    }

    private static void AddCheckColumn(DataGridView grid)
    {
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "VPN", Width = 51, Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable, ThreeState = false
        });
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || e.Graphics is not { } graphics) return;
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground |
                DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
            var side = grid.DeviceDpi * 16 / 96;
            var bounds = e.CellBounds;
            PaintCheck(graphics, new Rectangle(bounds.X + (bounds.Width - side) / 2,
                bounds.Y + (bounds.Height - side) / 2, side, side), e.Value is true, grid.Enabled);
            e.Handled = true;
        };
    }

    private static Control GridFrame(DataGridView grid, Label empty, string message)
    {
        var box = new Panel
        {
            Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Border, Margin = new Padding(0)
        };
        empty.Text = message;
        empty.Dock = DockStyle.Bottom;
        empty.Height = 40;
        empty.Padding = new Padding(12, 0, 12, 0);
        empty.BackColor = Field;
        box.Controls.Add(grid);
        box.Controls.Add(empty);
        empty.BringToFront();

        grid.HandleCreated += (_, _) =>
        {
            grid.RowTemplate.Height = 36 * grid.DeviceDpi / 96;
            grid.ColumnHeadersHeight = 32 * grid.DeviceDpi / 96;
            foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
        };
        return box;
    }
}
