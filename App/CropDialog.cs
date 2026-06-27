using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Img = SixLabors.ImageSharp.Image;
using IRect = SixLabors.ImageSharp.Rectangle;
using PointF = System.Drawing.PointF;
using Point = System.Drawing.Point;
using RectangleF = System.Drawing.RectangleF;
using Rectangle = System.Drawing.Rectangle;
using SizeF = System.Drawing.SizeF;
using Color = System.Drawing.Color;

namespace JixModMaker;

/// <summary>
/// 裁切窗口: 显示原图 + 按目标宽高比锁定的裁切框, 可拖动/缩放/滚轮调整。
/// 确定后裁出区域并缩放到 (targetW × targetH) 返回 PNG 字节。
/// </summary>
public class CropDialog : Form
{
    private readonly Bitmap _src;          // 原图 (WinForms 位图, 用于显示)
    private readonly string _srcPath;
    private readonly int _targetW, _targetH;
    private readonly double _aspect;        // 目标宽高比 W/H

    private readonly Panel _canvas = new() { Dock = DockStyle.Fill, BackColor = Theme.PicBg };
    private RectangleF _crop;               // 裁切框 (图像像素坐标)
    private float _scale;                   // 显示缩放
    private PointF _imgOrigin;              // 图像在画布上的左上角偏移
    private bool _dragging;
    private PointF _dragStart;
    private RectangleF _cropStart;

    public byte[] ResultPng { get; private set; }

