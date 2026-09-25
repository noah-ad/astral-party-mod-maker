# 吉星 Mod 制作器 (Astral Party Mod Maker)

面向《吉星派对 / Astral Party》的 Windows 图形化 Mod 制作工具。它可以自动定位游戏资源，浏览、预览和替换贴图，并把作品导出为 `.jxpack` 图包或 Bundle ZIP。

[![Latest Release](https://img.shields.io/github/v/release/noah-ad/astral-party-mod-maker?display_name=tag&label=最新版本&color=ff5fa2)](https://github.com/noah-ad/astral-party-mod-maker/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/noah-ad/astral-party-mod-maker/total?label=累计下载&color=55c8e8)](https://github.com/noah-ad/astral-party-mod-maker/releases)

> Unity 2021.3 + Addressables / C# / .NET 8 / WinForms / Windows only

## 下载

稳定发行版：**v2.2.1**。当前源码及实验版：**v2.3.0-preview.12**。

- [下载 v2.2.1 Win64 免安装包](https://github.com/noah-ad/astral-party-mod-maker/releases/download/v2.2.1/AstralPartyModMaker-v2.2.1-win64.zip)
- [下载 v2.3.0-preview.12 Win64 核心预览包](https://github.com/noah-ad/astral-party-mod-maker/releases/download/v2.3.0-preview.12/AstralPartyModMaker-v2.3.0-preview.12-win64-core.zip)
- [查看全部版本与更新说明](https://github.com/noah-ad/astral-party-mod-maker/releases)

preview.12 的公开附件是 EXE + data 核心包，包含静态 Mod 制作、索引、分类和导出功能，源码也包含动态手牌、事件、立绘及技能动画实现；但附件**不含 FFmpeg / Python / CriCodecs，不能执行视频转换**。完整视频运行时只保留本地构建，待第三方再分发条件确认后再公开。

下载 ZIP 后完整解压，双击 `吉星Mod制作器.exe`。发行包已包含 .NET 8 运行环境，无需另外安装 SDK 或 Desktop Runtime。

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
- **快速增量索引**：正常启动使用游戏目录与 Addressables catalog 指纹判断更新，不再逐个访问数千个热更包；发现新目录或 catalog 更新后才增量重扫。点“刷新索引”会强制核对实际文件，只重扫变化的包。音频、文本、模型和动画可在“工具 / 维护”中执行完整扫描。
- **选择导出范围**：图包和已改 Bundle ZIP 均支持勾选、搜索、最近 24 小时及最近 7 天筛选。旧记录没有修改时间，显示为“时间未知”；新替换和导入自动记录时间。Bundle ZIP 导出整个资源包，包含同包中的其它修改。
- **精简分类**：隐藏空分类，动作帧、特效和其它低频资源可按需展开；支持 06 及更高编号皮肤。
- **统一资源浏览**：角色卡面、细卡、半身、头像、角色照、升级图、技能动画和怪物立绘统一归入角色资源；技能动画跟随 Addressables catalog 放在对应角色与皮肤下，背景、横幅、道具、骰子、音频、模型、动画等按类型筛选。
- **角色与皮肤树**：角色栏支持展开角色和皮肤、搜索及快速定位，不需要逐页翻找。
- **直观替换与预览**：把图片拖入资源卡即可替换，也可双击选图；支持预览、智能裁切及原图导出。
- **拖动导出**：直接把贴图预览拖到资源管理器即可导出 PNG，资源列表连续滚动、不分页。
- **直接覆盖 ZIP**：保留当前资源根目录下的原始相对路径和文件名，手机端 `哈希目录/__data` 与配套 `__info` 保持原结构。不添加 current、original 或 manifest.json，解压到设备对应资源目录即可覆盖。只包含选中的资源包（同包的其它修改也会包含）。请基于目标手机版本的原始资源制作；此功能不会把 PC Bundle 转换为 Android Bundle。跨平台贴图可通过 `.jxpack` 应用到目标资源后再导出 ZIP。
- **容错导入**：v2 图包按贴图名定位，同名贴图会尽量写入当前目录的所有位置；缺失项只统计、不终止整包，可切换资源目录后重复导入。
- **资源包级操作**：非贴图资源支持索引、定位和替换所在资源包；资产级音频、模型与动画写回仍依赖对应格式的编码器。
- **备份与还原**：替换前自动保存原始 Bundle，可一键还原全部修改，并支持旧 Mod 迁移。
- **吉星风格界面**：采用应用图标的明快主色和圆角视觉，针对 Windows 150% DPI 及不缩放显示进行了布局适配。

## 动态资源实验版（本地 v2.3.0-preview.12）

preview.12 保留较小的原生视频槽位方案，并恢复手牌 / 事件卡入口，同时新增不改 DLL 的独立技能动画替换。

### 立绘、手牌与事件卡

1. 支持角色立绘 `UT_Hero_Card_*`、手牌 `UT_HandCard_*`、事件 `UT_Event_*` 和地图事件 `UT_MapEvent_*`。拖入视频或 GIF 后可按目标比例拖动、缩放取景；去绿幕默认关闭，需要时手动勾选。
2. FFmpeg 和 CriCodecs 会生成 CRI 播放器可用的颜色 / 透明双流 USM。正式视频最高保留到最长边 2048，使用 Lanczos 缩放、30 FPS、无音轨。
3. 视频数据只替换游戏已有的 `VHandCard_13021002`。角色立绘只改配置返回值；手牌和事件仅在资源名精确命中时把当前 `CardView` 的副本送入游戏已有卡牌视频图层，并拒绝已过期的视频回调。未命中的资源保持原逻辑。
4. 不向 DLL 塞入视频，不创建独立资源类型或播放器。一次只能绑定一个动态目标，并会占用 `VHandCard_13021002`；该异画在其它原生入口中的画面也可能随之改变。切换目标前先点“恢复原资源”。
5. 写入前会备份并校验热更新程序集包和原生视频包。恢复会把首次替换前的两个文件逐字节还原；文件被游戏更新或其它 Mod 改动时会拒绝覆盖。
6. 导出 ZIP 包含这两个已改资源包及 B 服 / Android 所需的同目录 `__info`，保持原始相对路径。PC 与 Android 必须分别用各自资源制作，ZIP 不能跨平台转换。

### 技能动画

1. Addressables catalog 中仍在使用的 `Talent*` Sprite 图集会自动归入对应角色与皮肤，选中后可直接拖入视频或 GIF。
2. 工具按技能原生宽高比裁剪视频，再采样为该技能原有帧数，重建现有分块 Sprite 图集；不会拉伸画面，也不修改 AnimationClip、Sprite 元数据或 `AstralParty.Runtime.dll`。
3. 技能动画不占用异画视频槽位，只改当前技能动画 Bundle。写入后会重新打开资源包，核对帧序、图集尺寸并解码首帧；失败时立即回滚本次写入。
4. 恢复操作会还原当前整个技能动画 Bundle，因此同一个包里若还有其它修改，也会一起回到首次备份状态。

旧 preview.9 / preview.10 的独立动态补丁不能直接升级。请先用当时的“恢复原资源”或游戏重置取得干净资源；工具检测到 `Jix.DynamicPortrait` / `JixLoadIndependentPortrait` 后会停止处理，避免在旧补丁上继续叠加。旧版曾产生空类型树的损坏包也会被拒绝。

PC 和 Android 的立绘资源副本，以及当前 PC 手牌 / 事件运行时副本，已通过封包回读、类型结构校验、源文件哈希不变、冲突拒绝、直接覆盖 ZIP 和逐字节恢复。技能动画在真实 `Talent-001` bundle 副本上通过 52 帧重建、首帧解码和原文件不变校验。100% / 模拟 150% 的主界面及技能弹窗布局检查也已通过。所有写入测试都在隔离副本中执行，**仍未完成所有目标的真实游戏内播放和移动端性能验证**；工具中的动态预览只证明转换结果，不代表游戏内已经播放成功。

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

默认产物在 `artifacts/AstralPartyModMaker-v2.3.0-preview.12-win64/`，同名 ZIP 位于旁边。脚本拒绝覆盖已有目录或 ZIP；重复构建请使用新的 `-OutputDirectory`。启动文件使用 SDK 的原生 apphost，以相对路径加载 `data/JixModMaker.dll`，运行时也随包存放，不需要额外启动器运行库或管理员权限。普通 `dotnet build` 的开发输出维持原样。

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
