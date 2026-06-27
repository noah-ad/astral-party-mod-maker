using System.Drawing;
using System.Windows.Forms;

namespace JixModMaker;

/// <summary>一个简单的深色输入对话框 (WinForms 无内置 InputBox)。</summary>
public static class PromptDialog
{
    public static string Show(IWin32Window owner, string title, string label, string initial = "")
    {
        var f = new Form
        {
            Text = title, Width = 400, Height = 170, FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false,
            BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.UI(10f)
        };
        var lbl = new Label { Text = label, Left = 16, Top = 16, Width = 360, ForeColor = Theme.Text };
        var box = new TextBox
        {
            Left = 16, Top = 44, Width = 356, Text = initial,
            BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle
        };
        var ok = Theme.FlatButton("确定", 90); ok.Left = 186; ok.Top = 84; ok.DialogResult = DialogResult.OK;
        var cancel = Theme.FlatButton("取消", 90); cancel.Left = 282; cancel.Top = 84; cancel.DialogResult = DialogResult.Cancel;
        f.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        box.SelectAll();

        return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}
