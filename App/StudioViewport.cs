using System.Runtime.InteropServices;

namespace JixModMaker;

internal static class StudioMotion
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int enabled, uint flags);

    public static bool Enabled => !SystemInformation.HighContrast && !SystemInformation.TerminalServerSession
        && SystemParametersInfo(0x1042, 0, out int enabled, 0) && enabled != 0; // SPI_GETCLIENTAREAANIMATION
}

// Only animate the navigation marker. Moving a scrolled tree of child HWNDs blits stale pixels.
internal sealed class StudioViewport : BufferedPanel
{
    private Control _active;

    public StudioViewport()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.FlowBg;
    }

    public void Reveal(Control page, Control previous)
    {
        if (previous != null && previous != page) previous.Visible = false;
        _active = page;
        page.Bounds = ClientRectangle;
        page.BringToFront();
        page.Visible = true;
        page.Invalidate(true);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_active != null) _active.Bounds = ClientRectangle;
    }
}
