# 吉星 Mod 制作器 (Astral Party Mod Maker)

面向《吉星派对 / Astral Party》的 Windows 图形化 Mod 制作工具。它可以自动定位游戏资源，浏览、预览和替换贴图，并把作品导出为 `.jxpack` 图包或 Bundle ZIP。

[![Latest Release](https://img.shields.io/github/v/release/noah-ad/astral-party-mod-maker?display_name=tag&label=最新版本&color=ff5fa2)](https://github.com/noah-ad/astral-party-mod-maker/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/noah-ad/astral-party-mod-maker/total?label=累计下载&color=55c8e8)](https://github.com/noah-ad/astral-party-mod-maker/releases)

> Unity 2021.3 + Addressables / C# / .NET 8 / WinForms / Windows only

## 下载

当前版本：**v2.2.0**

- [下载 v2.2.0 Win64 免安装包](https://github.com/noah-ad/astral-party-mod-maker/releases/download/v2.2.0/AstralPartyModMaker-v2.2.0-win64.zip)
- [查看全部版本与更新说明](https://github.com/noah-ad/astral-party-mod-maker/releases)

下载 ZIP 后完整解压，双击 `启动 吉星Mod制作器.bat` 或 `JixModMaker.exe`。发行包已包含 .NET 8 运行环境，无需另外安装 SDK 或 Desktop Runtime。

## 功能

- **自动检测游戏**：自动查找常见 Steam 安装位置，也可手动选择游戏资源目录。
- **PC / B服目录直读**：同时支持普通 `.bundle` 目录和 B服 `哈希目录/__data + __info` 套壳目录；误选 B服哈希子目录时会自动回溯到 `AssetBundles` 根目录。
- **增量索引**：后台检查实际游戏文件和热更目录，根据路径、大小和修改时间更新索引，只重扫变化的包；首次升级需要重建一次。音频、文本、模型和动画可在“工具 / 维护”中执行完整扫描。
- **选择导出范围**：图包和已改 Bundle ZIP 均支持勾选、搜索、最近 24 小时及最近 7 天筛选。旧记录没有修改时间，显示为“时间未知”；新替换和导入自动记录时间。Bundle ZIP 导出整个资源包，包含同包中的其它修改。
- **精简分类**：隐藏空分类，动作帧、特效和其它低频资源可按需展开；支持 06 及更高编号皮肤。
- **统一资源浏览**：角色卡面、细卡、半身、头像、角色照、升级图和怪物立绘统一归入角色资源；背景、横幅、道具、骰子、音频、模型、动画等按类型筛选。
- **角色与皮肤树**：角色栏支持展开角色和皮肤、搜索及快速定位，不需要逐页翻找。
- **直观替换与预览**：把图片拖入资源卡即可替换，也可双击选图；支持预览、智能裁切及原图导出。
- **拖动导出**：直接把贴图预览拖到资源管理器即可导出 PNG，资源列表连续滚动、不分页。
- **作品与分享**：导入、导出 `.jxpack` 图包，也可导出当前 Bundle 或全部已修改 Bundle 的 ZIP 压缩包，并附带来源清单和可用的原始备份。
- **容错导入**：v2 图包按贴图名定位，同名贴图会尽量写入当前目录的所有位置；缺失项只统计、不终止整包，可切换资源目录后重复导入。
- **资源包级操作**：非贴图资源支持索引、定位和替换所在资源包；资产级音频、模型与动画写回仍依赖对应格式的编码器。
- **备份与还原**：替换前自动保存原始 Bundle，可一键还原全部修改，并支持旧 Mod 迁移。
- **吉星风格界面**：采用应用图标的明快主色和圆角视觉，针对 Windows 150% DPI 及不缩放显示进行了布局适配。

## 编译

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)。

```bash
git clone https://github.com/noah-ad/astral-party-mod-maker.git
cd astral-party-mod-maker/App
dotnet build -c Release
# 或直接运行
dotnet run -c Release
```

第三方依赖已放在 [`libs/`](libs/) 目录（见 [libs/README.txt](libs/README.txt) 说明来源与协议），clone 后可直接编译，无需额外准备。

源码运行需要 Windows + .NET 8 Desktop Runtime（`dotnet publish -c Release -r win-x64 --self-contained true` 可打出免安装运行时的发布包）。普通用户直接下载上面的发行包即可。

## 使用

1. 启动程序并等待自动检测；未检测到时，手动选择包含游戏资源的目录。
2. 普通 PC 版选择 `...\StreamingAssets\aa\StandaloneWindows64`；B服选择 `...\com.unity.addressables\AssetBundles`。
3. 在“资源浏览”中选择类型，展开角色与皮肤后选中资源。
4. 将图片拖入卡片完成替换，或把预览拖到文件夹导出 PNG。
5. 在“作品 / 导出”中导出 `.jxpack` 或 Bundle ZIP；在“工具 / 维护”中刷新索引、完整扫描、迁移或还原。

## 技术栈 / 第三方库

- [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) — 读写 Unity bundle / 序列化文件
- AssetsTools.NET.Texture + [AssetRipper.TextureDecoder](https://github.com/AssetRipper/TextureDecoder) — Texture2D 编解码
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) — 图像处理

详见 [`libs/README.txt`](libs/README.txt)。

> ⚠️ 本工具只在本 GitHub 仓库免费发布。请勿从任何第三方渠道付费购买，谨防上当受骗。

## 免责声明

本工具仅供学习交流与个人 Mod 制作。**不包含任何游戏资源**；游戏素材版权归原作者所有。使用本工具修改游戏文件的风险由使用者自行承担（已内置自动备份 / 还原）。请勿用于商业用途或传播侵权内容。

## 协议

本项目源码采用 [MIT](LICENSE) 协议。`libs/` 下第三方库版权归各自作者，遵循其原始协议。
