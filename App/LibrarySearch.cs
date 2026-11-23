using System.Drawing.Drawing2D;

namespace JixModMaker;

internal sealed class LibrarySearch : BufferedPanel
{
    private readonly TextBox _input;
    private readonly Label _cue = new() { BackColor = Theme.PicBg, ForeColor = Theme.Muted,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Cursor = Cursors.IBeam };
    public LibrarySearch(TextBox input)
    {
        _input = input;
        BackColor = Theme.Bar;
        _input.BorderStyle = BorderStyle.None;
        Controls.Add(_input);
        Controls.Add(_cue);
        _cue.BringToFront();
        _cue.Click += (_, _) => _input.Focus();
        Click += (_, _) => _input.Focus();
        _input.TextChanged += (_, _) => ShowCue();
        _input.GotFocus += (_, _) => ShowCue();
        _input.LostFocus += (_, _) => ShowCue();
        RefreshLanguage();
    }

    public void RefreshLanguage()
    {
        _cue.Text = _input.AccessibleName = UiText.T("搜索资源名 / bundle");
        ShowCue();
    }
    private void ShowCue() { _cue.Visible = !_input.Focused && _input.TextLength == 0; Invalidate(); }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_input == null) return;
        int inset = LogicalToDeviceUnits(30), h = _input.PreferredHeight;
        _input.SetBounds(inset, (Height - h) / 2, Math.Max(1, Width - inset - LogicalToDeviceUnits(8)), h);
        _cue.Bounds = new Rectangle(inset, 1, _input.Width, Math.Max(1, Height - 2));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        int w = Width - 1, h = Height - 1;
        if (w < 2 || h < 2) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = Theme.RoundRect(new Rectangle(0, 0, w, h), LogicalToDeviceUnits(8));
        using var surface = new SolidBrush(Theme.PicBg);
        using var line = new Pen(_input.Focused ? Theme.Cyan : Theme.Line);
        g.FillPath(surface, outline);
        g.DrawPath(line, outline);
        using var icon = new Font("Segoe MDL2 Assets", Font.Size);
        TextRenderer.DrawText(g, "\uE721", icon, new Rectangle(0, 0, LogicalToDeviceUnits(30), Height), Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }
}
