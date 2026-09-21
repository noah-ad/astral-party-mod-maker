namespace JixModMaker;

public sealed class PortraitViewTabs : Control
{
    private int _selectedIndex;
    public event EventHandler SelectedIndexChanged;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public PortraitViewTabs()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Height = 42;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = "取景与动态预览";
        AccessibleRole = AccessibleRole.PageTabList;
    }
    private int TabWidth => (int)(124 * DeviceDpi / 96f);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var line = new Pen(Theme.Line);
        e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
        string[] labels = { "取景", "动态预览" };
        for (int i = 0; i < labels.Length; i++)
        {
            var bounds = new Rectangle(i * TabWidth, 0, TabWidth, Height - 4);
            TextRenderer.DrawText(e.Graphics, labels[i], Font, bounds, Enabled ? (i == SelectedIndex ? Theme.Cyan : Theme.SubText) : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (i == SelectedIndex)
            {
                using var accent = new SolidBrush(Theme.Cyan);
                e.Graphics.FillRectangle(accent, bounds.X + 12, Height - 3, bounds.Width - 24, 3);
                if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -6, -6));
            }
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || e.X >= 2 * TabWidth) return;
        Focus();
        SelectedIndex = e.X / TabWidth;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Space)) return;
        SelectedIndex = e.KeyCode == Keys.Left ? 0 : e.KeyCode == Keys.Right ? 1 : 1 - SelectedIndex;
        e.Handled = true;
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
