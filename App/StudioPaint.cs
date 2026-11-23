using System.Runtime.InteropServices;

namespace JixModMaker;

internal readonly struct StudioPaint : IDisposable
{
    private readonly Control _owner;
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string app, string id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Use only around synchronous layout, on a window which remains visible for the entire scope.
    public StudioPaint(Control owner)
    {
        _owner = owner is { IsHandleCreated: true, Visible: true } ? owner : null;
        if (_owner != null) SendMessage(_owner.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
    }
    public void Dispose()
    {
        if (_owner is not { IsDisposed: false, IsHandleCreated: true }) return;
        SendMessage(_owner.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
        RedrawWindow(_owner.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0080 | 0x0400); // invalidate children and frame
    }
    public static void DarkScroll(Control control)
    {
        if (!SystemInformation.HighContrast && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }
    public static void DarkTitle(Form form)
    {
        if (!SystemInformation.HighContrast && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            int useDark = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref useDark, 4) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref useDark, 4);
        }
    }
}
