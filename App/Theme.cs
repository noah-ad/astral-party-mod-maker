using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace JixModMaker;

public static class Theme
{
    public static readonly Color Bg         = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(31, 30, 39);
    public static readonly Color FlowBg     = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(23, 23, 30);
    public static readonly Color Bar        = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(34, 32, 43);
    public static readonly Color Card       = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(42, 40, 51);
    public static readonly Color CardAlt    = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(51, 46, 61);
    public static readonly Color CardMod    = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(62, 42, 58);
    public static readonly Color Accent     = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(249, 153, 181);
    public static readonly Color AccentSoft = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(255, 184, 204);
    public static readonly Color AccentDim  = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(97, 66, 88);
    public static readonly Color OnAccent   = SystemInformation.HighContrast ? SystemColors.HighlightText : Color.FromArgb(50, 25, 38);
    public static readonly Color Cyan       = SystemInformation.HighContrast ? SystemColors.HotTrack : Color.FromArgb(131, 215, 205);
    public static readonly Color CyanDim    = SystemInformation.HighContrast ? SystemColors.ControlLight : Color.FromArgb(51, 75, 77);
    public static readonly Color Warn       = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(244, 202, 130);
    public static readonly Color Good       = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(145, 217, 174);
    public static readonly Color Danger     = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(255, 159, 148);
    public static readonly Color Text       = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(246, 240, 245);
    public static readonly Color SubText    = SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(202, 193, 208);
    public static readonly Color Muted      = SystemInformation.HighContrast ? SystemColors.GrayText : Color.FromArgb(165, 155, 176);
    public static readonly Color PicBg      = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(29, 29, 36);
    public static readonly Color Line       = SystemInformation.HighContrast ? SystemColors.ControlDark : Color.FromArgb(75, 69, 83);
    public static readonly Color Surface    = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(53, 49, 62);

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
            Invalidate();
        }
    }
    public Color BorderColor { get; set; } = Theme.Line;
    public Color HoverBackColor { get; set; } = Theme.CardAlt;
    public Color DownBackColor { get; set; } = Theme.AccentDim;
    public Color GradientColor { get; set; } = Color.Transparent;
    public Color IndicatorColor { get; set; } = Color.Transparent;
    public string Symbol { get; set; }

    public RoundButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
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

    // ButtonBase can skip WM_ERASEBKGND. Every frame must include the pixels outside the curve.
    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) _down = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        using (var surface = new SolidBrush(ParentSurfaceColor())) g.FillRectangle(surface, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = ClientRectangle;
        r.Width -= 1;
        r.Height -= 1;
        if (r.Width <= 0 || r.Height <= 0) return;

        var fill = Enabled
            ? _down ? DownBackColor : _hover ? HoverBackColor : BackColor
            : Theme.Surface;

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
        var ink = Enabled ? ForeColor : Theme.Muted;
        if (Symbol != null)
        {
            using var symbolFont = new Font("Segoe MDL2 Assets", Font.Size);
            int glyph = Font.Height, gap = Math.Max(3, glyph / 3);
            int groupWidth = glyph + gap + TextRenderer.MeasureText(Text, Font).Width;
            if (groupWidth <= textBounds.Width)
            {
                int x = textBounds.X + (textBounds.Width - groupWidth) / 2;
                TextRenderer.DrawText(g, Symbol, symbolFont, new Rectangle(x, textBounds.Y, glyph, textBounds.Height), ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                textBounds = new Rectangle(x + glyph + gap, textBounds.Y, groupWidth - glyph - gap, textBounds.Height);
            }
        }
        TextRenderer.DrawText(g, Text, Font, textBounds, ink, flags);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -5, -4), ForeColor, fill);
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
        g.SmoothingMode = SmoothingMode.None;
        using (var outside = new SolidBrush(BackColor)) g.FillRectangle(outside, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle; r.Width -= 1; r.Height -= 1;
        if (r.Width < 2 || r.Height < 2) return;
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
