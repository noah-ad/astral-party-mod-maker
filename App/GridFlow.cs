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

    /// <summary>
    /// 可用内容宽度。竖向滚动条出现前 ClientSize 还没扣除它, 此时主动预留其宽度,
    /// 避免"先按无滚动条的宽度布局→滚动条冒出来→子项反而超宽→横向滚动条"的时序竞态。
    /// </summary>
    private int InnerWidth()
    {
        int reserve = VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
        return ClientSize.Width - Padding.Horizontal - reserve;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int inner = InnerWidth();
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

/// <summary>
/// 纵向列表容器(左侧角色分组)。每次布局前把所有子项(按钮)精确缩到客户区宽,
/// 这样无论 DPI/竖向滚动条多宽, 都不会因按钮比可视区宽而冒出横向滚动条。
/// </summary>
public sealed class SideListFlow : FlowLayoutPanel
{
    public SideListFlow()
    {
        AutoScroll = true;
        WrapContents = false;
        FlowDirection = FlowDirection.TopDown;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        // 角色列表通常很长、竖向滚动条几乎总在; 滚动条出现前主动预留其宽度, 杜绝横向滚动条
        int reserve = VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
        int inner = ClientSize.Width - Padding.Horizontal - reserve;
        foreach (Control c in Controls)
        {
            int target = inner - c.Margin.Horizontal;
            if (target < 40) target = 40;
            if (c.Width != target) c.Width = target;
        }
        base.OnLayout(e);
    }
}
