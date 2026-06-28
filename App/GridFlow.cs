using System.Drawing;
using System.Windows.Forms;

namespace JixModMaker;

/// <summary>
/// 缩略图网格容器。修复 FlowLayoutPanel + AutoScroll 在窄窗口/高DPI 下
/// 不换行、改为横向滚动的问题：每次布局前把整行分组标题(Tag="header")
/// 精确缩到「客户区宽 - 内边距 - 标题外边距」，使任何子项都不会超出客户区宽度，
/// 从而永远不触发横向滚动条，卡片始终按当前可见宽度自动换行。
/// </summary>
public sealed class GridFlow : FlowLayoutPanel
{
    public GridFlow()
    {
        AutoScroll = true;
        WrapContents = true;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        // 客户区已自动扣除竖向滚动条宽度; 标题撑满该宽度但绝不超出
        int inner = ClientSize.Width - Padding.Horizontal;
        foreach (Control c in Controls)
        {
            if ((c.Tag as string) != "header") continue;
            int target = inner - c.Margin.Horizontal;
            if (target < 80) target = 80;
            if (c.Width != target) c.Width = target;   // 在 base 布局前改好, 让换行按正确宽度计算
        }
        base.OnLayout(e);
    }
}
