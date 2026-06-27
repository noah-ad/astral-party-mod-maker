using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace JixModMaker;

/// <summary>首次启动提示: 本工具免费开源, 谨防被倒卖付费。</summary>
public static class NoticeDialog
{
    public const string Url = "https://github.com/noah-ad/astral-party-mod-maker";

    public static void ShowIfFirst(IWin32Window owner)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker");
            Directory.CreateDirectory(dir);
            string flag = Path.Combine(dir, ".noticed");
            if (File.Exists(flag)) return;
            Show(owner);
            File.WriteAllText(flag, DateTime.Now.ToString());
        }
        catch { }
    }

    public static void Show(IWin32Window owner)
    {
        var f = new Form
        {
            Text = "重要提示 — 本工具免费开源",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false,
            BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.UI(10f),
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(22, 18, 22, 16), MinimumSize = new Size(470, 0)
        };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Theme.Bg };

        var t1 = new Label { Text = "✅ 本工具完全免费 · 开源项目", AutoSize = true, ForeColor = Theme.Accent, Font = Theme.UI(13f, true), Margin = new Padding(0, 0, 0, 12) };
        var t2 = new Label { Text = "「吉星立绘 Mod 制作器」是免费开源工具，唯一官方发布地址：", AutoSize = true, ForeColor = Theme.Text, MaximumSize = new Size(450, 0), Margin = new Padding(0, 0, 0, 6) };
        var link = new LinkLabel { Text = Url, AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Color.White, Margin = new Padding(0, 0, 0, 14) };
        link.LinkClicked += (_, _) => OpenUrl();
        var warn = new Label
        {
            Text = "⚠️ 请勿从任何第三方渠道付费购买本工具！\n凡是收费售卖的，都是盗用倒卖，谨防上当受骗。\n本工具及其后续更新，永远只在上方 GitHub 免费发布。",
            AutoSize = true, ForeColor = Color.FromArgb(255, 184, 120), MaximumSize = new Size(450, 0), Margin = new Padding(0, 0, 0, 16)
        };

        var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var ok = Theme.FlatButton("我知道了"); ok.MinimumSize = new Size(96, 0); ok.DialogResult = DialogResult.OK;
        var gh = Theme.FlatButton("打开 GitHub 项目"); gh.MinimumSize = new Size(130, 0); gh.Margin = new Padding(8, 0, 0, 0); gh.Click += (_, _) => OpenUrl();
        btnPanel.Controls.Add(ok); btnPanel.Controls.Add(gh);

        table.Controls.Add(t1); table.Controls.Add(t2); table.Controls.Add(link); table.Controls.Add(warn); table.Controls.Add(btnPanel);
        f.Controls.Add(table);
        f.AcceptButton = ok;
        f.ShowDialog(owner);
    }

    private static void OpenUrl()
    {
        try { Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true }); }
        catch { }
    }
}
