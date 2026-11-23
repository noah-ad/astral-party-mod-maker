using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace JixModMaker;

internal sealed class StudioNavigation : BufferedPanel
{
    private readonly PictureBox _logo;
    private readonly Label _brand = Theme.Caption("", true);
    private readonly Button _language = Theme.FlatButton("", 84);
    private readonly List<Button> _tabs = new();
    private readonly ToolTip _tips = new();
    private readonly System.Windows.Forms.Timer _motion = new() { Interval = 16 };
    private readonly Stopwatch _clock = new();
    private int _selected, _scale = 96;
    private float _from, _left, _to, _lineWidth;
    private bool _layout;
    public int Direction { get; private set; } = 1;
    private static readonly (string Id, string Name, string Symbol)[] Pages =
        { ("dashboard", "工作台", "\uE80F"), ("browse", "资源库", "\uE8A9"),
          ("pack", "作品与导出", "\uE8B7"), ("tools", "工具", "\uE713") };

    public StudioNavigation(Image logo, Action<string> navigate, Action changeLanguage)
    {
        Dock = DockStyle.Top;
        BackColor = Theme.Bar;
        _logo = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, TabStop = false };
        _brand.ForeColor = Theme.Text;
        _brand.Font = Theme.UI(13, true);
        Controls.Add(_logo);
        Controls.Add(_brand);
        foreach (var (id, _, symbol) in Pages)
        {
            var button = Theme.FlatButton("", 90);
            button.Tag = id;
            button.Padding = Padding.Empty;
            button.BackColor = Theme.Bar;
            if (button is RoundButton b) { b.Symbol = symbol; b.BorderColor = Color.Transparent; b.HoverBackColor = Theme.CardAlt; }
            button.Click += (_, _) => navigate(id);
            _tabs.Add(button);
            Controls.Add(button);
        }
        _language.Click += (_, _) => changeLanguage();
        Controls.Add(_language);
        _motion.Tick += (_, _) =>
        {
            double t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / 155);
            _left = _from + (_to - _from) * (float)(1 - Math.Pow(1 - t, 3));
            if (t >= 1) _motion.Stop();
            Invalidate(new Rectangle(0, Height - Px(6), Width, Px(6)));
        };
        RefreshLanguage();
    }

    private int Px(int n) => (int)Math.Round(n * _scale / 96d);
    public void SetDpi(int dpi) { _scale = dpi; PerformLayout(); }

    public void RefreshLanguage()
    {
        _brand.Text = UiText.T("吉星工坊");
        _language.Text = UiText.English ? "中文" : "English";
        _language.AccessibleName = UiText.English ? "Switch to Chinese" : "切换到英文";
        _tips.SetToolTip(_language, _language.AccessibleName);
        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Text = UiText.T(Pages[i].Name);
            _tabs[i].AccessibleName = _tabs[i].Text;
        }
        PerformLayout();
    }

    public void SelectPage(string id, bool animate)
    {
        int previous = _selected;
        _selected = Array.FindIndex(Pages, x => x.Id == id);
        if (_selected < 0) return;
        Direction = _selected < previous ? -1 : 1;
        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].ForeColor = i == _selected ? Theme.Accent : Theme.SubText;
            _tabs[i].BackColor = Theme.Bar;
        }
        PositionIndicator(animate);
    }

    private void PositionIndicator(bool animate)
    {
        var tab = _tabs[_selected];
        _lineWidth = Math.Min(tab.Width * .65f, Px(56));
        _to = tab.Left + (tab.Width - _lineWidth) / 2;
        _motion.Stop();
        if (animate && IsHandleCreated && Visible && StudioMotion.Enabled)
        {
            _from = _left;
            _clock.Restart();
            _motion.Start();
        }
        else _left = _to;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_layout || _tabs?.Count != 4 || _language.Parent == null) return;
        _layout = true;
        try
        {
            int pad = Px(16), gap = Px(5), h = Math.Max(Px(34), _language.Font.Height + Px(10));
            int brandW = TextRenderer.MeasureText(_brand.Text, _brand.Font).Width + Px(4);
            int languageW = Math.Max(Px(78), TextRenderer.MeasureText(_language.Text, _language.Font).Width + _language.Padding.Horizontal);
            int[] widths = _tabs.Select(b => Math.Max(Px(76), TextRenderer.MeasureText(b.Text, b.Font).Width + b.Font.Height + Px(24))).ToArray();
            int logo = Px(38), brandEnd = pad + logo + Px(9) + brandW;
            bool stacked = brandEnd + Px(32) + widths.Sum() + gap * 3 + languageW + 2 * pad > Width;
            int brandH = Math.Max(h + Px(15), logo + Px(13));
            int height = stacked ? brandH + h + Px(9) : Math.Max(Px(64), brandH);
            if (Height != height) Height = height;
            int mid = stacked ? brandH / 2 : Height / 2;
            _logo.SetBounds(pad, mid - logo / 2, logo, logo);
            _brand.SetBounds(pad + logo + Px(9), mid - h / 2, brandW, h);
            _language.SetBounds(Width - pad - languageW, mid - h / 2, languageW, h);
            int x = stacked ? pad : brandEnd + Px(32);
            for (int i = 0; i < _tabs.Count; i++)
            {
                int w = stacked ? Math.Min(widths[i], Math.Max(1, (Width - 2 * pad - gap * 3) / 4)) : widths[i];
                _tabs[i].SetBounds(x, stacked ? brandH : mid - h / 2, w, h);
                x += w + gap;
            }
            PositionIndicator(false);
        }
        finally { _layout = false; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.Line);
        e.Graphics.DrawLine(border, 0, Height - 1, Width, Height - 1);
        if (_lineWidth < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Theme.Accent);
        using var path = Theme.RoundRect(new Rectangle((int)_left, Height - Px(4), (int)_lineWidth, Math.Max(2, Px(3))), Px(2));
        e.Graphics.FillPath(brush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _motion.Dispose(); _tips.Dispose(); _logo.Image?.Dispose(); }
        base.Dispose(disposing);
    }
}
