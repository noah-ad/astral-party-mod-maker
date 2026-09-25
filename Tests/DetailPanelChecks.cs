using System.Drawing;
using System.Reflection;
using JixModMaker;

internal static class DetailPanelChecks
{
    private sealed class FixtureForm : MainForm
    {
        public FixtureForm(string root) : base(root) { }
        protected override void OnShown(EventArgs e) { }
    }

    public static void Run(string root, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (float scale in new[] { 1f, 1.5f })
        foreach (var size in new[] { new Size(1400, 860), new Size(1100, 640) })
        {
            using var form = new FixtureForm(root)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-5000, -5000)
            };
            object Field(string name) => typeof(MainForm).GetField(name, flags).GetValue(form);
            void Set(string name, object value) => typeof(MainForm).GetField(name, flags).SetValue(form, value);
            void Call(string method, params object[] arguments) => typeof(MainForm).GetMethod(method, flags).Invoke(form, arguments);
            form.Show();
            form.Scale(new SizeF(scale, scale));
            form.MinimumSize = Size.Empty;
            form.Size = size;
            Call("Navigate", "browse");
            var portrait = new TexRef
            {
                Name = "UT_Hero_Card_101_06",
                Kind = ResourceKinds.Texture,
                CategoryLabel = "角色 / 皮肤 / 怪物",
                BundleName = "f55bc7ef344ae23241259042fdb44140.bundle",
                BundlePath = Path.Combine(root, "f55bc7ef344ae23241259042fdb44140.bundle"),
                PathId = 1234567890123456789,
                Width = 880,
                Height = 1205,
                Format = "RGBA32",
                Source = "基础包"
            };
            void Select(TexRef asset)
            {
                Set("_selectedAsset", asset);
                Call("UpdateDetailText", asset);
                Call("SetDetailButtons", true, asset.IsTexture);
                Application.DoEvents();
            }
            Select(portrait);
            var animated = (Button)Field("_replaceAnimationBtn");
            var panel = (Panel)Field("_rightPanel");
            string scenario = $"{size.Width}x{size.Height} at {scale}";
            check(animated.Visible && animated.Enabled, "portrait animation action visible and enabled: " + scenario);
            check(!panel.HorizontalScroll.Visible, "detail panel has no horizontal overflow: " + scenario);
            foreach (Control button in animated.Parent.Controls)
            {
                check(button.Right <= button.Parent.ClientSize.Width && button.Left >= 0,
                    "detail button stays in column: " + button.Text + " " + scenario);
                var measured = TextRenderer.MeasureText(button.Text, button.Font);
                check(measured.Height <= button.Height && measured.Width + button.Padding.Horizontal <= button.Width,
                    "detail button text fits: " + button.Text + " " + scenario);
            }
            var details = animated.Parent.Parent;
            var rows = details.Controls.Cast<Control>().OrderBy(c => c.Top).ToArray();
            for (int i = 1; i < rows.Length; i++) check(rows[i - 1].Bottom <= rows[i].Top, "detail rows never overlap: " + scenario);
            foreach (var name in new[] { "_detailTitle", "_detailMeta", "_detailPath", "_detailHint" })
            {
                var label = (Label)Field(name);
                check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, "detail label is not clipped: " + name + " " + scenario);
            }
            panel.ScrollControlIntoView(animated);
            Application.DoEvents();
            var relative = panel.PointToClient(animated.PointToScreen(Point.Empty));
            check(relative.Y >= 0 && relative.Y + animated.Height <= panel.ClientSize.Height, "animation action remains reachable: " + scenario);
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"detail-panel-{size.Width}-{scale}.png"));
            }
            if (scale == 1f && size.Width == 1400)
            {
                bool opened = false;
                using var closeDialog = new System.Windows.Forms.Timer { Interval = 40 };
                closeDialog.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<AnimatedPortraitDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    opened = true;
                    dialog.Close();
                };
                closeDialog.Start();
                animated.PerformClick();
                closeDialog.Stop();
                check(opened, "detail action opens replacement dialog directly");
            }
            Select(new TexRef
            {
                Name = "Talent-001",
                Kind = ResourceKinds.Texture,
                CategoryLabel = "角色 / 皮肤 / 怪物",
                IsSkillAnimation = true,
                OwnerHeroId = "101",
                OwnerVariant = "02"
            });
            var replaceTexture = (Button)Field("_replaceTextureBtn");
            var detailHint = (Label)Field("_detailHint");
            check(animated.Visible && animated.Enabled && animated.Text == "替换技能动画",
                "skill animation action is explicit and reachable: " + scenario);
            check(!replaceTexture.Enabled, "packed skill atlas cannot be replaced as one static image: " + scenario);
            check(detailHint.Text.Contains("原生帧数") && detailHint.Text.Contains("只修改当前资源包"),
                "skill animation safety scope is visible: " + scenario);
            var skillText = TextRenderer.MeasureText(animated.Text, animated.Font);
            check(skillText.Height <= animated.Height && skillText.Width + animated.Padding.Horizontal <= animated.Width,
                "skill animation button text fits: " + scenario);
            Select(new TexRef { Name = "UT_Hero_Bust_101", Kind = ResourceKinds.Texture });
            check(animated.Visible && !animated.Enabled, "unsupported bust does not offer a false dynamic replacement");
            foreach (string name in new[]
            {
                "UT_HandCard_21002", "UT_Event_12703", "UT_MapEvent_31001_JP", "UT_HandCard_21002_sfw"
            })
            {
                Select(new TexRef { Name = name, Kind = ResourceKinds.Texture });
                check(animated.Visible && animated.Enabled, "supported card artwork offers dynamic replacement: " + name);
            }
            foreach (string name in new[] { "PlatformEvent", "LandEvent", "UT_HandCard_", "UT_Event_frame" })
            {
                Select(new TexRef { Name = name, Kind = ResourceKinds.Texture });
                check(animated.Visible && !animated.Enabled, "unaddressable card artwork stays disabled: " + name);
            }
            Select(new TexRef { Name = "voice", Kind = ResourceKinds.Audio });
            check(!animated.Enabled, "audio cannot invoke portrait replacement");
            Call("ClearDetails");
            check(!animated.Enabled, "clearing selection disables portrait replacement");
            form.Close();
            Application.DoEvents();
        }
    }
}
