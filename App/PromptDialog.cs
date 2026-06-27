using System.Drawing;
using System.Windows.Forms;

namespace JixModMaker;

/// <summary>深色输入对话框 (自适应大小, 高 DPI 下文字不截断)。</summary>
public static class PromptDialog
{
    public static string Show(IWin32Window owner, string title, string label, string initial = "")
    {
        var f = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false,
            BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.UI(10f),
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18, 16, 18, 14),
            MinimumSize = new Size(420, 0)
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg
        };

        var lbl = new Label
        {
            Text = label, AutoSize = true, ForeColor = Theme.Text,
            MaximumSize = new Size(440, 0), Margin = new Padding(0, 0, 0, 10)
        };
        var box = new TextBox
        {
            Text = initial, Width = 400, BackColor = Theme.Card, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 12),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
            Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0)
        };
        var ok = Theme.FlatButton("确定"); ok.MinimumSize = new Size(80, 0); ok.DialogResult = DialogResult.OK;
        var cancel = Theme.FlatButton("取消"); cancel.MinimumSize = new Size(80, 0); cancel.DialogResult = DialogResult.Cancel;
        cancel.Margin = new Padding(8, 0, 0, 0);
        btnPanel.Controls.Add(ok);
        btnPanel.Controls.Add(cancel);

        table.Controls.Add(lbl, 0, 0);
        table.Controls.Add(box, 0, 1);
        table.Controls.Add(btnPanel, 0, 2);
        f.Controls.Add(table);
        f.AcceptButton = ok; f.CancelButton = cancel;
        box.SelectAll();

        return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}
