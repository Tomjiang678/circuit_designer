using System.Drawing;

namespace CircuitModuleDesigner.Forms;

/// <summary>
/// Application visual language: one palette, one font set and a small set of styling
/// helpers so the shell and module controls do not each invent their own look.
/// </summary>
internal static class UiTheme
{
    public static readonly Color Accent = Color.FromArgb(45, 108, 223);
    public static readonly Color AccentDark = Color.FromArgb(33, 84, 178);
    public static readonly Color AccentSoft = Color.FromArgb(232, 240, 253);
    public static readonly Color Window = Color.FromArgb(242, 245, 249);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(214, 221, 231);
    public static readonly Color Text = Color.FromArgb(31, 41, 55);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);

    public static readonly Font Body = new("Segoe UI", 9F);
    public static readonly Font Subheading = new("Segoe UI Semibold", 10.5F);
    public static readonly Font Title = new("Segoe UI Semibold", 16F);
    public static readonly Font Caption = new("Segoe UI", 8.25F);
    public static readonly Font Mono = new("Consolas", 9F);

    public static void StylePrimaryButton(Button button)
    {
        StyleButton(button);
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = AccentDark;
        button.FlatAppearance.MouseOverBackColor = AccentDark;
        button.FlatAppearance.MouseDownBackColor = AccentDark;
    }

    public static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = AccentSoft;
        button.FlatAppearance.MouseDownBackColor = AccentSoft;
        button.BackColor = Surface;
        button.ForeColor = Text;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    /// <summary>Applies the neutral flat look to a module's control tree so modules blend into the shell.</summary>
    public static void ApplyModule(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case Button button:
                    StyleButton(button);
                    break;
                case TabControl tabs:
                    tabs.Font = Body;
                    break;
                case DataGridView grid:
                    grid.BackgroundColor = Surface;
                    grid.BorderStyle = BorderStyle.None;
                    grid.EnableHeadersVisualStyles = false;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = AccentSoft;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = AccentSoft;
                    break;
                case Label label when label.BackColor == SystemColors.Control:
                    label.BackColor = Color.Transparent;
                    break;
                case CheckBox check when check.BackColor == SystemColors.Control:
                    control.BackColor = Color.Transparent;
                    break;
                case RadioButton radio when radio.BackColor == SystemColors.Control:
                    control.BackColor = Color.Transparent;
                    break;
                default:
                    if (control.BackColor == SystemColors.Control) control.BackColor = Surface;
                    break;
            }
            if (control.HasChildren) ApplyModule(control);
        }
    }

    /// <summary>Menu palette. Drop-down selections use the accent colour.</summary>
    internal sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColorTable()) => RoundedEdges = false;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Selected) e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }
    }

    private sealed class MenuColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuItemSelected => Accent;
        public override Color MenuItemSelectedGradientBegin => Accent;
        public override Color MenuItemSelectedGradientEnd => Accent;
        public override Color MenuItemPressedGradientBegin => AccentSoft;
        public override Color MenuItemPressedGradientEnd => AccentSoft;
        public override Color MenuItemBorder => AccentSoft;
        public override Color MenuBorder => Border;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
    }
}

/// <summary>Thin white card with a single bottom border, used by the shell sections.</summary>
internal sealed class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Surface;
        Padding = new Padding(16, 12, 16, 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(UiTheme.Border);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}
