using System.Drawing;
using System.Reflection;
using JixModMaker;

internal static class DpiLayoutChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class BrowserForm : MainForm
    {
        protected override void OnShown(EventArgs e) { }
    }

    private static IEnumerable<Control> All(Control root) =>
        new[] { root }.Concat(root.Controls.Cast<Control>().SelectMany(All));

    public static void Run(string output, Action<bool, string> check)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                WindowsFormsSynchronizationContext.AutoInstall = false;
                using var context = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(context);
                foreach (int dpi in new[] { 96, 120, 144, 192, 240 })
                {
                    float scale = dpi / 96f;
                    var fonts = new Dictionary<Control, Font>();
                    void Resize(Form form, Size logical)
                    {
                        var area = Screen.FromControl(form).WorkingArea;
                        form.ClientSize = new Size(Math.Min((int)(logical.Width * scale), area.Width - 32),
                            Math.Min((int)(logical.Height * scale), area.Height - 64));
                    }
                    void ScaleFonts(Control root)
                    {
                        var fresh = All(root).Where(c => !fonts.ContainsKey(c)).Select(c => (Control: c, Font: c.Font)).ToArray();
                        foreach (var (control, font) in fresh) fonts[control] = font;
                        foreach (var (control, font) in fresh)
                            control.Font = new Font(font.FontFamily, font.Size * scale, font.Style);
                    }
                    void Prepare(Form form, Size logical, bool browser = false)
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-5000, -5000);
                        form.Show();
                        form.MinimumSize = Size.Empty;
                        form.Scale(new SizeF(scale, scale));
                        ScaleFonts(form);
                        Resize(form, logical);
                        if (browser) Call((MainForm)form, "ApplyDpiScale", dpi);
                        form.Location = new Point(-5000, -5000);
                        Application.DoEvents();
                    }
                    void Settle(Control root)
                    {
                        ScaleFonts(root);
                        foreach (var panel in All(root).OfType<TableLayoutPanel>().Reverse()) panel.PerformLayout();
                        foreach (var card in All(root).OfType<RoundedCard>()) card.PerformLayout();
                        root.PerformLayout();
                        Application.DoEvents();
                    }
                    void Snapshot(Form form, string name)
                    {
                        if (dpi is not (144 or 240)) return;
                        using var bitmap = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(output, $"{name}-{dpi}.png"));
                    }
                    void Buttons(Control root, string scenario)
                    {
                        foreach (var button in All(root).OfType<Button>().Where(b => b.Visible))
                        {
                            var area = button.Parent.ClientRectangle;
                            bool scrolls = button.Parent is ScrollableControl { AutoScroll: true };
                            check(scrolls ? button.Left >= 0 && button.Right <= area.Right : area.Contains(button.Bounds),
                                $"{scenario} {dpi}: action stays inside parent: {button.Text} {button.Bounds} / {area}");
                            var measured = TextRenderer.MeasureText(button.Text, button.Font);
                            check(measured.Width + button.Padding.Horizontal <= button.Width && measured.Height + button.Padding.Vertical <= button.Height,
                                $"{scenario} {dpi}: action text fits: {button.Text}");
                        }
                        foreach (var panel in All(root).OfType<FlowLayoutPanel>().Where(p => !p.AutoScroll))
                        {
                            var children = panel.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
                            check(children.All(c => c.Left >= 0 && c.Top >= 0 && c.Right <= panel.ClientSize.Width && c.Bottom <= panel.ClientSize.Height),
                                $"{scenario} {dpi}: flow options are fully visible");
                            for (int i = 0; i < children.Length; i++)
                            for (int j = i + 1; j < children.Length; j++)
                                check(!children[i].Bounds.IntersectsWith(children[j].Bounds), $"{scenario} {dpi}: options do not overlap");
                        }
                    }

                    using (var form = new BrowserForm())
                    {
                        Prepare(form, new Size(1100, 700), browser: true);
                        typeof(MainForm).GetField("_ws", Flags).SetValue(form, new ModManifest
                        {
                            Entries = new List<ModEntry> { new() { TextureName = "UT_Monster_Card_1071", Bundle = "monster.bundle", Label = "1071" } }
                        });
                        foreach (string page in new[] { "dashboard", "pack", "tools" })
                        {
                            Call(form, "Navigate", page);
                            Settle(form);
                            foreach (Control region in form.Controls.Cast<Control>().Where(c => c.Visible))
                                check(form.ClientRectangle.Contains(region.Bounds), $"{page} {dpi}: docked region fits in the window");
                            var flow = (Panel)Field(form, "_flow");
                            Buttons(flow, page);
                            check(!flow.HorizontalScroll.Visible, $"{page} {dpi}: no horizontal overflow");
                            foreach (Control card in flow.Controls)
                            {
                                check(card.Right <= flow.ClientSize.Width, $"{page} {dpi}: full card fits");
                                foreach (Control child in card.Controls)
                                    check(card.ClientRectangle.Contains(child.Bounds), $"{page} {dpi}: content fits in card");
                            }
                            Snapshot(form, page);
                        }
                        Call(form, "Navigate", "browse");
                        Settle(form);
                        foreach (int width in new[] { 1100, 960, 1400 })
                        {
                            Resize(form, new Size(width, 700));
                            Application.DoEvents();
                            var filters = (Panel)Field(form, "_browseFilters");
                            var top = (Panel)Field(form, "_topBar");
                            var title = (Label)Field(form, "_pageTitle");
                            check(top.ClientRectangle.Contains(filters.Bounds) && !title.Bounds.IntersectsWith(filters.Bounds), $"browse {dpi}/{width}: title and filters fit");
                            foreach (Control control in filters.Controls)
                                check(filters.ClientRectangle.Contains(control.Bounds), $"browse {dpi}/{width}: filter is fully visible: {control.GetType().Name}");
                        }
                        var emptyBrowse = (Panel)Field(form, "_flow");
                        Buttons(emptyBrowse, "browse-empty");
                        foreach (Control card in emptyBrowse.Controls)
                            foreach (Control child in card.Controls)
                                check(card.ClientRectangle.Contains(child.Bounds), $"browse {dpi}: empty-state content fits in the narrow center");
                        var index = new GameIndex
                        {
                            Items = new[] { "UT_Monster_Card_1071", "UT_Hero_Card_101", "UT_Hero_Card_101_06" }
                                .Select(name => new TexIndexEntry { Name = name, CategoryId = ResourceCategories.CharacterId }).ToList()
                        };
                        typeof(MainForm).GetField("_index", Flags).SetValue(form, index);
                        Call(form, "BuildCategoryList", ResourceKinds.Texture);
                        Settle(form);
                        Buttons((Panel)Field(form, "_leftPanel"), "navigation");
                        var asset = new TexRef { Name = "UT_Monster_Card_1071", Kind = ResourceKinds.Texture, Width = 1100, Height = 1540,
                            BundleName = "35684cb98cc3f9d1e8c820ec4efee7d8.bundle", BundlePath = Path.Combine(output, "missing.bundle") };
                        Call(form, "UpdateDetailText", asset);
                        Call(form, "SetDetailButtons", true, true);
                        Settle(form);
                        var detail = (Panel)Field(form, "_rightPanel");
                        Buttons(detail.Controls[0], "details");
                        check(!detail.HorizontalScroll.Visible, $"details {dpi}: no horizontal overflow");
                        Snapshot(form, "browse");
                        form.Close();
                    }

                    var entries = new[] { new ModEntry { TextureName = "UT_Monster_Card_1071", Bundle = "monster.bundle", ModifiedAt = DateTimeOffset.UtcNow } };
                    using (var dialog = new ExportSelectionDialog(entries, true))
                    {
                        Prepare(dialog, new Size(640, 420));
                        Settle(dialog);
                        Buttons(dialog, "export");
                        var range = All(dialog).OfType<ComboBox>().Single();
                        range.SelectedIndex = 1;
                        check(dialog.SelectedEntries.Count == 1, $"export {dpi}: recent filter keeps the matching entry");
                        All(dialog).OfType<Button>().Single(b => b.Text == "清空选择").PerformClick();
                        check(dialog.SelectedEntries.Count == 0, $"export {dpi}: clear selection works");
                        All(dialog).OfType<Button>().Single(b => b.Text == "全选当前结果").PerformClick();
                        check(dialog.SelectedEntries.Count == 1, $"export {dpi}: select all works");
                        Snapshot(dialog, "export");
                        dialog.Close();
                    }
                    using (var dialog = new AnimatedPortraitDialog(output, "UT_Hero_Card_101"))
                    {
                        Prepare(dialog, new Size(600, 440));
                        Settle(dialog);
                        Buttons(dialog, "portrait");
                        check(All(dialog).OfType<VideoCropControl>().Single().Height >= 60 * scale, $"portrait {dpi}: crop area remains usable");
                        Snapshot(dialog, "portrait");
                        dialog.Close();
                    }
                    var info = new SkillAnimationInfo("Talent-001", 1, 512, 512, 256, 256, new[] { "frame1", "frame2" });
                    var skill = new TexRef { Name = "Talent-001", BundleName = "missing.bundle", BundlePath = Path.Combine(output, "missing.bundle") };
                    using (var dialog = new SkillAnimationDialog(skill, info, output))
                    {
                        Prepare(dialog, new Size(620, 460));
                        Settle(dialog);
                        Buttons(dialog, "skill");
                        check(All(dialog).OfType<VideoCropControl>().Single().Height >= 60 * scale, $"skill {dpi}: crop area remains usable");
                        Snapshot(dialog, "skill");
                        dialog.Close();
                    }
                    using (var dialog = new VideoRuntimeDialog())
                    {
                        Prepare(dialog, new Size(510, 350));
                        Settle(dialog);
                        Buttons(dialog, "video-setup");
                        foreach (var label in All(dialog).OfType<Label>())
                            check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, $"video-setup {dpi}: text is fully visible");
                        Snapshot(dialog, "video-setup");
                        dialog.Close();
                    }
                    string imagePath = Path.Combine(output, "crop-fixture.png");
                    if (!File.Exists(imagePath))
                    {
                        using var image = new Bitmap(320, 240);
                        using var graphics = Graphics.FromImage(image);
                        graphics.Clear(Color.CornflowerBlue);
                        image.Save(imagePath);
                    }
                    using (var dialog = new CropDialog(imagePath, 880, 1205, "DPI"))
                    {
                        Prepare(dialog, new Size(640, 480));
                        Settle(dialog);
                        Buttons(dialog, "crop");
                        Snapshot(dialog, "crop");
                        dialog.Close();
                    }
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static object Field(MainForm form, string name) => typeof(MainForm).GetField(name, Flags).GetValue(form);
    private static void Call(MainForm form, string name, params object[] arguments) => typeof(MainForm).GetMethod(name, Flags).Invoke(form, arguments);
}
