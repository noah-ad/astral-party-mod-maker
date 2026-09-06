namespace JixModMaker;

public sealed class ExportSelectionDialog : Form
{
    private readonly List<ModEntry> _entries;
    private readonly HashSet<ModEntry> _selected;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true };
    private readonly TextBox _search = new() { Width = 240, PlaceholderText = "搜索名称 / 资源包" };
    private readonly ComboBox _range = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _export = new() { AutoSize = true, Text = "导出所选" };
    private bool _refreshing;

    public List<ModEntry> SelectedEntries => _entries.Where(_selected.Contains).ToList();

    public ExportSelectionDialog(IEnumerable<ModEntry> entries, bool bundles)
    {
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
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8), WrapContents = false };
        _range.Items.AddRange(new object[] { "全部时间", "最近 24 小时", "最近 7 天", "时间未知" });
        _range.SelectedIndex = 0;
        filters.Controls.AddRange(new Control[] { _search, _range });
        var all = new Button { Text = "全选当前结果", AutoSize = true };
        var none = new Button { Text = "清空选择", AutoSize = true };
        filters.Controls.AddRange(new Control[] { all, none });
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        footer.Controls.AddRange(new Control[] { _export, cancel });
        if (bundles)
            Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 44, Text = "Bundle ZIP 包含整包内容；同一资源包中的其它修改也会一起导出。", AutoEllipsis = false });
        Controls.Add(_list);
        Controls.Add(filters);
        Controls.Add(footer);
        _list.BringToFront();
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
