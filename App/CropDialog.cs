using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Img = SixLabors.ImageSharp.Image;
using PointF = System.Drawing.PointF;
using Point = System.Drawing.Point;
using RectangleF = System.Drawing.RectangleF;
using Rectangle = System.Drawing.Rectangle;
using SizeF = System.Drawing.SizeF;
using Color = System.Drawing.Color;

namespace JixModMaker;

/// <summary>
/// 画布合成编辑器: 把一张图自由缩放/平移到固定尺寸画布上, 所见即所得。
/// 画布(targetW×targetH) = 最终输出区域; 图层可比画布小(周围透明)或大(超出裁掉)。
/// 既能"缩小整图摆到一角", 也能"放大铺满裁切"。
/// </summary>
public class CropDialog : Form
{
    private readonly Bitmap _src;
    private readonly string _srcPath;
    private readonly int _targetW, _targetH;

    private readonly Panel _canvas = new() { Dock = DockStyle.Fill, BackColor = Theme.PicBg };

    private float _layerScale = 1f;     // 图层相对原图的缩放 (画布坐标)
    private PointF _offset;             // 图层左上角在画布坐标系中的位置
    private float _viewScale;           // 画布 → 屏幕显示缩放
    private PointF _canvasOrigin;       // 画布左上角在屏幕的位置

    private bool _dragging;
    private Point _dragStart;
    private PointF _offsetStart;

    public byte[] ResultPng { get; private set; }

