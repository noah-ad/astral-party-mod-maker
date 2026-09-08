using JixModMaker;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}

Check(NameParser.Parse("UT_Hero_Card_135_06_0").Skin == "皮肤06", "new hero and skin 06");
Check(NameParser.Parse("UT_Hero_Card_135").Skin == "原皮", "base skin label");
var now = DateTimeOffset.UtcNow;
Check(!ExportSelectionDialog.MatchesRange(new ModEntry(), 1, now), "unknown date excluded from recent");
Check(ExportSelectionDialog.MatchesRange(new ModEntry(), 3, now), "unknown date filter");
Check(ExportSelectionDialog.MatchesRange(new ModEntry { ModifiedAt = now.AddHours(-2) }, 1, now), "recent entry included");
Check(!ExportSelectionDialog.MatchesRange(new ModEntry { ModifiedAt = now.AddDays(-8) }, 2, now), "old entry excluded");
var workspace = new ModManifest();
PackService.Upsert(workspace, new ModEntry { Bundle = "a", PathId = 1 });
Check(workspace.Entries.Single().ModifiedAt >= now, "replacement gets timestamp");

var root = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var bundle = Path.Combine(root, "test.bundle");
File.WriteAllBytes(bundle, new byte[] { 1 });
var first = IndexService.ComputeSourceStamp(root, false, false);
File.WriteAllBytes(bundle, new byte[] { 1, 2 });
Check(first != IndexService.ComputeSourceStamp(root, false, false), "same filename update invalidates index");
first = IndexService.ComputeSourceStamp(root, false, false);
File.WriteAllBytes(Path.Combine(root, "new-hero.bundle"), new byte[] { 3 });
Check(first != IndexService.ComputeSourceStamp(root, false, false), "new bundle invalidates index");
Console.WriteLine("All regression checks passed.");
var wrapped = Path.Combine(root, "hash-a", "hash-b");
Directory.CreateDirectory(wrapped);
File.WriteAllBytes(Path.Combine(wrapped, "__data"), new byte[] { 4, 5 });
File.WriteAllText(Path.Combine(wrapped, "__info"), "metadata");
var output = Path.Combine(root, "replacement.zip");
ReplacementZip.Export(root, new[] { bundle, Path.Combine(wrapped, "__data") }, output);
using (var zip = System.IO.Compression.ZipFile.OpenRead(output))
{
    Check(zip.Entries.Select(e => e.FullName).Order().SequenceEqual(new[] { "hash-a/hash-b/__data", "hash-a/hash-b/__info", "test.bundle" }), "replacement ZIP preserves exact paths without wrappers");
    using var data = zip.GetEntry("hash-a/hash-b/__data").Open();
    Check(data.ReadByte() == 4 && data.ReadByte() == 5 && data.ReadByte() == -1, "replacement bytes unchanged");
}
try
{
    ReplacementZip.Export(wrapped, new[] { bundle }, output);
    throw new Exception("external path accepted");
}
catch (InvalidOperationException) { Console.WriteLine("PASS external resource rejected"); }
Exception uiError = null;
var ui = new Thread(() =>
{
    try
    {
        using var dialog = new ExportSelectionDialog(new[]
        {
            new ModEntry { TextureName = "UT_Hero_Card_135_06", Bundle = "new-hero.bundle", ModifiedAt = now },
            new ModEntry { TextureName = "UT_Hero_Card_101", Bundle = "old.bundle" }
        }, true);
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new System.Drawing.Point(-2000, -2000);
        dialog.Show();
        Application.DoEvents();
        dialog.PerformLayout();
        using var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "export-dialog.png"));
        Check(dialog.SelectedEntries.Count == 2, "export selection retains both entries");
    }
    catch (Exception ex) { uiError = ex; }
});
ui.SetApartmentState(ApartmentState.STA);
ui.Start();
ui.Join();
if (uiError != null) throw uiError;
