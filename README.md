# 吉星 Mod 制作器 (Astral Party Mod Maker)

面向《吉星派对 / Astral Party》的 Windows 图形化 Mod 制作工具。它可以自动定位游戏资源，浏览、预览和替换贴图，并把作品导出为 `.jxpack` 图包或 Bundle ZIP。

[![Latest Release](https://img.shields.io/github/v/release/noah-ad/astral-party-mod-maker?display_name=tag&label=最新版本&color=ff5fa2)](https://github.com/noah-ad/astral-party-mod-maker/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/noah-ad/astral-party-mod-maker/total?label=累计下载&color=55c8e8)](https://github.com/noah-ad/astral-party-mod-maker/releases)

> Unity 2021.3 + Addressables / C# / .NET 8 / WinForms / Windows only

## 下载

稳定发行版：**v2.2.1**。当前源码及本地实验版：**v2.3.0-preview.9**。

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

## 独立动态立绘实验版（本地 v2.3.0-preview.9）

**封包修复：**preview.4 错用了对象表中的旧版脚本索引，将 TextAsset 的实际索引 `65535` 写为 `0`，额外生成空类型树。preview.6 改用目标文件的正确索引，并校验封包前后类型表及每个对象的类型索引一致，恢复动态立绘替换功能。仍须使用游戏重置后的干净资源；旧版损坏资源不能叠加应用。这个已确认的封包错误不能解释所有登录故障，游戏实际加载仍需验证。

稳定发行版 v2.2.1 不包含此功能。本地实验版支持把视频 / GIF 直接拖到角色 `UT_Hero_Card_*` 资源卡或右侧预览上，也可点击右侧 **替换动态立绘** 按钮。操作区位于资源名下方，信息随文字自动增高，小窗口可以滚动。半身、头像等未接入立绘视频分支的资源不会启用此按钮。

**preview.8 画面修正：**独立视频在游戏播放控件中按自身比例居中显示，不再铺满不同宽高的矩形；不改变控件的布局、锚点与关联。透明流直接保留 0..255 覆盖率，修正旧流程将其压缩为 16..235 的问题；BT.709 等输入先正确解码，再显式转换到 MPEG-1 使用的 BT.601 色彩矩阵，避免色彩标记丢失后偏色。动态预览提高到最长边 960，并逐帧生成调色板。需要用原始视频重新执行替换，旧 ZIP 中的视频不会随工具升级而改变。

1. 拖入视频后显示完整源画面和固定比例取景框，比例来自选中的立绘尺寸。拖动框调整位置，用滑杆调整范围，支持居中和方向键微调；切到“动态预览”查看按同一范围裁剪后的前 6 秒。点击“替换”后自动转换、定位、备份和写入，不需要配置编码器。
2. 去绿幕默认关闭，需手动勾选；开启时处理深绿幕并保留输入透明区域，绿色服装仍可能被误抠除。正式视频完整转换，按源尺寸保留细节，最长边上限提高至 2048，使用 Lanczos 缩放和更高质量编码，30 FPS、无音轨。不会靠放大恢复源视频不存在的细节；颜色和透明流各自达到约 128 MB 时拒绝写入。
3. 自动在目标目录的 `_原始备份/动态立绘/` 下保存 ZIP 和替换记录。写入后校验所有文件哈希，主预览显示动态效果和“已写入并校验”状态；这只代表本地文件验证，不代表游戏内播放验证。静态 Texture2D 本身不变。游戏更新、重置或资源变化后，不再将旧记录显示为有效替换。
4. 每个角色立绘有独立的视频键和数据，可以添加多个角色或更新同一角色。“恢复全部动态立绘”还原到第一次动态替换前，而非上一份动态补丁。资源被其他程序改动时会拒绝恢复，避免覆盖新改动。Unity 的 `__info` 访问时间不参与有效性判断，本地安装和恢复不覆盖它。替换与恢复前请关闭游戏。
5. “导出 ZIP”只包含新增独立资源所在的热更新程序集包及配套元数据，保留原始覆盖路径，不包含原异画视频包。同一程序集里的其它独立动态立绘会一起导出。Android 资源在电脑上修改后通过此 ZIP 传回手机。必须使用目标平台的资源制作，不会把 PC Bundle 转成 Android 格式。

本地版本使用 FFmpeg 8.1.1、Python 3.11.9 嵌入运行时与 CriCodecs 1.2.0 自动生成 MPEG1 颜色 / 透明双流 USM，不再需要 Sofdec。GIF 与实际 MP4 转换、双流数据哈希回读、深绿幕处理和取消操作已通过测试。本机游戏原生 CRI 库在隔离进程中识别了测试视频的 397 帧、30 FPS、透明通道，并完成解码准备到 `Ready`；这不等同于完整游戏 UI 渲染测试。第三方二进制未纳入 Git；CriCodecs 再分发许可尚待确认，当前不发布含这些依赖的公共安装包。

新原理：将视频作为独立命名的数据块加入 `AstralParty.Runtime.dll`，不覆盖 `VHandCard_13021002` 或其它异画槽位；立绘配置返回 `JixPortrait_<贴图名>`，新增加载分支创建自己的 `CriManaUsmAsset`，填入新视频的宽高、帧数、帧率、透明流和循环信息。释放时销毁自己的资产，不调用 Addressables 释放异画。仍是热更新代码修改，并非单纯替换 Texture2D；游戏更新后需要用新资源重新制作。

旧版借用槽位补丁会被拒绝叠加。请先使用对应旧备份恢复，或选择干净的资源副本制作；新版不会自动恢复或覆盖旧异画。preview.8 已通过独立资源加载/缓存/释放的托管测试、不同显示框的等比网格测试、裁剪位置与比例测试、1080p 保留测试、100% / 模拟 150% 界面检查，以及 PC / Android 资源副本的连续安装和首次备份恢复测试。透明渐变编码误差不超过 1/255，BT.709 测试色块最大通道误差 5/255；实际裁剪样片也通过本机 CRI 解码准备。这些不代替完整游戏内验证。

**限制与风险：**只适用于调用原生立绘视频分支的界面，不保证头像、细卡、所有战斗画面都动态化。独立视频增加程序集大小和运行内存，请优先使用短片；两端必须分别用各自版本的资源制作，ZIP 不可混用。已通过两端本地资源的封包回读和恢复内容校验，但**未完成两端游戏内播放、透明通道、性能与兼容性验证**；不要当作稳定功能分发。没有绕过游戏校验机制，游戏可能拒绝加载修改后的程序集。

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

默认产物在 `artifacts/AstralPartyModMaker-v2.3.0-preview.9-win64/`，同名 ZIP 位于旁边。脚本拒绝覆盖已有目录或 ZIP；重复构建请使用新的 `-OutputDirectory`。启动文件使用 SDK 的原生 apphost，以相对路径加载 `data/JixModMaker.dll`，运行时也随包存放，不需要额外启动器运行库或管理员权限。普通 `dotnet build` 的开发输出维持原样。

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
