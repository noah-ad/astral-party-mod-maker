# 吉星 Mod 制作器 (Astral Party Mod Maker)

面向《吉星派对 / Astral Party》的 Windows 图形化 Mod 制作工具。它可以自动定位游戏资源，浏览、预览和替换贴图，并把作品导出为 `.jxpack` 图包或 Bundle ZIP。

[![Latest Release](https://img.shields.io/github/v/release/noah-ad/astral-party-mod-maker?display_name=tag&label=最新版本&color=ff5fa2)](https://github.com/noah-ad/astral-party-mod-maker/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/noah-ad/astral-party-mod-maker/total?label=累计下载&color=55c8e8)](https://github.com/noah-ad/astral-party-mod-maker/releases)

> Unity 2021 / 2022 + Addressables / C# / .NET 8 / WinForms / Windows only

## 下载

最新发行版：**v2.3.6 完整视频版**。修复动态替换误报“程序集未下载”、旧 Mod 记录识别和混合来源 ZIP 导出，保留高 DPI 布局与怪兽立绘补齐。

**随包提供 FFmpeg、Python 和 CriCodecs，完整解压后不需要再下载视频组件。** 同时核对用户缓存和游戏安装目录缓存，按当前 catalog 精确定位程序集，不会因为用户目录只留下旧包而误报未下载。

- [下载 v2.3.6 Win64 完整视频版](https://github.com/noah-ad/astral-party-mod-maker/releases/download/v2.3.6/AstralPartyModMaker-v2.3.6-win64-full.zip)
- [查看 v2.3.6 更新说明](docs/releases/v2.3.6.md)
- [查看全部版本与更新说明](https://github.com/noah-ad/astral-party-mod-maker/releases)

**v2.3.6 完整视频版直接拖入视频 / GIF 即可转换，无需启用下载。** 只有旧标准版或显式构建的精简标准版需要首次点击“下载并启用”，从 FFmpeg 构建发布者、Python 官网及 PyPI 下载固定版本组件并校验 SHA256。完整视频版不走这个下载流程。

完整包可以在没有用户组件缓存的机器上离线转换视频。只浏览静态资源、检查组件或生成预览都不会改动游戏文件。独立动态立绘 / 手牌 / 事件仍属于实验功能，游戏内启动和 Android 兼容性限制见下文。

下载 ZIP 后完整解压，双击顶层的 `吉星Mod制作器*.exe`。发行包已包含 .NET 8 运行环境，无需另外安装 SDK 或 Desktop Runtime。

### 新版启动方式

文件夹、ZIP、启动 EXE 和窗口标题统一标出版本号。便携包顶层只有两项：

```text
吉星Mod制作器-v2.3.6-完整视频版.exe
data/
```

双击顶层 EXE 即可。不需要打开 `data`、寻找 DLL 或运行 BAT，也不需要另装 .NET。启动 EXE 与 `data` 必须放在一起。可以把整个文件夹移到其它位置，也可以给 EXE 创建桌面快捷方式；不要只移动 EXE，也不要直接在压缩包内运行。

版本号由项目构建信息统一生成，`data/package.json` 记录版本、类型、启动文件、组件启用方式及构建时间。完整版的转换组件位于 `data/Tools/video`，“工具 / 维护”仅做本地检查。旧标准版安装的组件仍位于 `%LOCALAPPDATA%\JixModMaker\video-runtime`，可供精简包复用。

该布局不改变游戏 Mod 的直接覆盖 ZIP。第三方程序随完整发行包提供，不提交到 Git 源码历史。版本、原始分发包、校验值、源码入口和条款说明见 [转换组件说明](App/Tools/video/DEPENDENCIES.md)。

## 功能

**v2.3.6** 补全安装目录缓存定位：当前游戏 3.3.0 的程序集可位于 `AstralParty_CN_Data/com.unity.addressables/AssetBundles`，不能仅检查 `LocalLow`。程序、独立视频、备份和直接覆盖 ZIP 使用同一缓存布局，安装目录模式通过 `Application.dataPath` 读取视频；Android / 用户缓存模式仍使用 `persistentDataPath`。保留旧动态还原入口，两处均有当前包或 catalog 冲突时停止写入。见 [v2.3.6 更新说明](docs/releases/v2.3.6.md)。

v2.3.6 同时集成 **v2.3.4** 的旧 Mod 记录关联和修改状态识别：同一资源包按贴图名兼容变化后的资源编号；后台对比现有原始备份，恢复缺失的修改记录。卡片和详情区分“已修改”“备份一致”“有记录”“未核验”，核验结果会缓存。保留全部历史记录，导出默认排除已还原和不在当前索引中的旧条目。见 [v2.3.4 更新说明](docs/releases/v2.3.4.md)。

v2.3.6 同时集成 **v2.3.3** 的混合来源导出修复：一次导出自动生成“基础资源”和“热更新缓存”两个直接覆盖 ZIP；只选一种来源时仍使用所选的单个 ZIP 文件名。见 [v2.3.3 更新说明](docs/releases/v2.3.3.md)。

**v2.3.2** 包含 v2.3.1 的怪兽立绘补齐：识别 `UT_Monster_Card_*`、`UT_Monster_Bust_*` 等怪兽贴图，包括 `UT_Monster_Card_1071`。它们显示在“角色 / 皮肤 / 怪物 → 怪兽”，卡片标签带怪兽编号，和谐版本与皮肤仍分别保留。已有完整、轻量索引会自动刷新分类，不因分类调整重新扫描未变化的 Bundle；游戏更新后的新增包仍走正常增量扫描。此改动只涉及资源浏览与静态贴图操作，不增加怪兽动态补丁。

高 DPI 下统一按 Windows 缩放比例排版。资源筛选栏在空间不足时分行，作品操作、导出筛选和图片裁切按钮自动换行；资源详情保留纵向滚动。窗口在打开或切换不同 DPI 的屏幕后会适配工作区，避免按钮落到屏幕外。

- **自动检测游戏**：自动查找常见 Steam 安装位置，也可手动选择游戏资源目录。
- **PC / B服目录直读**：同时支持普通 `.bundle` 目录和 B服 `哈希目录/__data + __info` 套壳目录；误选 B服哈希子目录时会自动回溯到 `AssetBundles` 根目录。
- **快速增量索引**：正常启动使用游戏目录与 Addressables catalog 指纹判断更新，不再逐个访问数千个热更包；发现新目录或 catalog 更新后才增量重扫。preview.14 修复 Windows 路径大小写不同导致数千包被误判更新的问题。原生技能视频直接从 catalog 加入索引，按需预览。音频、文本、模型和动画可在“工具 / 维护”中执行完整扫描。
- **Mod 记录兼容与核验**：读取资源根目录原有的 `.jixmod.json`，同包按稳定名称关联旧编号；保存使用原子替换并保留 `.bak`。主文件损坏时读取上次有效记录并显示提示，无法解析的原文件仍会保留。有 `_原始备份` 时在后台按贴图实际数据比对，识别 RGBA32、ASTC 等格式的修改，不根据编码格式猜测。核验不修改游戏资源，未变化的包复用缓存；“工具 / 维护”和作品页均可重新核验。
- **选择导出范围**：图包和已改 Bundle ZIP 均支持勾选、搜索、最近 24 小时及最近 7 天筛选。旧记录没有修改时间，显示为“时间未知”；新替换和导入自动记录时间。Bundle ZIP 导出整个资源包，包含同包中的其它修改。
- **精简分类**：隐藏空分类，动作帧、特效和其它低频资源可按需展开；支持 06 及更高编号皮肤。
- **统一资源浏览**：角色卡面、细卡、半身、头像、角色照、升级图、技能特写和怪物立绘统一归入角色资源。原生 `VSkill_Hero...` 特写按角色、皮肤分组；`Talent...` 图集明确标为“Q版动作”，不再误称技能特写。
- **角色与皮肤树**：角色栏支持展开角色和皮肤、搜索及快速定位，不需要逐页翻找。
- **直观替换与预览**：把图片拖入资源卡即可替换，也可双击选图；支持预览、智能裁切及原图导出。
- **拖动导出**：直接把贴图预览拖到资源管理器即可导出 PNG，资源列表连续滚动、不分页。
- **直接覆盖 ZIP**：保留当前资源根目录下的原始相对路径和文件名，手机端 `哈希目录/__data` 与配套 `__info` 保持原结构。不添加 current、original 或 manifest.json，解压到设备对应资源目录即可覆盖。v2.3.3 混选基础资源和热更新缓存时自动导出 `文件名-基础资源.zip`、`文件名-热更新缓存.zip`，分别覆盖到对应目录，无需手动拆选。只包含选中的资源包（同包的其它修改也会包含）。请基于目标手机版本的原始资源制作；此功能不会把 PC Bundle 转换为 Android Bundle。跨平台贴图可通过 `.jxpack` 应用到目标资源后再导出 ZIP。
- **容错导入**：v2 图包按贴图名定位，同名贴图会尽量写入当前目录的所有位置；缺失项只统计、不终止整包，可切换资源目录后重复导入。
- **资源包级操作**：非贴图资源支持索引、定位和替换所在资源包；资产级音频、模型与动画写回仍依赖对应格式的编码器。
- **备份与还原**：替换前自动保存原始 Bundle，可一键还原全部修改，并支持旧 Mod 迁移。
- **吉星风格界面**：采用应用图标的明快主色和圆角视觉，支持高 DPI、自适应筛选栏和可换行的操作区。

## 动态资源

v2.3.6 完整视频版已包含转换组件；标准版的下载入口作为精简包兼容功能保留。检查完整包的随包组件只做本地验证，不触发下载。

动态替换必须写入当前游戏实际加载的热更新程序集。v2.3.6 从 `settings.json` 指定的版本 catalog 查找 `AstralParty.Runtime.dll`，按 `AssetBundleRequestOptions` 的 bundle 名称和内容哈希，直接检查用户缓存与所选游戏安装目录缓存的对应 `__data`，不全盘扫描。不会按修改时间选择旧 DLL。两处都找不到当前包、两处同时有当前包、同版本 catalog 内容冲突，或旧记录指向不同版本时会说明原因并停止写入；不会删除缓存或把旧 DLL 覆盖到新版本。旧绑定保留还原入口，须先通过工具还原，再对当前版本重新替换。

v2.3.0 解决公开包缺少视频组件的交付问题，不改变 preview.16 的游戏补丁方案，也不把实验性独立动态方案宣称为已完成游戏内验证。

preview.14 保留角色立绘、手牌和事件卡功能，将技能大立绘 / 特写演出改为直接替换原生视频。不会将 Q 版动作当成技能特写，也不会为替换原生技能视频修改热更新 DLL。

preview.15 增加资源详情“还原当前资源包 / 还原动态替换”按钮，以及“工具 / 维护”中的独立动态还原入口。不需要先选择视频，也不依赖转换组件。还原当前包会撤销同包修改并更新作品记录；还原动态替换会恢复记录中的程序集包和视频包，不动其他 Mod。写入前后核对哈希，确认期间资源或备份有变化时拒绝覆盖。

**preview.16 将静态立绘、手牌和事件的动态化改为独立 USM 文件，不再读取或替换 `VHandCard_13021002`。** 复用游戏 CRI 播放器的 `SetFile` 接口，不增加播放器类型、不把视频嵌入 DLL。仍需修改热更新程序集中的目标显示与加载入口，这是未经游戏内验证的实验方案，不保证已解决启动问题。原生技能特写仍直接替换其自身视频包，操作不变。

### 立绘、手牌与事件卡

1. 支持角色立绘 `UT_Hero_Card_*`、手牌 `UT_HandCard_*`、事件 `UT_Event_*` 和地图事件 `UT_MapEvent_*`。拖入视频或 GIF 后可按目标比例拖动、缩放取景；去绿幕默认关闭，需要时手动勾选。
2. FFmpeg 和 CriCodecs 会生成 CRI 播放器可用的颜色 / 透明双流 USM。正式视频最高保留到最长边 2048，使用 Lanczos 缩放、30 FPS、无音轨。
3. 每个目标各用一个 `JixVideos/JixVideo_<原贴图名>.usm`，放在当前程序集所在的 `AssetBundles` 缓存根目录下。Windows 安装目录模式从 `Application.dataPath/com.unity.addressables/AssetBundles/JixVideos/` 读取；Android / 用户缓存模式从 `Application.persistentDataPath/com.unity.addressables/AssetBundles/JixVideos/` 读取，不能混用。直接覆盖 ZIP 解压到该模式对应的缓存根目录。原静态贴图和官方异画视频都不变，手牌 / 事件保留卡牌副本和过期回调保护。
4. 支持多个目标共存，每次从首次备份的程序集重新生成绑定，不在已打补丁的 DLL 上继续叠加。文件缺失时，下一次绘制该资源会使用静态图；也可在 `JixVideos` 内创建名为 `disabled` 的空文件暂停全部独立映射。此回退针对文件缺失 / 禁用，不代表能捕获全部原生解码错误。
5. 写入前保存完整恢复记录，并核对当前文件哈希；出现冲突时拒绝覆盖，写入失败时撤销本次操作。“还原动态替换”会恢复首次备份的程序集、删除本工具新增的全部独立视频，不影响其他 Mod。缺失的自建视频不妨碍还原；内容被其他程序改动则停止还原。旧借槽模式需先还原，再安装新方案。
6. “导出 ZIP”包含当前全部独立绑定需要的程序集资源包、原有 `__info`（存在时）和独立 USM，保持相对路径，可覆盖到**同平台、同游戏版本的 AssetBundles 缓存根目录**。不含工程配置。恢复 ZIP 另有新增文件清单，完整撤销请使用工具的还原按钮，单独解压不能删除新增文件。PC 与 Android 必须分别使用各自程序集；当前实际测试使用 Windows 副本，Android 加载路径、AOT 接口和播放效果尚未验证。

### 技能大立绘 / 特写

1. 根据当前 Addressables catalog 的资源类型和主依赖定位 `VSkill_Hero...` / `VSkill_Monster...`，放在对应角色和皮肤下。预览读取当前 Bundle 中的实际视频。
2. 选中“技能特写”，拖入视频 / GIF 或点击“替换技能特写”。可以拖动取景和缩放，去绿幕手动勾选。资源详情和取景窗口会显示原始时长、帧数；导入后读取输入文件时长，超长时突出提示预计截掉的秒数，替换前再次确认。按原资源尺寸、宽高比、帧率和帧数转换；长视频截断到原时长，短视频停留在末帧，不拉长技能流程。比如 `VSkill_Hero101` 为 40 帧 / 30 FPS，即约 1.333 秒；其他技能以实际读出的数值为准。
3. 只替换所选技能资源中的颜色 / 透明视频。不占用 `VHandCard_13021002`，不修改 `AstralParty.Runtime.dll`，不修改动作图集。保留原有非循环标记、名称、脚本引用、类型树和其他对象。
4. 替换前备份并核对源文件哈希，先在临时包中完成回读和完整结构校验，再原子替换。资源在操作期间被更新时拒绝覆盖；校验失败回退本次改动。
5. 从“作品 / 导出”的已改 Bundle ZIP 中选择技能视频导出。`.jxpack` 仍只存贴图，不包含原生技能视频。恢复会还原该整个 Bundle 的首次备份，同包的其他修改也会一并还原。

### Q版动作

原有 `Talent*` Sprite 图集替换保留，但入口更名为“Q版动作”。它改变棋盘 Q 版角色的动作帧，不是发动技能时的大立绘演出。

### 启动卡在背景

“工具 / 维护”的启动按钮通过 Steam 客户端启动游戏，避免直接运行 EXE 被游戏判定为非 Steam 启动后退出。工具不会修改系统代理、证书或游戏联网代码。

本机排查中，启动日志记录背景请求超时和热更新配置 TLS 校验失败；同一 HTTPS 热更新接口直连成功、经当前代理超时。2026-09-25 的进程级 `NO_PROXY` 对照通过 Steam 初始化，但游戏仍连接本地代理并停在背景；该实验入口未保留在发行包中。**启动问题尚未确认修复，也尚未证明所有动态补丁在游戏内正常**。请保留日志与替换备份，按实际日志区分联网失败与资源加载失败，不要反复叠加或盲目恢复不同版本资源。

旧 preview.9 / preview.10 的独立动态补丁不能直接升级。请先用当时的“恢复原资源”或游戏重置取得干净资源；工具检测到 `Jix.DynamicPortrait` / `JixLoadIndependentPortrait` 后会停止处理，避免在旧补丁上继续叠加。旧版曾产生空类型树的损坏包也会被拒绝。

旧借槽方案的 PC / Android 资源副本曾通过封包与恢复测试；这不代表新的独立文件方案已在 Android 验证。preview.16 通过 369 项回归、真实 Windows 程序集检查，以及立绘 / 手牌 / 事件多目标安装、重复替换和逐字节还原的副本测试；隔离进程中的原生 CRI 库直接读取独立 USM 后到达 `Ready`。preview.14 在真实 `VSkill_Hero101` 副本上验证了 1504×1080、30 FPS、40 帧非循环视频替换、深绿幕去除、不变形、仅单包导出与逐字节恢复；旧 `Talent-001` 图集测试作为 Q 版动作回归保留。100% / 模拟 150% 界面检查也已通过。所有写入测试均在隔离副本执行，**仍未完成所有目标的真实游戏内播放和移动端性能验证**；本地预览不能代替实机验证。

旧 v2.2.1 不包含动态立绘功能，preview.16 核心包没有组件启用入口。请下载最新完整视频版，完整解压后即可转换，无需下载组件。

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
# 本地完整视频版，组件随包提供；生成目录和 ZIP
./scripts/Publish-Portable.ps1 -Zip

# 显式构建精简标准版，首次使用视频需下载组件
./scripts/Publish-Portable.ps1 -WithoutVideoRuntime -Zip

# 显式指定完整视频版的隔离输出目录
./scripts/Publish-Portable.ps1 -WithVideoRuntime -OutputDirectory ./artifacts/local-full
```

完整视频版默认产物在 `artifacts/吉星Mod制作器-v2.3.6-win64-完整视频版/`，同名 ZIP 位于旁边。构建前必须备齐 `App/Tools/video` 下的转换组件和第三方声明，缺少时脚本会失败，不会悄悄生成需要下载的精简版。公开分发前应确认 [第三方组件条款](App/Tools/video/DEPENDENCIES.md)。脚本拒绝覆盖已有目录或 ZIP。对外交付有变化的程序必须先递增项目版本号。启动文件以相对路径加载 `data/JixModMaker.dll`，运行时也随包存放。普通 `dotnet build` 的开发输出维持原样。

可运行 `./Tests/PortablePackage.ps1 -PackageDirectory <发布目录>` 检查根目录结构、图标与版本、中文空格路径、移动后的启动和随包运行库。测试只打开空资源目录，不修改游戏文件。

`dotnet run --project Tests/Regression.csproj -c Release -- --dpi-ui artifacts/dpi-checks` 可运行 100%、125%、150%、200%、250% 的模拟 DPI 布局检查，并在 150%、250% 保存截图。覆盖主界面、筛选、资源详情、导出、视频取景、技能替换、组件安装和图片裁切；模拟检查不替代真实多显示器切换验证。

## 使用

1. 启动程序并等待自动检测；未检测到时，手动选择包含游戏资源的目录。
2. 普通 PC 版选择 `...\StreamingAssets\aa\StandaloneWindows64`；B服选择 `...\com.unity.addressables\AssetBundles`。
3. 在“资源浏览”中选择类型，展开角色与皮肤后选中资源。
4. 将图片拖入卡片完成替换，或把预览拖到文件夹导出 PNG。
5. 在“作品 / 导出”中导出 `.jxpack` 或 Bundle ZIP；在“工具 / 维护”中刷新索引、完整扫描、迁移或还原。

### 修改状态

- **已修改**：当前贴图数据或原生技能视频与对应的首次备份不同。
- **备份一致**：当前内容与备份一致，不再计入当前修改或默认导出。备份可能来自已经装过 Mod 的目录，因此这不是“已通过官方原版验证”。
- **有记录**：保留了旧修改记录，但尚未完成比对，或没有可比对的备份。仍可选择导出。
- **未核验**：没有修改记录，也没有足够证据判断是不是原版；不会仅因贴图为 RGBA32 就判为 Mod。

首次核验只检查有原始备份且仍在当前索引中的包，在后台进行，不阻塞浏览。不再存在的资源记录保留在 `.jixmod.json` 中，但不会误导出旧文件。游戏更新、修改或还原资源后，相关缓存会失效并重新核验。缺少修改记录且没有备份时，工具无法可靠推断资源是否为官方原版。

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
