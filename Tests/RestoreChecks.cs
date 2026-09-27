using JixModMaker;
using System.Reflection;
using System.Text.Json;

internal static class RestoreChecks
{
    private sealed class FixtureForm : MainForm
    {
        public FixtureForm(string root) : base(root) { }
        protected override void OnShown(EventArgs e) { }
    }

    public static void RunUi(string root, Action<bool, string> check)
    {
        string folder = Path.Combine(root, "restore-ui");
        string state = Path.Combine(folder, "_原始备份", "动态立绘", "current.json");
        Directory.CreateDirectory(Path.GetDirectoryName(state)!);
        var receipt = new PortraitReplacement.Receipt("UT_HandCard_21001", AnimatedPortraitPatch.VideoSlot, "test.mp4", false,
            DateTimeOffset.Now, null, null, null, null, new() { ["runtime.bundle"] = "fixture" });
        File.WriteAllText(state, JsonSerializer.Serialize(receipt));
        using var form = new FixtureForm(folder) { StartPosition = FormStartPosition.Manual, Location = new(-5000, -5000) };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Call(string method, params object[] arguments) => typeof(MainForm).GetMethod(method, flags).Invoke(form, arguments);
        form.Show();
        Call("Navigate", "browse");
        typeof(MainForm).GetField("_selectedAsset", flags).SetValue(form, new TexRef { Name = "UT_HandCard_21001", Kind = ResourceKinds.Texture });
        Call("SetDetailButtons", true, true);
        var button = (Button)typeof(MainForm).GetField("_restoreResourceBtn", flags).GetValue(form);
        check(button.Enabled && button.Text == "还原动态替换", "card restore uses its dynamic receipt without requiring video tools");
        typeof(MainForm).GetField("_selectedAsset", flags).SetValue(form, new TexRef { Name = "UT_HandCard_21002", Kind = ResourceKinds.Texture });
        Call("SetDetailButtons", true, true);
        check(!button.Enabled, "unrelated card cannot restore another card's dynamic patch");
        Call("Navigate", "tools");
        IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
            .SelectMany(child => new[] { child }.Concat(Descendants(child)));
        var maintenance = Descendants(form).OfType<Button>().Single(control => control.Text == "还原动态替换");
        check(maintenance.Enabled, "maintenance exposes dynamic restoration without selecting an asset");
        File.Delete(state);
        Call("Navigate", "tools");
        maintenance = Descendants(form).OfType<Button>().Single(control => control.Text == "还原动态替换");
        check(!maintenance.Enabled, "maintenance disables restoration once the dynamic receipt is cleared");
        form.Close();
        Application.DoEvents();
    }

    public static void Run(string root, Action<bool, string> check)
    {
        string folder = Path.Combine(root, "selective-restore"), backups = Path.Combine(folder, "backup");
        Directory.CreateDirectory(backups);
        string target = Path.Combine(folder, "one.bundle"), backup = Path.Combine(backups, "one.bundle");
        string other = Path.Combine(folder, "other.bundle");
        File.WriteAllText(target, "changed");
        File.WriteAllText(backup, "original");
        File.WriteAllText(other, "unrelated mod");
        var plan = BundleRestore.Prepare(target, backups, "one.bundle");
        BundleRestore.Apply(plan);
        check(File.ReadAllText(target) == "original" && File.ReadAllText(other) == "unrelated mod", "selected restore leaves other mods untouched");
        check(File.ReadAllText(backup) == "original", "selected restore retains the original backup");
        File.WriteAllText(target, "another edit");
        try { BundleRestore.Apply(plan); throw new Exception("stale restore accepted"); }
        catch (IOException) { check(File.ReadAllText(target) == "another edit", "restore rejects target changes after confirmation"); }
        plan = BundleRestore.Prepare(target, backups, "one.bundle");
        File.WriteAllText(backup, "different backup");
        try { BundleRestore.Apply(plan); throw new Exception("changed backup accepted"); }
        catch (IOException) { check(File.ReadAllText(target) == "another edit", "restore rejects backup changes after confirmation"); }
        check(!Directory.EnumerateFiles(folder).Any(path => path.Contains(".jix-restore-")), "restore leaves no temporary replacement files");
        var timing = new NativeMovieTiming(1504, 1080, 30, 1, 40, true, false);
        check(SkillAnimationDialog.TimingNotice(timing, null).Contains("1.333") &&
            SkillAnimationDialog.TimingNotice(timing, null).Contains("40 帧"), "native duration and frame count visible before selecting input");
        check(SkillAnimationDialog.WillTrim(timing, 10) && SkillAnimationDialog.TimingNotice(timing, 10).Contains("8.667"),
            "long video warning states the actual excess duration");
        check(!SkillAnimationDialog.WillTrim(timing, .4) && SkillAnimationDialog.TimingNotice(timing, .4).Contains("停留末帧"),
            "short video notice explains final-frame hold");
        check(!SkillAnimationDialog.WillTrim(timing, 1.34), "duration header rounding does not produce a false truncation warning");
    }
}
