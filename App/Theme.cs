using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace JixModMaker;

/// <summary>全局视觉规范: 配色 / 字体 / 圆角卡片 / 扁平按钮。后续新 UI 统一复用。</summary>
public static class Theme
{
    public static readonly Color Bg        = Color.FromArgb(24, 24, 28);
    public static readonly Color FlowBg    = Color.FromArgb(32, 32, 38);
    public static readonly Color Bar       = Color.FromArgb(38, 38, 44);
    public static readonly Color Card      = Color.FromArgb(50, 50, 58);
    public static readonly Color CardMod   = Color.FromArgb(38, 92, 50);
    public static readonly Color Accent    = Color.FromArgb(82, 132, 212);
    public static readonly Color AccentDim = Color.FromArgb(60, 92, 150);
    public static readonly Color Text      = Color.FromArgb(228, 228, 232);
    public static readonly Color SubText   = Color.FromArgb(150, 150, 160);
    public static readonly Color PicBg     = Color.FromArgb(28, 28, 32);

    public static Font UI(float size, bool bold = false)
        => new("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>创建一个扁平风按钮 (hover 强调色)。</summary>
    public static Button FlatButton(string text, int width = 0)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Text,
            BackColor = Card,
            Font = UI(10f),
            Height = 34,
            AutoSize = width == 0,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Padding = new Padding(12, 0, 12, 0)
        };
        if (width > 0) b.Width = width;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Accent;
        b.FlatAppearance.MouseDownBackColor = AccentDim;
        return b;
    }
}

/// <summary>圆角卡片控件 (双缓冲, 自绘圆角填充 + 边框)。</summary>
public class RoundedCard : Panel
{
    public int Radius { get; set; } = 10;
    public Color Fill { get; set; } = Theme.Card;
    public Color BorderColor { get; set; } = Color.Transparent;

    public RoundedCard()
    {
        DoubleBuffered = true;
        BackColor = Theme.FlowBg; // 四角露出的底色
    }

    public void SetFill(Color c) { Fill = c; Invalidate(); }
    public void SetBorder(Color c) { BorderColor = c; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle; r.Width -= 1; r.Height -= 1;
        using var path = Theme.RoundRect(r, Radius);
        using var brush = new SolidBrush(Fill);
        g.FillPath(brush, path);
        if (BorderColor.A > 0)
        {
            using var pen = new Pen(BorderColor, 2f);
            g.DrawPath(pen, path);
        }
    }
}