    public CropDialog(string imagePath, int targetW, int targetH, string title)
    {
        _srcPath = imagePath;
        _src = new Bitmap(imagePath);
        _targetW = targetW; _targetH = targetH;
        _aspect = (double)targetW / targetH;

        Text = $"裁切 — {title}  (目标 {targetW}×{targetH})";
        Width = 900; Height = 720;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.UI(9.5f);

        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += OnKey;

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Bar };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(8, 9, 0, 0), BackColor = Theme.Bar };
        var btnCenter = Theme.FlatButton("居中"); btnCenter.Margin = new Padding(3); btnCenter.Click += (_, _) => CenterCrop();
        var btnFill = Theme.FlatButton("铺满"); btnFill.Margin = new Padding(3); btnFill.Click += (_, _) => { InitCrop(); _canvas.Invalidate(); };
        var btnZoomIn = Theme.FlatButton("放大 +"); btnZoomIn.Margin = new Padding(3); btnZoomIn.Click += (_, _) => ZoomCrop(0.9f);
        var btnZoomOut = Theme.FlatButton("缩小 −"); btnZoomOut.Margin = new Padding(3); btnZoomOut.Click += (_, _) => ZoomCrop(1.111f);
        tools.Controls.AddRange(new Control[] { btnCenter, btnFill, btnZoomIn, btnZoomOut });

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 9, 8, 0), BackColor = Theme.Bar };
        var ok = Theme.FlatButton("✔ 确定"); ok.Margin = new Padding(3); ok.Click += (_, _) => DoCrop();
        var cancel = Theme.FlatButton("取消"); cancel.Margin = new Padding(3);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        right.Controls.Add(ok); right.Controls.Add(cancel);
        bar.Controls.Add(tools); bar.Controls.Add(right);

        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 26, BackColor = Theme.Bar, ForeColor = Theme.SubText,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0),
            Text = "拖动框=移动 · 方向键=微调(Shift 大步) · 滚轮/+−=缩放 · 边角=改大小 · 比例已锁定"
        };

        _canvas.Paint += OnPaint;
        _canvas.MouseDown += OnMouseDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseUp += (_, _) => _dragging = false;
        _canvas.MouseWheel += OnWheel;
        _canvas.Resize += (_, _) => { RecalcScale(); _canvas.Invalidate(); };
        typeof(Panel).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_canvas, true);

        Controls.Add(_canvas);
        Controls.Add(hint);
        Controls.Add(bar);

        InitCrop();
    }

    private void CenterCrop()
    {
        _crop = new RectangleF((_src.Width - _crop.Width) / 2, (_src.Height - _crop.Height) / 2, _crop.Width, _crop.Height);
        _canvas.Invalidate();
    }

    private void ZoomCrop(float factor)
    {
        float cx = _crop.X + _crop.Width / 2, cy = _crop.Y + _crop.Height / 2;
        float nw = _crop.Width * factor, nh = (float)(nw / _aspect);
        if (nw < 20 || nw > _src.Width || nh > _src.Height) return;
        float nx = Clamp(cx - nw / 2, 0, _src.Width - nw);
        float ny = Clamp(cy - nh / 2, 0, _src.Height - nh);
        _crop = new RectangleF(nx, ny, nw, nh);
        _canvas.Invalidate();
    }

    private void MoveCrop(float dx, float dy)
    {
        float nx = Clamp(_crop.X + dx, 0, _src.Width - _crop.Width);
        float ny = Clamp(_crop.Y + dy, 0, _src.Height - _crop.Height);
        _crop = new RectangleF(nx, ny, _crop.Width, _crop.Height);
        _canvas.Invalidate();
    }

    private void OnKey(object s, KeyEventArgs e)
    {
        float step = e.Shift ? 20 : 5;
        switch (e.KeyCode)
        {
            case Keys.Left: MoveCrop(-step, 0); break;
            case Keys.Right: MoveCrop(step, 0); break;
            case Keys.Up: MoveCrop(0, -step); break;
            case Keys.Down: MoveCrop(0, step); break;
            case Keys.Oemplus: case Keys.Add: ZoomCrop(0.9f); break;
            case Keys.OemMinus: case Keys.Subtract: ZoomCrop(1.111f); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>初始裁切框: 在原图内取最大的、符合目标比例的居中矩形。</summary>
    private void InitCrop()
    {
        float iw = _src.Width, ih = _src.Height;
        float cw, ch;
        if (iw / ih > _aspect) { ch = ih; cw = (float)(ih * _aspect); }
        else { cw = iw; ch = (float)(iw / _aspect); }
        _crop = new RectangleF((iw - cw) / 2, (ih - ch) / 2, cw, ch);
    }

    private void RecalcScale()
    {
        if (_canvas.ClientSize.Width <= 0) return;
        _scale = Math.Min((float)_canvas.ClientSize.Width / _src.Width,
                          (float)_canvas.ClientSize.Height / _src.Height);
        _imgOrigin = new PointF(
            (_canvas.ClientSize.Width - _src.Width * _scale) / 2,
            (_canvas.ClientSize.Height - _src.Height * _scale) / 2);
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); RecalcScale(); _canvas.Invalidate(); }

    private PointF ToScreen(PointF img) => new(_imgOrigin.X + img.X * _scale, _imgOrigin.Y + img.Y * _scale);
    private PointF ToImage(Point scr) => new((scr.X - _imgOrigin.X) / _scale, (scr.Y - _imgOrigin.Y) / _scale);

    private void OnPaint(object s, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(_src, _imgOrigin.X, _imgOrigin.Y, _src.Width * _scale, _src.Height * _scale);

        var sc = ToScreen(new PointF(_crop.X, _crop.Y));
        var sz = new SizeF(_crop.Width * _scale, _crop.Height * _scale);
        var r = new RectangleF(sc, sz);

        // 框外压暗
        using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            var full = _canvas.ClientRectangle;
            var reg = new Region(full);
            reg.Exclude(Rectangle.Round(r));
            g.FillRegion(dim, reg);
        }
        // 框 + 三分线 + 角柄
        using var pen = new Pen(Theme.Accent, 2f);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        using var thin = new Pen(Color.FromArgb(120, 255, 255, 255), 1f);
        for (int i = 1; i <= 2; i++)
        {
            g.DrawLine(thin, r.X + r.Width * i / 3, r.Y, r.X + r.Width * i / 3, r.Bottom);
            g.DrawLine(thin, r.X, r.Y + r.Height * i / 3, r.Right, r.Y + r.Height * i / 3);
        }
        using var hb = new SolidBrush(Theme.Accent);
        foreach (var c in Corners(r)) g.FillRectangle(hb, c.X - 5, c.Y - 5, 10, 10);
    }

    private static PointF[] Corners(RectangleF r) => new[]
    {
        new PointF(r.X, r.Y), new PointF(r.Right, r.Y),
        new PointF(r.X, r.Bottom), new PointF(r.Right, r.Bottom)
    };

    private int _activeCorner = -1;
    private void OnMouseDown(object s, MouseEventArgs e)
    {
        var sc = ToScreen(new PointF(_crop.X, _crop.Y));
        var r = new RectangleF(sc, new SizeF(_crop.Width * _scale, _crop.Height * _scale));
        _activeCorner = -1;
        var cs = Corners(r);
        for (int i = 0; i < 4; i++)
            if (Math.Abs(e.X - cs[i].X) < 10 && Math.Abs(e.Y - cs[i].Y) < 10) { _activeCorner = i; break; }

        _dragging = true;
        _dragStart = e.Location;
        _cropStart = _crop;
    }

    private void OnMouseMove(object s, MouseEventArgs e)
    {
        if (!_dragging) return;
        float dxImg = (e.X - _dragStart.X) / _scale, dyImg = (e.Y - _dragStart.Y) / _scale;

        if (_activeCorner < 0)
        {
            // 移动整个框
            float nx = Clamp(_cropStart.X + dxImg, 0, _src.Width - _crop.Width);
            float ny = Clamp(_cropStart.Y + dyImg, 0, _src.Height - _crop.Height);
            _crop = new RectangleF(nx, ny, _crop.Width, _crop.Height);
        }
        else
        {
            // 缩放 (保持比例, 以对角为锚)
            float anchorX = (_activeCorner is 0 or 2) ? _cropStart.Right : _cropStart.Left;
            float anchorY = (_activeCorner is 0 or 1) ? _cropStart.Bottom : _cropStart.Top;
            float curX = (_activeCorner is 0 or 2) ? _cropStart.X + dxImg : _cropStart.Right + dxImg;
            float newW = Math.Abs(anchorX - curX);
            float newH = (float)(newW / _aspect);
            newW = Math.Max(20, newW); newH = Math.Max(20, newH);
            float x = Math.Min(anchorX, anchorX - newW * Math.Sign(anchorX - curX == 0 ? 1 : anchorX - curX));
            // 简化: 以锚点为固定角重建
            float left = (_activeCorner is 1 or 3) ? anchorX : anchorX - newW;
            float top = (_activeCorner is 2 or 3) ? anchorY : anchorY - newH;
            var rect = new RectangleF(left, top, newW, newH);
            if (rect.X >= 0 && rect.Y >= 0 && rect.Right <= _src.Width && rect.Bottom <= _src.Height)
                _crop = rect;
        }
        _canvas.Invalidate();
    }

    private void OnWheel(object s, MouseEventArgs e)
    {
        float factor = e.Delta > 0 ? 0.92f : 1.08f;
        float cx = _crop.X + _crop.Width / 2, cy = _crop.Y + _crop.Height / 2;
        float nw = _crop.Width * factor, nh = (float)(nw / _aspect);
        if (nw < 20 || nw > _src.Width || nh > _src.Height) return;
        float nx = Clamp(cx - nw / 2, 0, _src.Width - nw);
        float ny = Clamp(cy - nh / 2, 0, _src.Height - nh);
        _crop = new RectangleF(nx, ny, nw, nh);
        _canvas.Invalidate();
    }

    private static float Clamp(float v, float lo, float hi) => Math.Max(lo, Math.Min(hi, v));

    private void DoCrop()
    {
        try
        {
            using var img = Img.Load<Rgba32>(_srcPath);
            var rect = new IRect(
                (int)Math.Round(_crop.X), (int)Math.Round(_crop.Y),
                (int)Math.Round(_crop.Width), (int)Math.Round(_crop.Height));
            rect.X = Math.Max(0, rect.X); rect.Y = Math.Max(0, rect.Y);
            rect.Width = Math.Min(rect.Width, img.Width - rect.X);
            rect.Height = Math.Min(rect.Height, img.Height - rect.Y);

            img.Mutate(c => c.Crop(rect).Resize(_targetW, _targetH));
            using var ms = new MemoryStream();
            img.SaveAsPng(ms);
            ResultPng = ms.ToArray();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"裁切失败：{ex.Message}", "错误");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _src?.Dispose();
        base.Dispose(disposing);
    }
}
