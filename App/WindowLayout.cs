namespace JixModMaker;

internal static class WindowLayout
{
    public static void Configure(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Shown += (_, _) => FitToWorkingArea(form);
        form.DpiChanged += (_, _) => form.BeginInvoke(() =>
        {
            if (!form.IsDisposed) FitToWorkingArea(form);
        });
    }

    public static void FitToWorkingArea(Form form)
    {
        var area = Screen.FromControl(form).WorkingArea;
        form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width), Math.Min(form.MinimumSize.Height, area.Height));
        if (form.WindowState != FormWindowState.Normal) return;
        var size = new Size(Math.Min(form.Width, area.Width), Math.Min(form.Height, area.Height));
        form.Bounds = new Rectangle(
            Math.Clamp(form.Left, area.Left, area.Right - size.Width),
            Math.Clamp(form.Top, area.Top, area.Bottom - size.Height), size.Width, size.Height);
    }
}
