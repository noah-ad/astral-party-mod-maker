using System.Drawing.Drawing2D;

namespace JixModMaker;

internal static class WorkshopArt
{
    // Use the current index to resolve art by name, as bundle hashes change between game versions.
    public static byte[] Read(GameIndex index, string folder)
    {
        IEnumerable<string> names = index.Lightweight
            ? index.TextureNamesByBundle.Values.SelectMany(list => list)
            : index.Items.Where(item => item.Kind == ResourceKinds.Texture).Select(item => item.Name);
        var alternates = names.Where(name => name?.StartsWith("UT_AccountBackground_", StringComparison.Ordinal) == true)
            .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).Take(4);
        return Decode(index, folder, new[] { "UT_AccountBackground_101_01", "UT_AccountBackground_101_01_sfw" }.Concat(alternates).Distinct(), 1100);
    }

    public static byte[] ReadBadge(GameIndex index, string folder)
        => Decode(index, folder, new[] { "UT_Item_Dice_72001", "UT_Item_Dice_72002" }, 128);

    private static byte[] Decode(GameIndex index, string folder, IEnumerable<string> names, int limit)
    {
        var engine = new ModEngine();
        foreach (string name in names)
        foreach (var item in index.FindTextureTargets(name).OrderBy(it => IndexService.SourcePriority(it.Source)).Take(3))
        {
            try
            {
                string path = string.IsNullOrEmpty(item.BundlePath) ? Path.Combine(folder, item.Bundle) : item.BundlePath;
                if (!File.Exists(path)) continue;
                var texture = engine.FindTextureByName(path, name);
                if (texture != null) return engine.DecodePng(path, texture.PathId, limit);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        return null;
    }
}

internal sealed class GameArtHeading : Label
{
    public Image Art { get; set; }
    public Image Badge { get; set; }
    public GameArtHeading()
    {
        AutoSize = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        ForeColor = Theme.Text;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int w = Width, h = Height;
        if (w < 2 || h < 2) return;
        var g = e.Graphics;
        using var bg = new SolidBrush(BackColor);
        g.FillRectangle(bg, ClientRectangle);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        int x = Math.Max(2, Padding.Left);
        if (Art != null && !SystemInformation.HighContrast)
        {
            int aw = Math.Min(w, Math.Max(1, (int)(h * (float)Art.Width / Art.Height)));
            var dest = new Rectangle(w - aw, 0, aw, h);
            float scale = Math.Max(aw / (float)Art.Width, h / (float)Art.Height);
            float sw = aw / scale, sh = h / scale;
            g.DrawImage(Art, dest, (Art.Width - sw) / 2, (Art.Height - sh) / 2, sw, sh, GraphicsUnit.Pixel);
            int fade = Math.Min(aw, Math.Max(aw / 2, TextRenderer.MeasureText(Text, Font).Width + x));
            using var shade = new LinearGradientBrush(new Rectangle(dest.Left - 1, 0, fade + 1, h),
                BackColor, Color.FromArgb(4, BackColor), 0f);
            g.FillRectangle(shade, dest.Left, 0, fade, h);
        }
        if (Badge != null && w > Font.Height * 4)
        {
            int side = Math.Min(h - 2, Font.Height + LogicalToDeviceUnits(10));
            g.DrawImage(Badge, x, (h - side) / 2, side, side);
            x += side + LogicalToDeviceUnits(5);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Math.Max(1, w - x), h), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

internal sealed class WorkshopBanner : BufferedPanel
{
    private readonly Font _title = Theme.UI(23, true);
    private readonly Font _stats = Theme.UI(10);
    private readonly Func<Image> _art;
    private readonly Func<string> _summary;
    public WorkshopBanner(Func<Image> art, Func<string> summary)
    {
        _art = art;
        _summary = summary;
        Tag = "page_card";
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = UiText.T("吉星派对");
        BackColor = Theme.FlowBg;
        Margin = new Padding(6, 4, 6, 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var r = ClientRectangle;
        if (r.Width < 2 || r.Height < 2) return;
        var g = e.Graphics;
        int pad = Math.Max(12, _stats.Height);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), pad);
        var state = g.Save();
        g.SetClip(shape, CombineMode.Intersect);
        var ink = Theme.Bar;
        using var baseBrush = new SolidBrush(ink);
        g.FillRectangle(baseBrush, r);
        var art = _art();
        var text = _summary();
        int textWidth = Math.Min(Width - 2 * pad, Math.Max(Width / 3, TextRenderer.MeasureText(text, _stats).Width));
        int artStart = Math.Min(Width - pad, pad + textWidth + pad);
        int aw = Width - artStart;
        if (aw > pad)
        {
            using var backdrop = new SolidBrush(Theme.CardAlt);
            g.FillRectangle(backdrop, artStart, 0, aw, Height);
            if (art != null && art.Width >= art.Height * 1.55)
            {
                float zoom = Math.Max((float)aw / art.Width, (float)Height / art.Height);
                float w = aw / zoom, h = Height / zoom;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(art, new Rectangle(artStart, 0, aw, Height), (art.Width - w) / 2, (art.Height - h) / 2, w, h, GraphicsUnit.Pixel);
            }
            else if (art != null)
            {
                using var stripe = new SolidBrush(Theme.CardMod);
                g.FillPolygon(stripe, new[] { new Point(Width - aw / 2, 0), new Point(Width, 0),
                    new Point(Width, Height), new Point(Width - aw * 2 / 3, Height) });
                float scale = Math.Min((float)(Height - 2 * pad) / art.Height, (float)aw / art.Width);
                int w = Math.Max(1, (int)(art.Width * scale)), h = Math.Max(1, (int)(art.Height * scale));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(art, artStart + (aw - w) / 2, (Height - h) / 2, w, h);
            }
            int blend = Math.Max(2, Math.Min(aw, pad * 4));
            using var edge = new LinearGradientBrush(new Rectangle(artStart - 1, 0, blend + 1, Height), ink, Color.FromArgb(0, ink), 0f);
            g.FillRectangle(edge, artStart - 1, 0, blend, Height);
        }
        var titleBounds = new Rectangle(pad, Math.Max(0, Height / 2 - _title.Height), Math.Max(1, textWidth), _title.Height + pad / 2);
        TextRenderer.DrawText(g, UiText.T("吉星派对"), _title, titleBounds, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, text, _stats, new Rectangle(pad, titleBounds.Bottom, Math.Max(1, textWidth + pad / 2), Math.Max(1, Height - titleBounds.Bottom)),
            Color.FromArgb(211, 235, 235), TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        g.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _title.Dispose(); _stats.Dispose(); }
        base.Dispose(disposing);
    }
}
