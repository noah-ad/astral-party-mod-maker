# 吉星 Mod 制作器 (Astral Party Mod Maker)

面向《吉星派对 / Astral Party》的 Windows 图形化 Mod 制作工具。它可以自动定位游戏资源，浏览、预览和替换贴图，并把作品导出为 `.jxpack` 图包或 Bundle ZIP。

[![Latest Release](https://img.shields.io/github/v/release/noah-ad/astral-party-mod-maker?display_name=tag&label=最新版本&color=ff5fa2)](https://github.com/noah-ad/astral-party-mod-maker/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/noah-ad/astral-party-mod-maker/total?label=累计下载&color=55c8e8)](https://github.com/noah-ad/astral-party-mod-maker/releases)

> Unity 2021.3 + Addressables / C# / .NET 8 / WinForms / Windows only

## 下载

稳定发行版：**v2.2.1**。当前源码及本地实验版：**v2.3.0-preview.11**。

- [下载 v2.2.1 Win64 免安装包](https://github.com/noah-ad/astral-party-mod-maker/releases/download/v2.2.1/AstralPartyModMaker-v2.2.1-win64.zip)
- [查看全部版本与更新说明](https://github.com/noah-ad/astral-party-mod-maker/releases)

下载 ZIP 后完整解压，双击 `启动 吉星Mod制作器.bat` 或 `JixModMaker.exe`。发行包已包含 .NET 8 运行环境，无需另外安装 SDK 或 Desktop Runtime。

### 新版启动方式

preview.9 起的便携包顶层只有两项：

```text
吉星Mod制作器.exe
data/
```

**完整解压后双击 `吉星Mod制作器.exe`。** 不需要打开 `data`、寻找 DLL 或运行 BAT，也不需要另装 .NET。图标、程序、运行库及本地视频转换组件统一收进 `data`，启动 EXE 与该文件夹必须放在一起。可以把整个文件夹移到其它位置，也可以给 EXE 创建桌面快捷方式；不要只移动 EXE，也不要直接在压缩包内运行。

该布局只改变工具的发布目录，不改变游戏 Mod 的直接覆盖 ZIP。GitHub 源码包含新布局与动态立绘实现；由于视频转换依赖的再分发条件尚待确认，完整转换运行时仍只用于本地构建，暂未上传新的完整实验版安装包。

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
- **直接覆盖 ZIP**：保留当前资源根目录下的原始相对路径和文件名，手机端 `哈希目录/__data` 与配套 `__info` 保持原结构。不添加 current、original 或 manifest.json，解压到设备对应资源目录即可覆盖。只包含选中的资源包（同包的其它修改也会包含）。请基于目标手机版本的原始资源制作；此功能不会把 PC Bundle 转换为 Android Bundle。跨平台贴图可通过 `.jxpack` 应用到目标资源后再导出 ZIP。
- **容错导入**：v2 图包按贴图名定位，同名贴图会尽量写入当前目录的所有位置；缺失项只统计、不终止整包，可切换资源目录后重复导入。
- **资源包级操作**：非贴图资源支持索引、定位和替换所在资源包；资产级音频、模型与动画写回仍依赖对应格式的编码器。
- **备份与还原**：替换前自动保存原始 Bundle，可一键还原全部修改，并支持旧 Mod 迁移。
- **吉星风格界面**：采用应用图标的明快主色和圆角视觉，针对 Windows 150% DPI 及不缩放显示进行了布局适配。

## 动态立绘实验版（本地 v2.3.0-preview.11）

preview.11 已撤销独立资源注入、动态手牌和动态事件卡。当前实现回到较小的原生槽位方案，只支持角色立绘 `UT_Hero_Card_*`：

1. 拖入视频或 GIF，按选中立绘的宽高比拖动、缩放取景框；“动态预览”使用同一裁剪范围。去绿幕默认关闭，需要时手动勾选。
2. 工具使用 FFmpeg 和 CriCodecs 将内容转换成 CRI 播放器可用的颜色 / 透明双流 USM。正式视频最高保留到最长边 2048，使用 Lanczos 缩放、30 FPS、无音轨。
3. 视频数据只替换游戏已有的 `VHandCard_13021002` 资源；`AstralParty.Runtime.dll` 只新增一个小型映射函数，让选中的静态立绘返回这个视频键和 `isVideo=true`。不向 DLL 塞入视频，不创建新播放器、缓存、网格或卡牌渲染分支。
4. 原静态 Texture2D 不会被改动。没有匹配到所选立绘时，原名称和静态标记原样返回。手牌、事件、地图事件、头像、半身和细卡不开放动态按钮。
5. 一次只能绑定一个角色，并会占用 `VHandCard_13021002`；该异画在其它原生入口中的画面也可能随之改变。更换角色前先点“恢复原资源”。
6. 每次写入前会备份并校验热更新程序集包和原生视频包。恢复会把首次替换前的两个文件逐字节还原；如果文件已被游戏更新或其它 Mod 改动，工具会拒绝覆盖。
7. “导出 ZIP”包含这两个已改资源包及 B 服 / Android 所需的同目录 `__info`，保持原始相对路径，可直接覆盖对应平台资源。PC 与 Android 必须分别用各自资源制作，ZIP 不能跨平台转换。

旧 preview.9 / preview.10 的独立动态补丁不能直接升级。请先用当时的“恢复原资源”或游戏重置取得干净资源；工具检测到 `Jix.DynamicPortrait` / `JixLoadIndependentPortrait` 后会停止处理，避免在旧补丁上继续叠加。旧版曾产生空类型树的损坏包也会被拒绝。

PC 和 Android 的真实资源副本已通过：封包回读、完整类型表与对象类型索引一致、源文件哈希不变、重复替换、第二角色冲突拒绝、修改冲突拒绝、直接覆盖 ZIP、首次备份逐字节恢复。100% / 模拟 150% 的入口和布局检查也已通过。所有安装测试都只在隔离副本中执行，**仍未完成真实游戏内播放和移动端性能验证**；工具中的动态预览只证明转换结果，不代表游戏内已经播放成功。

稳定版 v2.2.1 不包含动态立绘功能。视频转换依赖未纳入 Git；CriCodecs 的再分发条件仍待确认，因此公开源码不能单独提供完整转换运行时。

详细结构与验证记录见 [动态立绘调查](Research/AnimationProbe/README.md)。

## 源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)。

```bash
git clone https://github.com/noah-ad/astral-party-mod-maker.git
cd astral-party-mod-maker/App
dotnet build -c Release
# 或直接运行
dotnet run -c Release
```

基础程序的第三方依赖已放在 [`libs/`](libs/) 目录（见 [libs/README.txt](libs/README.txt) 说明来源与协议），clone 后可直接编译。视频转换依赖不在 Git 内，见 [转换组件说明](App/Tools/video/DEPENDENCIES.md)。

源码运行需要 Windows + .NET 8 Desktop Runtime（`dotnet publish -c Release -r win-x64 --self-contained true` 可打出免安装运行时的发布包）。普通用户直接下载上面的发行包即可。

### 构建 EXE + data 便携包

在仓库根目录使用 Windows PowerShell 5.1 或更新版本：

```powershell
# 需要本地已经配齐视频转换组件；生成目录和 ZIP
./scripts/Publish-Portable.ps1 -Zip

# 仅供开发检查：不包含视频转换器，不作为完整功能包分发
./scripts/Publish-Portable.ps1 -WithoutVideoRuntime -OutputDirectory ./artifacts/developer-check
```

默认产物在 `artifacts/AstralPartyModMaker-v2.3.0-preview.11-win64/`，同名 ZIP 位于旁边。脚本拒绝覆盖已有目录或 ZIP；重复构建请使用新的 `-OutputDirectory`。启动文件使用 SDK 的原生 apphost，以相对路径加载 `data/JixModMaker.dll`，运行时也随包存放，不需要额外启动器运行库或管理员权限。普通 `dotnet build` 的开发输出维持原样。

可运行 `./Tests/PortablePackage.ps1 -PackageDirectory <发布目录>` 检查根目录结构、图标与版本、中文空格路径、移动后的启动和随包运行库。测试只打开空资源目录，不修改游戏文件。

## 使用

1. 启动程序并等待自动检测；未检测到时，手动选择包含游戏资源的目录。
2. 普通 PC 版选择 `...\StreamingAssets\aa\StandaloneWindows64`；B服选择 `...\com.unity.addressables\AssetBundles`。
3. 在“资源浏览”中选择类型，展开角色与皮肤后选中资源。
4. 将图片拖入卡片完成替换，或把预览拖到文件夹导出 PNG。
5. 在“作品 / 导出”中导出 `.jxpack` 或 Bundle ZIP；在“工具 / 维护”中刷新索引、完整扫描、迁移或还原。

## 技术栈 / 第三方库

- [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) — 读写 Unity bundle / 序列化文件
- [Mono.Cecil](https://github.com/jbevain/cecil) — 实验性立绘热更新程序集补丁（MIT）
- AssetsTools.NET.Texture + [AssetRipper.TextureDecoder](https://github.com/AssetRipper/TextureDecoder) — Texture2D 编解码
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) — 图像处理

详见 [`libs/README.txt`](libs/README.txt)。

> ⚠️ 本工具只在本 GitHub 仓库免费发布。请勿从任何第三方渠道付费购买，谨防上当受骗。

## 免责声明

本工具仅供学习交流与个人 Mod 制作。**不包含任何游戏资源**；游戏素材版权归原作者所有。使用本工具修改游戏文件的风险由使用者自行承担（已内置自动备份 / 还原）。请勿用于商业用途或传播侵权内容。

## 协议

本项目源码采用 [MIT](LICENSE) 协议。`libs/` 下第三方库版权归各自作者，遵循其原始协议。
