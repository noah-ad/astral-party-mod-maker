namespace JixModMaker;

public sealed class ExportSelectionDialog : Form
{
    private readonly List<ModEntry> _entries;
    private readonly HashSet<ModEntry> _selected;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true };
    private readonly TextBox _search = new() { Width = 240, PlaceholderText = "搜索名称 / 资源包" };
    private readonly ComboBox _range = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _export = Theme.FlatButton("导出所选");
    private bool _refreshing;

    public List<ModEntry> SelectedEntries => _entries.Where(_selected.Contains).ToList();

    public ExportSelectionDialog(IEnumerable<ModEntry> entries, bool bundles)
    {
        SuspendLayout();
        WindowLayout.Configure(this);
        _entries = entries.OrderByDescending(e => e.ModifiedAt).ToList();
        _selected = new HashSet<ModEntry>(_entries);
        Text = bundles ? "选择导出的资源包" : "选择导出的贴图";
        Size = new Size(860, 600);
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.UI(9f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        _list.BackColor = Theme.Card;
        _list.ForeColor = Theme.Text;
        _list.Columns.Add("贴图", 300);
        _list.Columns.Add("修改时间", 155);
        _list.Columns.Add("资源包", 300);
        void LayoutColumns()
        {
            int width = Math.Max(1, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            int time = Math.Min(width / 3, TextRenderer.MeasureText("2026-10-17 23:59", _list.Font).Width + _list.Font.Height);
            _list.Columns[0].Width = (width - time) / 2;
            _list.Columns[1].Width = time;
            _list.Columns[2].Width = width - _list.Columns[0].Width - time;
        }
        _list.SizeChanged += (_, _) => LayoutColumns();
        _list.FontChanged += (_, _) => LayoutColumns();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), WrapContents = true };
        _range.Items.AddRange(new object[] { "全部时间", "最近 24 小时", "最近 7 天", "时间未知" });
        _range.SelectedIndex = 0;
        filters.Controls.AddRange(new Control[] { _search, _range });
        var all = Theme.FlatButton("全选当前结果");
        var none = Theme.FlatButton("清空选择");
        filters.Controls.AddRange(new Control[] { all, none });
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        var cancel = Theme.FlatButton("取消");
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.AddRange(new Control[] { _export, cancel });
        var notice = new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12, 8, 12, 8),
            Text = "同一资源包中的其它修改会一并导出；基础资源与热更新缓存混选时自动分为两个 ZIP。", Visible = bundles };
        layout.Controls.Add(filters, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        layout.Controls.Add(notice, 0, 2);
        layout.Controls.Add(footer, 0, 3);
        layout.SizeChanged += (_, _) => notice.MaximumSize = new Size(Math.Max(1, layout.ClientSize.Width - notice.Margin.Horizontal), 0);
        Controls.Add(layout);
        CancelButton = cancel;
        _search.TextChanged += (_, _) => RefreshRows(false);
        _range.SelectedIndexChanged += (_, _) => RefreshRows(true);
        _list.ItemChecked += (_, e) =>
        {
            if (_refreshing || e.Item.Tag is not ModEntry entry) return;
            if (e.Item.Checked) _selected.Add(entry); else _selected.Remove(entry);
            UpdateCount();
        };
        all.Click += (_, _) => { foreach (ListViewItem item in _list.Items) item.Checked = true; };
        none.Click += (_, _) => { _selected.Clear(); RefreshRows(false); };
        _export.Click += (_, _) => { if (_selected.Count > 0) { DialogResult = DialogResult.OK; Close(); } };
        RefreshRows(false);
        ResumeLayout(true);
    }

    private void RefreshRows(bool selectRange)
    {
        var now = DateTimeOffset.UtcNow;
        var query = _search.Text.Trim();
        var visible = _entries.Where(e => MatchesRange(e, _range.SelectedIndex, now))
            .Where(e => string.IsNullOrEmpty(query) || (e.TextureName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
                || (e.Bundle ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (selectRange) { _selected.Clear(); _selected.UnionWith(visible); }
        _refreshing = true;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var entry in visible)
            {
                var item = new ListViewItem(entry.TextureName ?? entry.Label ?? "未命名") { Tag = entry, Checked = _selected.Contains(entry) };
                item.SubItems.Add(entry.ModifiedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "时间未知");
                item.SubItems.Add(entry.Bundle ?? "");
                _list.Items.Add(item);
            }
        }
        finally { _list.EndUpdate(); _refreshing = false; }
        UpdateCount();
    }

    public static bool MatchesRange(ModEntry entry, int range, DateTimeOffset now) => range switch
    {
        1 => entry.ModifiedAt >= now.AddDays(-1) && entry.ModifiedAt <= now,
        2 => entry.ModifiedAt >= now.AddDays(-7) && entry.ModifiedAt <= now,
        3 => entry.ModifiedAt == null,
        _ => true
    };

    private void UpdateCount()
    {
        _export.Text = $"导出所选 ({_selected.Count})";
        _export.Enabled = _selected.Count > 0;
    }
}
