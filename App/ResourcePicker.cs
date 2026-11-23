using System.Drawing.Drawing2D;

namespace JixModMaker;

internal sealed class ResourcePicker : RoundButton
{
    private readonly ContextMenuStrip _menu = new() { ShowImageMargin = false, ShowCheckMargin = false, Padding = new Padding(3) };
    private int _index;
    public string Kind => ResourceKinds.All[_index].Id;
    public int PreferredHeight => Math.Max(LogicalToDeviceUnits(32), Font.Height + LogicalToDeviceUnits(12));
    public event EventHandler SelectionChanged;

    public ResourcePicker()
    {
        TextAlign = ContentAlignment.MiddleLeft;
        Radius = 8;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        HoverBackColor = Theme.CardAlt;
        Padding = new Padding(10, 0, 23, 0);
        AccessibleRole = AccessibleRole.ButtonDropDown;
        _menu.BackColor = Theme.Card;
        _menu.ForeColor = Theme.Text;
        _menu.Renderer = new MenuPaint();
        RefreshLanguage();
    }

    public void RefreshLanguage()
    {
        Text = UiText.T(ResourceKinds.All[_index].Label);
        AccessibleName = UiText.T("资源类型") + ": " + Text;
        foreach (ToolStripItem item in _menu.Items) item.Text = UiText.T(ResourceKinds.All[(int)item.Tag].Label);
    }

    private void SelectKind(int index)
    {
        if (index == _index || index < 0 || index >= ResourceKinds.All.Length) return;
        _index = index;
        RefreshLanguage();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (_menu.Visible) { _menu.Close(); return; }
        if (_menu.Items.Count == 0)
            for (int i = 0; i < ResourceKinds.All.Length; i++)
            {
                int slot = i;
                var item = new ToolStripMenuItem { Tag = i, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(10, 0, 0, 0) };
                item.Click += (_, _) => SelectKind(slot);
                _menu.Items.Add(item);
            }
        RefreshLanguage();
        foreach (ToolStripMenuItem item in _menu.Items)
        {
            item.Size = new Size(Math.Max(Width - _menu.Padding.Horizontal, LogicalToDeviceUnits(112)), PreferredHeight);
            item.Checked = (int)item.Tag == _index;
            item.BackColor = Theme.Card;
            item.ForeColor = item.Checked ? Theme.Accent : Theme.Text;
        }
        _menu.AutoSize = false;
        _menu.Size = new Size(Width, _menu.Padding.Vertical + _menu.Items.Cast<ToolStripItem>().Sum(i => i.Height + i.Margin.Vertical));
        _menu.Show(this, new Point(0, Height + 2));
        _menu.Items[_index].Select();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Down) || keyData == Keys.F4) { PerformClick(); return true; }
        if (keyData == Keys.Down || keyData == Keys.Up || keyData == Keys.Home || keyData == Keys.End)
        {
            SelectKind(keyData switch { Keys.Home => 0, Keys.End => ResourceKinds.All.Length - 1, Keys.Up => _index - 1, _ => _index + 1 });
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int t = Math.Max(3, LogicalToDeviceUnits(4)), x = Width - 3 * t - 1, y = Height / 2 - t / 2;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.SubText, Math.Max(1.5f, t / 3f));
        e.Graphics.DrawLines(pen, new[] { new Point(x - t, y), new Point(x, y + t), new Point(x + t, y) });
    }

    protected override void Dispose(bool disposing) { if (disposing) _menu.Dispose(); base.Dispose(disposing); }

    private sealed class MenuPaint : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            => e.Graphics.Clear(Theme.Card);
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Theme.Line);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            bool current = e.Item is ToolStripMenuItem { Checked: true };
            using var fill = new SolidBrush(e.Item.Selected ? Theme.CyanDim : current ? Theme.CardMod : Theme.Card);
            e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size));
            if (!current) return;
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, 0, e.Item.Height / 4, 3, e.Item.Height / 2);
        }
    }
}
