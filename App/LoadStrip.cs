using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace JixModMaker;

internal enum LoadStage { Idle, Detect, Resolve, Workspace, Cache, Enumerate, Scan, Save, Match, Verify, Import, Ready, Empty, Failed }

internal sealed class LoadStrip : Control
{
    private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 45 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _done, _total;
    public bool TrackOnly { get; init; }
    public GameEdition? Edition { get; set; }
    public LoadStage Stage { get; private set; }
    public string Caption => (Stage == LoadStage.Ready && Edition is { } edition
        ? UiText.T(edition == GameEdition.International ? "海外服" : "国服") + " · " : "") + UiText.T(Stage switch
    {
        LoadStage.Detect => "正在检测 Steam 安装",
        LoadStage.Resolve => "正在定位资源目录",
        LoadStage.Workspace => "正在读取修改记录",
        LoadStage.Cache => "正在校验索引缓存",
        LoadStage.Enumerate => "正在枚举资源包",
        LoadStage.Scan => "正在扫描资源包",
        LoadStage.Save => "正在保存索引",
        LoadStage.Match => "正在匹配资源与修改记录",
        LoadStage.Verify => "资源可浏览 · 后台核验",
        LoadStage.Import => "正在应用图包...",
        LoadStage.Ready => "资源库已就绪",
        LoadStage.Empty => "目录内没有可浏览资源",
        LoadStage.Failed => "资源库未能打开",
        _ => "尚未连接游戏目录"
    }) + (_total > 0 ? $"   {_done:N0} / {_total:N0}  ({100L * _done / _total}%)" : "");

    public LoadStrip()
    {
        Dock = DockStyle.Left;
        BackColor = Theme.Bar;
        TabStop = false;
        AccessibleRole = AccessibleRole.StatusBar;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _pulse.Tick += (_, _) => Invalidate();
    }

    public void Set(LoadStage stage, int done = 0, int total = 0)
    {
        Stage = stage; _done = done; _total = total;
        RefreshCaption();
        SetPulse();
    }
    public void RefreshCaption() { AccessibleName = Text = Caption; Invalidate(); }
    private void SetPulse() => _pulse.Enabled = Visible && Busy && _total == 0 && StudioMotion.Enabled;
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (_pulse != null) SetPulse(); }
    public void CopyTo(LoadStrip target) => target.Set(Stage, _done, _total);

    private bool Busy => Stage is >= LoadStage.Detect and <= LoadStage.Import;
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int pad = TrackOnly ? 0 : LogicalToDeviceUnits(12);
        int track = TrackOnly ? Width : Busy ? Math.Min(Width / 3, LogicalToDeviceUnits(144)) : 0;
        int start = pad;
        Color accent = Stage == LoadStage.Failed ? Theme.Danger : Stage == LoadStage.Empty ? Theme.Warn : Theme.Cyan;
        using var fill = new SolidBrush(accent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (track > 8)
        {
            int h = Math.Max(4, LogicalToDeviceUnits(TrackOnly ? 8 : 5)), y = (Height - h) / 2;
            using var shape = Theme.RoundRect(new Rectangle(pad, y, track, h), h / 2);
            using var rail = new SolidBrush(Theme.Line);
            g.FillPath(rail, shape);
            var state = g.Save();
            g.SetClip(shape, CombineMode.Intersect);
            int segment = _total > 0 ? (int)Math.Round(track * Math.Clamp((double)_done / _total, 0, 1)) : track / 3;
            int offset = _total > 0 ? 0 : _pulse.Enabled
                ? (int)((track - segment) * (.5 - .5 * Math.Cos(_clock.ElapsedMilliseconds / 270d))) : (track - segment) / 2;
            g.FillRectangle(fill, pad + offset, y, segment, h);
            g.Restore(state);
            start += track + pad;
        }
        else
        {
            int d = Math.Max(5, LogicalToDeviceUnits(7));
            g.FillEllipse(fill, start, (Height - d) / 2, d, d);
            start += d + pad / 2;
        }
        if (!TrackOnly) TextRenderer.DrawText(g, Caption, Font, new Rectangle(start, 0, Math.Max(1, Width - start - 2), Height),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pulse.Dispose();
        base.Dispose(disposing);
    }
}
