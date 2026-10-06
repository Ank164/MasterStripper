namespace MasterStripper;

internal sealed class ThemeButton : Button
{
    internal Color DisabledTextColor { get; set; } = SystemColors.GrayText;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Enabled) { base.OnPaint(e); return; }
        e.Graphics.Clear(BackColor);
        using var pen = new Pen(FlatAppearance.BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, DisabledTextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

internal static class UiTheme
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MasterStripper", "theme.txt");

    internal static bool LoadDarkMode()
    {
        try { return !File.Exists(PreferencePath) || File.ReadAllText(PreferencePath).Trim() != "light"; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    internal static void SaveDarkMode(bool dark)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, dark ? "dark" : "light");
        }
        catch (IOException) { /* The theme still works for this session. */ }
        catch (UnauthorizedAccessException) { /* The theme still works for this session. */ }
    }

    internal static void Apply(Control root, bool dark, Button primary)
    {
        var background = dark ? Color.FromArgb(24, 27, 33) : Color.FromArgb(244, 246, 250);
        var surface = dark ? Color.FromArgb(34, 39, 48) : Color.White;
        var foreground = dark ? Color.FromArgb(230, 234, 241) : Color.FromArgb(30, 39, 53);
        var border = dark ? Color.FromArgb(65, 75, 91) : Color.FromArgb(194, 204, 219);
        var accent = Color.FromArgb(43, 102, 195);

        void Style(Control control)
        {
            control.BackColor = control is TextBox or ListBox or Button ? surface : background;
            control.ForeColor = foreground;
            if (control is TextBox text) text.BorderStyle = BorderStyle.FixedSingle;
            if (control is ListBox list) list.BorderStyle = BorderStyle.FixedSingle;
            if (control is Button button)
            {
                if (button is ThemeButton themed)
                    themed.DisabledTextColor = dark ? Color.FromArgb(148, 160, 178) : Color.FromArgb(108, 119, 135);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = border;
                button.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(48, 58, 73) : Color.FromArgb(226, 233, 244);
                button.FlatAppearance.MouseDownBackColor = dark ? Color.FromArgb(59, 71, 89) : Color.FromArgb(209, 221, 239);
                button.UseVisualStyleBackColor = false;
                button.Cursor = Cursors.Hand;
                if (button == primary)
                {
                    button.BackColor = accent;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = accent;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 119, 217);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(33, 83, 162);
                }
            }
            foreach (Control child in control.Controls) Style(child);
        }

        root.SuspendLayout();
        Style(root);
        root.ResumeLayout();
        root.Invalidate(true);
    }
}
