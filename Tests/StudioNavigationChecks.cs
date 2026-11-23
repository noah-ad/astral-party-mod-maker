using System.Diagnostics;
using System.Reflection;
using JixModMaker;

internal static class StudioNavigationChecks
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Studio : MainForm
    {
        protected override void OnShown(EventArgs e) { }
    }

    public static void Run(string output, string artBundle, Action<bool, string> check)
    {
        output = Path.GetFullPath(output);
        var settings = Directory.CreateDirectory(Path.Combine(output, "settings")).FullName;
        string preference = Path.Combine(settings, "ui-language.txt");
        File.WriteAllText(preference, "zh-CN");
        AppContext.SetData("JixModMaker.SettingsDirectory", settings);
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                using var form = new Studio { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, 0) };
                form.Show();
                form.ClientSize = new Size(1180, 740);
                object Field(string name) => typeof(MainForm).GetField(name, Members)!.GetValue(form);
                void Call(string name, params object[] args) => typeof(MainForm).GetMethod(name, Members)!.Invoke(form, args);
                static IEnumerable<Control> Descendants(Control root) =>
                    root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
                Button Button(string caption) => Descendants(form).OfType<Button>().Single(b => b.Visible && b.Text == caption);
                void Snapshot(string name)
                {
                    using var image = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(output, name + ".png"));
                }
                void Settle(Func<bool> ready, int limit = 2000)
                {
                    var clock = Stopwatch.StartNew();
                    while (!ready() && clock.ElapsedMilliseconds < limit) { Application.DoEvents(); Thread.Sleep(10); }
                    check(ready(), "UI operation completed");
                }

                var index = new GameIndex { GameDir = output };
                for (int i = 1; i <= 48; i++)
                    index.Items.Add(new TexIndexEntry
                    {
                        Name = $"UT_Hero_Card_101_{i:D2}", PathId = i, CategoryId = ResourceCategories.CharacterId,
                        CategoryLabel = "角色 / 皮肤 / 怪物", Bundle = "fixture.bundle", Source = "基础包"
                    });
                if (artBundle != null)
                    index.Items.Add(new TexIndexEntry { Name = "UT_AccountBackground_101_01", Bundle = Path.GetFileName(artBundle),
                        BundlePath = Path.GetFullPath(artBundle), CategoryId = "background", CategoryLabel = "背景 / 横幅" });
                typeof(MainForm).GetField("_index", Members)!.SetValue(form, index);
                if (artBundle != null)
                {
                    Call("LoadWorkshopArt", index, output);
                    Settle(() => Field("_artwork") is Image, 20000);
                    var art = (Image)Field("_artwork");
                    check(art.Width > 0 && art.Height > 0 && art.Width <= 1100, "local game banner decoded through the studio loader");
                }
                Button("English").PerformClick();
                check(File.ReadAllText(preference) == "en" && form.Text.Contains("Astral Party"), "English preference saved and window title translated");
                check(Button("Browse assets").Visible, "dashboard command translated");
                Snapshot("workshop-en");
                Button("Library").PerformClick();
                var grid = (Panel)Field("_flow");
                var original = grid.Controls.OfType<RoundedCard>().Where(c => c.Tag is TexRef).ToList();
                check(original.Count > 1, "library rendered fixture assets");
                var chosen = (TexRef)original[1].Tag;
                Call("SelectAsset", chosen, null);
                grid.AutoScrollPosition = new Point(0, 80);
                var scroll = grid.AutoScrollPosition;
                Button("Projects").PerformClick();
                Button("Tools").PerformClick();
                Button("Library").PerformClick();
                check(ReferenceEquals(grid, Field("_flow")) && ReferenceEquals(original[0], grid.Controls.OfType<RoundedCard>().First()), "page round-trip reused live thumbnail controls");
                check(ReferenceEquals(chosen, Field("_selectedAsset")) && grid.AutoScrollPosition == scroll, "selection and scroll position survived navigation");
                Button("中文").PerformClick();
                check(File.ReadAllText(preference) == "zh-CN" && form.Text.Contains("吉星派对"), "Chinese preference saved");
                check(ReferenceEquals(chosen, Field("_selectedAsset")) && !original[0].IsDisposed, "language switch retained selected asset and thumbnails");
                check(((Label)Field("_detailState")).Text.Contains("未核验"), "inspector follows language switch");
                form.ClientSize = new Size(720, 550);
                Button("分类").PerformClick();
                check(((Panel)Field("_leftPanel")).Visible && !grid.Visible, "compact category pane reachable");
                Button("预览").PerformClick();
                check(((Panel)Field("_rightPanel")).Visible && !grid.Visible, "compact inspector reachable");
                Button("浏览").PerformClick();
                check(grid.Visible && ((TextBox)Field("_searchBox")).Visible, "compact browser and search reachable");
                Button("English").PerformClick();
                var search = (TextBox)Field("_searchBox");
                search.Text = "nonexistent-asset";
                Settle(() => Descendants(grid).Any(c => c.Text == "No matching assets"));
                check(Button("Clear search").Enabled, "debounced search shows translated empty state");
                Snapshot("compact-en");
                check(Application.OpenForms.Count == 1, "page and language changes kept one window");
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
