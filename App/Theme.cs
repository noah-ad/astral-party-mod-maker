using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace JixModMaker;

/// <summary>全局视觉规范: 取自应用图标的深海军蓝 / 冰蓝 / 亮粉 / 金色。</summary>
public static class Theme
{
    public static readonly Color Bg         = Color.FromArgb(18, 18, 45);
    public static readonly Color FlowBg     = Color.FromArgb(9, 17, 43);
    public static readonly Color Bar        = Color.FromArgb(27, 27, 62);
    public static readonly Color Card       = Color.FromArgb(35, 36, 78);
    public static readonly Color CardAlt    = Color.FromArgb(45, 49, 99);
    public static readonly Color CardMod    = Color.FromArgb(76, 51, 109);
    public static readonly Color Accent     = Color.FromArgb(255, 77, 151);
    public static readonly Color AccentSoft = Color.FromArgb(255, 135, 185);
    public static readonly Color AccentDim  = Color.FromArgb(124, 107, 226);
    public static readonly Color Cyan       = Color.FromArgb(105, 215, 241);
    public static readonly Color CyanDim    = Color.FromArgb(35, 73, 114);
    public static readonly Color Warn       = Color.FromArgb(255, 211, 122);
    public static readonly Color Good       = Color.FromArgb(91, 225, 181);
    public static readonly Color Danger     = Color.FromArgb(255, 102, 126);
    public static readonly Color Text       = Color.FromArgb(250, 248, 255);
    public static readonly Color SubText    = Color.FromArgb(202, 202, 234);
    public static readonly Color Muted      = Color.FromArgb(113, 119, 166);
    public static readonly Color PicBg      = Color.FromArgb(8, 22, 53);
    public static readonly Color Line       = Color.FromArgb(63, 71, 122);
    public static readonly Color Surface    = Color.FromArgb(30, 37, 78);

    public static Font UI(float size, bool bold = false)
        => new("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);

    public static Font Mono(float size, bool bold = false)
        => new("Consolas", size, bold ? FontStyle.Bold : FontStyle.Regular);

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        radius = Math.Max(1, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
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
        var b = new RoundButton
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Text,
            BackColor = Card,
            Font = UI(9.5f),
            Height = 34,
            AutoSize = width == 0,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Padding = new Padding(12, 0, 12, 0),
            Radius = 12,
            BorderColor = Line,
            HoverBackColor = CyanDim,
            DownBackColor = AccentDim
        };
        if (width > 0) b.Width = width;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Muted;
        b.FlatAppearance.MouseOverBackColor = AccentDim;
        b.FlatAppearance.MouseDownBackColor = AccentDim;
        return b;
    }

    public static Label Caption(string text, bool strong = false) => new()
    {
        Text = text,
        ForeColor = strong ? Accent : SubText,
        BackColor = Color.Transparent,
        Font = strong ? UI(10f, true) : UI(9f),
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft
    };
}

/// <summary>自绘圆角按钮。WinForms 原生 Button 在高 DPI 下边框太硬，这里统一绘制。</summary>
public class RoundButton : Button
{
    private bool _hover;
    private bool _down;
    private int _radius = 12;

    public int Radius
    {
        get => _radius;
        set
        {
            _radius = Math.Max(2, value);
            UpdateRoundedRegion();
            Invalidate();
        }
    }
    public Color BorderColor { get; set; } = Theme.Line;
    public Color HoverBackColor { get; set; } = Theme.CardAlt;
    public Color DownBackColor { get; set; } = Theme.AccentDim;
    public Color GradientColor { get; set; } = Color.Transparent;
    public Color IndicatorColor { get; set; } = Color.Transparent;

    public RoundButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRoundedRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedRegion();
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = Theme.RoundRect(new Rectangle(0, 0, Width, Height), _radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    private Color ParentSurfaceColor()
    {
        Control owner = Parent;
        while (owner != null)
        {
            if (owner is RoundedCard card)
                return card.GradientFill.A > 0 ? card.GradientFill : card.Fill;
            if (owner is GradientPanel gradient)
                return gradient.GradientEnd;
            if (owner.BackColor.A == 255)
                return owner.BackColor;
            owner = owner.Parent;
        }
        return Theme.Bg;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
        => pevent.Graphics.Clear(ParentSurfaceColor());

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) _down = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = ClientRectangle;
        r.Width -= 1;
        r.Height -= 1;
        if (r.Width <= 0 || r.Height <= 0) return;

        var fill = Enabled
            ? _down ? DownBackColor : _hover ? HoverBackColor : BackColor
            : Color.FromArgb(45, 38, 58);

        using (var path = Theme.RoundRect(r, Radius))
        {
            if (!_hover && !_down && GradientColor.A > 0)
            {
                using var brush = new LinearGradientBrush(r, fill, GradientColor, LinearGradientMode.Horizontal);
                g.FillPath(brush, path);
            }
            else
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
        }

        if (BorderColor.A > 0)
        {
            using var pen = new Pen(Enabled ? BorderColor : Color.FromArgb(64, Theme.Line), 1f);
            using var path = Theme.RoundRect(r, Radius);
            g.DrawPath(pen, path);
        }

        if (IndicatorColor.A > 0 && r.Height >= 12)
        {
            var indicator = new Rectangle(r.Left + 5, r.Top + 6, 3, Math.Max(6, r.Height - 12));
            using var indicatorPath = Theme.RoundRect(indicator, 2);
            using var indicatorBrush = new SolidBrush(IndicatorColor);
            g.FillPath(indicatorBrush, indicatorPath);
        }

        var textBounds = new Rectangle(
            r.Left + Padding.Left,
            r.Top + Padding.Top,
            Math.Max(1, r.Width - Padding.Horizontal),
            Math.Max(1, r.Height - Padding.Vertical));
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        flags |= TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft
            ? TextFormatFlags.Left
            : TextAlign is ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight
                ? TextFormatFlags.Right
                : TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? ForeColor : Color.FromArgb(120, 112, 140), flags);
    }
}

/// <summary>圆角卡片控件 (双缓冲, 自绘圆角填充 + 边框)。</summary>
public class RoundedCard : Panel
{
    public int Radius { get; set; } = 14;
    public Color Fill { get; set; } = Theme.Card;
    public Color GradientFill { get; set; } = Color.Transparent;
    public Color BorderColor { get; set; } = Theme.Line;

    public RoundedCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
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
        if (GradientFill.A > 0)
        {
            using var brush = new LinearGradientBrush(r, Fill, GradientFill, LinearGradientMode.Vertical);
            g.FillPath(brush, path);
        }
        else
        {
            using var brush = new SolidBrush(Fill);
            g.FillPath(brush, path);
        }
        if (BorderColor.A > 0)
        {
            using var pen = new Pen(BorderColor, 1f);
            g.DrawPath(pen, path);
        }
    }
}

public class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
    }
}

public class BufferedFlowPanel : FlowLayoutPanel
{
    public BufferedFlowPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
    }
}

public class GradientPanel : BufferedPanel
{
    public Color GradientStart { get; set; } = Theme.Bar;
    public Color GradientEnd { get; set; } = Theme.Card;
    public float GradientAngle { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using var brush = new LinearGradientBrush(ClientRectangle, GradientStart, GradientEnd, GradientAngle);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}
