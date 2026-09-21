namespace JixModMaker;

public sealed record VideoCrop(double X, double Y, double Width, double Height)
{
    public static VideoCrop Fit(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, double zoom = 1,
        double centerX = .5, double centerY = .5)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0 || !double.IsFinite(zoom))
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        double ratio = targetWidth / (double)targetHeight / (sourceWidth / (double)sourceHeight);
        double width = Math.Min(1, ratio) / Math.Clamp(zoom, 1, 4);
        double height = Math.Min(1, 1 / ratio) / Math.Clamp(zoom, 1, 4);
        return new(Math.Clamp(centerX - width / 2, 0, 1 - width), Math.Clamp(centerY - height / 2, 0, 1 - height), width, height);
    }

    public VideoCrop Move(double x, double y) => this with { X = Math.Clamp(x, 0, 1 - Width), Y = Math.Clamp(y, 0, 1 - Height) };

    public string Filter()
    {
        if (!new[] { X, Y, Width, Height }.All(double.IsFinite) || X < 0 || Y < 0 || Width <= 0 || Height <= 0 || X + Width > 1.000001 || Y + Height > 1.000001)
            throw new InvalidDataException("取景范围超出视频边界。");
        return FormattableString.Invariant($"crop=w='max(2,round(iw*{Width:0.########}))':h='max(2,round(ih*{Height:0.########}))':x='round(iw*{X:0.########})':y='round(ih*{Y:0.########})':exact=1");
    }
}

public sealed class VideoCropControl : Control
{
    private Image _image;
    private Size _target = new(1, 1);
    private float _zoom = 1;
    private Point? _dragStart;
    private VideoCrop _dragCrop;
    public VideoCrop Crop { get; private set; }
    public event EventHandler CropChanged;
    public event EventHandler CropCommitted;
    public VideoCropControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.PicBg;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = "视频取景范围";
    }
    public void SetSource(Image image, Size target)
    {
        _image = image;
        _target = target;
        _zoom = 1;
        Crop = VideoCrop.Fit(image.Width, image.Height, target.Width, target.Height);
        Invalidate();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SetZoom(float zoom)
    {
        if (_image == null) return;
        _zoom = Math.Clamp(zoom, 1, 4);
        Crop = VideoCrop.Fit(_image.Width, _image.Height, _target.Width, _target.Height, _zoom, Crop.X + Crop.Width / 2, Crop.Y + Crop.Height / 2);
        Invalidate();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }
    public void CenterCrop()
    {
        if (_image == null) return;
        Crop = VideoCrop.Fit(_image.Width, _image.Height, _target.Width, _target.Height, _zoom);
        Invalidate();
        CropChanged?.Invoke(this, EventArgs.Empty);
        CropCommitted?.Invoke(this, EventArgs.Empty);
    }
    private RectangleF ImageBounds()
    {
        if (_image == null) return RectangleF.Empty;
        float scale = Math.Max(.001f, Math.Min((ClientSize.Width - 24f) / _image.Width, (ClientSize.Height - 24f) / _image.Height));
        return new((ClientSize.Width - _image.Width * scale) / 2, (ClientSize.Height - _image.Height * scale) / 2, _image.Width * scale, _image.Height * scale);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image == null || Crop == null) return;
        var bounds = ImageBounds();
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_image, bounds);
        var frame = new RectangleF(bounds.X + (float)Crop.X * bounds.Width, bounds.Y + (float)Crop.Y * bounds.Height,
            (float)Crop.Width * bounds.Width, (float)Crop.Height * bounds.Height);
        using var shade = new SolidBrush(Color.FromArgb(155, 5, 8, 20));
        using var mask = new Region(bounds);
        mask.Exclude(frame);
        e.Graphics.FillRegion(shade, mask);
        using var border = new Pen(Theme.Cyan, 2 * DeviceDpi / 96f);
        e.Graphics.DrawRectangle(border, frame.X, frame.Y, frame.Width, frame.Height);
        using var grid = new Pen(Color.FromArgb(120, Color.White));
        for (int i = 1; i <= 2; i++)
        {
            e.Graphics.DrawLine(grid, frame.X + frame.Width * i / 3, frame.Y, frame.X + frame.Width * i / 3, frame.Bottom);
            e.Graphics.DrawLine(grid, frame.X, frame.Y + frame.Height * i / 3, frame.Right, frame.Y + frame.Height * i / 3);
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || _image == null || e.Button != MouseButtons.Left || !ImageBounds().Contains(e.Location)) return;
        Focus();
        Capture = true;
        _dragStart = e.Location;
        _dragCrop = Crop;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not Point start) return;
        var bounds = ImageBounds();
        Crop = _dragCrop.Move(_dragCrop.X + (e.X - start.X) / bounds.Width, _dragCrop.Y + (e.Y - start.Y) / bounds.Height);
        Invalidate();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragStart == null) return;
        _dragStart = null;
        Capture = false;
        CropCommitted?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Crop == null) return;
        double dx = e.KeyCode == Keys.Left ? -.01 : e.KeyCode == Keys.Right ? .01 : 0;
        double dy = e.KeyCode == Keys.Up ? -.01 : e.KeyCode == Keys.Down ? .01 : 0;
        if (dx == 0 && dy == 0) return;
        Crop = Crop.Move(Crop.X + dx, Crop.Y + dy);
        Invalidate();
        CropChanged?.Invoke(this, EventArgs.Empty);
        CropCommitted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
}
