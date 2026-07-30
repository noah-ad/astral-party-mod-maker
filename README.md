# 吉星立绘 Mod 制作器 (Astral Party Mod Maker)

面向普通玩家的 **吉星派对 / Astral Party** 立绘 Mod 制作工具。无需手动操作 AssetStudio、UABEA，全流程图形化：浏览游戏立绘 → 拖图替换 → 导出 / 分享图包。

> Unity 2021.3 + Addressables · C# / .NET 8 / WinForms · Windows only

## ✨ 功能

- **按角色 / 皮肤浏览**：自动扫描游戏全部立绘并建立索引（首次约 10 秒，之后缓存秒开），左侧按角色分组，右侧按皮肤（原皮 / 皮肤01 / 皮肤02 / Max）分区。
- **拖拽替换**：把图片拖到对应格子即可替换；悬停高亮目标；也支持双击选图。
- **智能裁切**：卡面2 / 细卡这类固定竖版比例的立绘，拖图时自动弹出裁切框（锁定比例、三分构图线、拖拽缩放），省去手动 PS。
- **PC / B服目录直读**：同时支持普通 `.bundle` 目录和 B服 `哈希目录/__data + __info` 套壳目录，直接选择真实 `AssetBundles` 根目录，无需桥接或硬链接。
- **图包 (.jxpack)**：把改过的立绘打包分享；导入一键套用。**v2 格式按贴图名定位**，同名贴图在当前目录的所有位置都会尽量写入；缺失项只统计、不终止整包。
- **分目录重复导入**：B服资源拆在多个目录时，可在第一个目录应用能匹配的部分，再切到下一个目录导入同一个图包继续覆盖。
- **旧 Mod 迁移**：一键把旧版（按 bundle 哈希命名、版本更新后失效）的 Mod 转成最新图包，按 PC/安卓 × 角色/卡图 分类。
- **缩放 / 重命名**：Ctrl + 滚轮缩放缩略图；右键给角色起名（持久保存）。
- **安全**：所有替换前自动备份，可一键还原。

## ⬇️ 下载使用（普通玩家）

不想编译？直接到 [**Releases**](https://github.com/noah-ad/astral-party-mod-maker/releases) 下载打包好的 `吉星立绘Mod制作器.zip`，解压后双击根目录的「启动 吉星立绘Mod制作器.bat」即可，电脑无需安装任何环境。

## 🔧 编译

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)。

```bash
git clone https://github.com/<你的用户名>/astral-party-mod-maker.git
cd astral-party-mod-maker/App
dotnet build -c Release
# 或直接运行
dotnet run -c Release
```

第三方依赖已放在 [`libs/`](libs/) 目录（见 [libs/README.txt](libs/README.txt) 说明来源与协议），clone 后可直接编译，无需额外准备。

运行需要 Windows + .NET 8 Desktop Runtime（`dotnet publish -c Release -r win-x64 --self-contained true` 可打出免安装运行时的发布包）。

## 📖 使用

1. 点 **打开游戏目录**：
   - 普通 PC 版选择 `...\StreamingAssets\aa\StandaloneWindows64`。
   - B服选择 `...\com.unity.addressables\AssetBundles`；误选下面任意哈希子目录时也会自动回溯到根目录。
2. 左侧选角色，右侧拖图替换。卡面2 / 细卡会弹裁切框。
3. **导出图包** 分享，或 **导入图包** 套用别人的 Mod。当前目录没有对应位置的图片会跳过，可切换目录后再次导入同一个图包。
4. **还原全部** 可按真实相对路径恢复普通 bundle 与 B服套壳资源。

## 🧩 技术栈 / 第三方库

- [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) — 读写 Unity bundle / 序列化文件
- AssetsTools.NET.Texture + [AssetRipper.TextureDecoder](https://github.com/AssetRipper/TextureDecoder) — Texture2D 编解码
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) — 图像处理

详见 [`libs/README.txt`](libs/README.txt)。


> ⚠️ 本工具只在本 GitHub 仓库免费发布。请勿从任何第三方渠道付费购买，谨防上当受骗。

## ⚠️ 免责声明

本工具仅供学习交流与个人 Mod 制作。**不包含任何游戏资源**；游戏素材版权归原作者所有。使用本工具修改游戏文件的风险由使用者自行承担（已内置自动备份 / 还原）。请勿用于商业用途或传播侵权内容。

## 📄 协议

本项目源码采用 [MIT](LICENSE) 协议。`libs/` 下第三方库版权归各自作者，遵循其原始协议。
