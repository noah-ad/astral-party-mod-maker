namespace JixModMaker;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 调试入口: --crop-test <图片>  直接打开裁切窗口 (Card2 比例 420x684)
        if (args.Length >= 2 && args[0] == "--crop-test")
        {
            Application.Run(new CropDialog(args[1], 420, 684, "卡面2 测试"));
            return;
        }

        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
    }
}