    public CropDialog(string imagePath, int targetW, int targetH, string title)
    {
        _srcPath = imagePath;
        _src = new Bitmap(imagePath);
        _targetW = targetW; _targetH = targetH;

        Text = $"调整布局 — {title}  (画布 {targetW}×{targetH})";
        Width = 940; Height = 760;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.UI(9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += OnKey;

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Bar };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(8, 9, 0, 0), BackColor = Theme.Bar };
        var btnFit = Theme.FlatButton("适应整图"); btnFit.Margin = new Padding(3); btnFit.Click += (_, _) => { Fit(); };
        var btnFill = Theme.FlatButton("填满画布"); btnFill.Margin = new Padding(3); btnFill.Click += (_, _) => { Fill(); };
        var btnCenter = Theme.FlatButton("居中"); btnCenter.Margin = new Padding(3); btnCenter.Click += (_, _) => { Center(); };
        var btnZoomIn = Theme.FlatButton("放大 +"); btnZoomIn.Margin = new Padding(3); btnZoomIn.Click += (_, _) => ZoomLayer(1.1f);
        var btnZoomOut = Theme.FlatButton("缩小 −"); btnZoomOut.Margin = new Padding(3); btnZoomOut.Click += (_, _) => ZoomLayer(0.9f);
        tools.Controls.AddRange(new Control[] { btnFit, btnFill, btnCenter, btnZoomIn, btnZoomOut });

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 9, 8, 0), BackColor = Theme.Bar };
        var ok = Theme.FlatButton("✔ 确定"); ok.Margin = new Padding(3); ok.Click += (_, _) => DoRender();
        var cancel = Theme.FlatButton("取消"); cancel.Margin = new Padding(3);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        right.Controls.Add(ok); right.Controls.Add(cancel);
        bar.Controls.Add(tools); bar.Controls.Add(right);

        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 26, BackColor = Theme.Bar, ForeColor = Theme.SubText,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0),
            Text = "拖动=移动图片 · 滚轮/+−=缩放 · 方向键=微调(Shift 大步) · 亮区(画布)内为最终效果, 外面会被裁掉"
        };

        _canvas.Paint += OnPaint;
        _canvas.MouseDown += OnMouseDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseUp += (_, _) => _dragging = false;
        _canvas.MouseWheel += (_, e) => ZoomLayer(e.Delta > 0 ? 1.1f : 0.9f);
        _canvas.Resize += (_, _) => { RecalcView(); _canvas.Invalidate(); };
        typeof(Panel).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_canvas, true);

        Controls.Add(_canvas);
        Controls.Add(hint);
        Controls.Add(bar);

        Fit();   // 初始: 整图适应画布居中
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); RecalcView(); _canvas.Invalidate(); }

    // ---------- 视图/坐标 ----------

    private void RecalcView()
    {
        if (_canvas.ClientSize.Width <= 0) return;
        float pad = 40f;
        float availW = _canvas.ClientSize.Width - pad * 2, availH = _canvas.ClientSize.Height - pad * 2;
        _viewScale = Math.Min(availW / _targetW, availH / _targetH);
        _canvasOrigin = new PointF(
            (_canvas.ClientSize.Width - _targetW * _viewScale) / 2,
            (_canvas.ClientSize.Height - _targetH * _viewScale) / 2);
    }

    private PointF CanvasToScreen(PointF c) => new(_canvasOrigin.X + c.X * _viewScale, _canvasOrigin.Y + c.Y * _viewScale);

    // ---------- 布局操作 ----------

    private void Fit()
    {
        _layerScale = Math.Min((float)_targetW / _src.Width, (float)_targetH / _src.Height);
        Center();
    }
    private void Fill()
    {
        _layerScale = Math.Max((float)_targetW / _src.Width, (float)_targetH / _src.Height);
        Center();
    }
    private void Center()
    {
        _offset = new PointF((_targetW - _src.Width * _layerScale) / 2, (_targetH - _src.Height * _layerScale) / 2);
        _canvas.Invalidate();
    }
    private void ZoomLayer(float factor)
    {
        float ns = _layerScale * factor;
        if (ns < 0.02f || ns > 30f) return;
        // 以画布中心为锚缩放
        var cc = new PointF(_targetW / 2f, _targetH / 2f);
        _offset = new PointF(cc.X - (cc.X - _offset.X) * factor, cc.Y - (cc.Y - _offset.Y) * factor);
        _layerScale = ns;
        _canvas.Invalidate();
    }
    private void MoveLayer(float dx, float dy)
    {
        _offset = new PointF(_offset.X + dx, _offset.Y + dy);
        _canvas.Invalidate();
    }

    // ---------- 绘制 ----------

    private void OnPaint(object s, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.Clear(Theme.PicBg);

        // 图层
        var lp = CanvasToScreen(_offset);
        float lw = _src.Width * _layerScale * _viewScale, lh = _src.Height * _layerScale * _viewScale;
        g.DrawImage(_src, lp.X, lp.Y, lw, lh);

        // 画布区域
        var cp = CanvasToScreen(PointF.Empty);
        var canvasRect = new RectangleF(cp.X, cp.Y, _targetW * _viewScale, _targetH * _viewScale);

        // 画布外压暗 (提示会被裁掉)
        using (var dim = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
        {
            var reg = new Region(_canvas.ClientRectangle);
            reg.Exclude(Rectangle.Round(canvasRect));
            g.FillRegion(dim, reg);
        }
        // 画布边框 + 三分线
        using var pen = new Pen(Theme.Accent, 2f);
        g.DrawRectangle(pen, canvasRect.X, canvasRect.Y, canvasRect.Width, canvasRect.Height);
        using var thin = new Pen(Color.FromArgb(90, 255, 255, 255), 1f);
        for (int i = 1; i <= 2; i++)
        {
            g.DrawLine(thin, canvasRect.X + canvasRect.Width * i / 3, canvasRect.Y, canvasRect.X + canvasRect.Width * i / 3, canvasRect.Bottom);
            g.DrawLine(thin, canvasRect.X, canvasRect.Y + canvasRect.Height * i / 3, canvasRect.Right, canvasRect.Y + canvasRect.Height * i / 3);
        }
    }

    // ---------- 交互 ----------

    private void OnMouseDown(object s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true; _dragStart = e.Location; _offsetStart = _offset;
        _canvas.Focus();
    }
    private void OnMouseMove(object s, MouseEventArgs e)
    {
        if (!_dragging) return;
        _offset = new PointF(
            _offsetStart.X + (e.X - _dragStart.X) / _viewScale,
            _offsetStart.Y + (e.Y - _dragStart.Y) / _viewScale);
        _canvas.Invalidate();
    }
    private void OnKey(object s, KeyEventArgs e)
    {
        float step = e.Shift ? 20 : 4;
        switch (e.KeyCode)
        {
            case Keys.Left: MoveLayer(-step, 0); break;
            case Keys.Right: MoveLayer(step, 0); break;
            case Keys.Up: MoveLayer(0, -step); break;
            case Keys.Down: MoveLayer(0, step); break;
            case Keys.Oemplus: case Keys.Add: ZoomLayer(1.1f); break;
            case Keys.OemMinus: case Keys.Subtract: ZoomLayer(0.9f); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------- 输出 ----------

    private void DoRender()
    {
        try
        {
            using var canvas = new Image<Rgba32>(_targetW, _targetH);   // 透明背景
            using var layer = Img.Load<Rgba32>(_srcPath);
            int lw = Math.Max(1, (int)Math.Round(_src.Width * _layerScale));
            int lh = Math.Max(1, (int)Math.Round(_src.Height * _layerScale));
            layer.Mutate(c => c.Resize(lw, lh));
            var loc = new SixLabors.ImageSharp.Point((int)Math.Round(_offset.X), (int)Math.Round(_offset.Y));
            canvas.Mutate(c => c.DrawImage(layer, loc, 1f));

            using var ms = new MemoryStream();
            canvas.SaveAsPng(ms);
            ResultPng = ms.ToArray();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { MessageBox.Show($"生成失败：{ex.Message}", "错误"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _src?.Dispose();
        base.Dispose(disposing);
    }
}
