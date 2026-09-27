namespace JixModMaker;

public sealed record BundleRestorePlan(string Target, string Backup, string CurrentHash, string OriginalHash);

public static class BundleRestore
{
    public static BundleRestorePlan Prepare(string target, string backupDir, string bundleName)
    {
        target = Path.GetFullPath(target);
        string backup = Path.GetFullPath(ResourceLocator.BackupPath(target, backupDir, bundleName));
        if (!File.Exists(backup)) throw new FileNotFoundException("没有找到当前资源包的原始备份。", backup);
        if (Path.GetFullPath(target).Equals(Path.GetFullPath(backup), StringComparison.OrdinalIgnoreCase))
            throw new IOException("不能把备份文件作为还原目标。");
        return new(target, backup, SkillMovieEngine.Hash(target), SkillMovieEngine.Hash(backup));
    }

    public static void Apply(BundleRestorePlan plan)
    {
        PortraitReplacement.EnsureGameClosed();
        if (SkillMovieEngine.Hash(plan.Target) != plan.CurrentHash || SkillMovieEngine.Hash(plan.Backup) != plan.OriginalHash)
            throw new IOException("资源或备份在确认期间已改变，未覆盖。请重新选择后再还原。");
        string stage = plan.Target + ".jix-restore-" + Guid.NewGuid().ToString("N");
        string rollback = stage + ".rollback";
        try
        {
            File.Copy(plan.Backup, stage, false);
            if (SkillMovieEngine.Hash(stage) != plan.OriginalHash) throw new IOException("还原副本校验失败，未写入。");
            PortraitReplacement.EnsureGameClosed();
            if (SkillMovieEngine.Hash(plan.Target) != plan.CurrentHash) throw new IOException("当前资源已改变，未覆盖。");
            File.Replace(stage, plan.Target, rollback);
            if (SkillMovieEngine.Hash(plan.Target) != plan.OriginalHash)
                throw new IOException("还原后文件已被其他程序改动，现场副本保留在：" + rollback);
            File.Delete(rollback);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
}
