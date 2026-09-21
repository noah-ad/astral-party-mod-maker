# 动态卡面与皮肤资源调查

本工具只读取 Addressables catalog 和 Unity bundle。可选的第三个参数将内嵌 CRID 数据提取为新文件，不覆盖已有文件，不修改游戏资源。

## 已验证的本地证据

- catalog：本机 `catalog_3.2.0.json`。
- `UT_HandCard_21002` 是 Texture2D，不是动画本体。
- 对应异画动画：`VHandCard_13021002`，类型为 `CriWare.Assets.CriManaUsmAsset`。
- 动态卡面 bundle：`642cabe780796a1393ddc285e5d3c4e9`。
- 缓存父目录：`5eff7bac736d3da4f1f9bc92e083b8ca`。
- 对象 PathId：`-1812358608357914351`。
- 数据字段：`references` → rid 1000 → `CriSerializedBytesAssetImpl` → `data.Array`。
- 内嵌视频大小：7,956,384 字节，文件头 `CRID`；`assetInfo.loop = 1`。
- 安全版本：`VHandCard_13021002_sfw`，bundle `bc6e4960ec17091e629c931f1780f8f8`（仅验证 catalog 映射）。
- 对照皮肤 `VSkin_100121002`：bundle `dfc2b26f8e085a1dd9f4478a4f24025f.bundle`，相同脚本引用与数据实现，内嵌 CRID 数据 2,513,408 字节，`assetInfo.loop = 0`。
- 两类资源共同依赖 bundle `9108fa33debf1c8ac9d7931fe7f40d57.bundle`。

提取的卡面文件保存在本地 `发布/VHandCard_13021002.usm`，不纳入 Git。

## 可行性与边界

动画来自 CRI Mana 视频资源，不是 Texture2D 的 GIF 帧。CRIWARE 官方将 CriManaUsmAsset 定义为导入的 USM 文件，由对应播放组件播放：

- https://game.criware.jp/manual/unity_plugin_en/latest/contents/classCriWare_1_1Assets_1_1CriManaUsmAsset.html
- https://game.criware.jp/manual/unity_plugin_en/latest/contents/addon4u_assetsupport_assets_sofdec_playback.html

优先研究已有 VSkin 动态槽位的视频替换，具备明确的数据结构依据。但尚未进行重新封包、游戏内播放、透明通道或移动端兼容性验证。不能保证任意 USM 都可直接播放。

普通静态立绘能否动态化仍未确认。仅替换 Texture2D 不会自动添加视频播放器；需要继续追踪实际加载逻辑、皮肤配置、UI 播放组件以及触发条件。VSkin 命名和类型证明有皮肤视频资源，不证明所有立绘展示位置都支持它。

GIF/MP4 导入需要兼容的视频编码和封装流程，不能通过改扩展名实现。初步调查阶段未修改应用 UI；后续实验实现见下节。

## 静态立绘实验补丁（后续实现）

从热更新包提取并检查 `AstralParty.Runtime.dll` 后发现：

- `SkinStandingPaintingConfigureItem.GetCharacter()` 返回 `(string, bool)`，当前各分支均将布尔值写为 false。
- `GetCharacterInGame()` 可返回独立战斗立绘，也可回退到 GetCharacter。
- `UI.HeroPanel/<RendererSkin>d__32.MoveNext()` 已有 isVideo 分支，使用 `loader_Skin_Video`、`CriMovieManager.Play`，静态分支则使用 `loader_Skin_Image`。
- `PreviewVideo` 单独控制预览按钮，不等同于常驻立绘。因此只修改预览配置不符合静态立绘动态化需求。

应用新增独立实验导出器：将这两个配置方法的 tuple 构造调用替换为受限映射函数，匹配选中贴图名时返回视频名与 true，其他输入保持原样。沿用原生 UI 播放和销毁流程，不安装 Windows 注入插件。仍属热更新代码修改，不能据此保证手机或 PC 会接受并播放。

本地 PC 输入平台标识为 19，Android 为 13。两端均通过程序集和视频封包回读、源文件哈希不变、恢复 ZIP 内容校验。Android 输入位置：

- 程序集：`4da1f23c112208b0f8df2a1327b4e4ce/0d039c7bb95a1431c52640822ea6f68b/__data`
- 动态卡面：`cd9ab624d763af6ab2ec6b0974a4e754/0db15afb325f78a327b3a2ae7ab07e00/__data`

AssetsTools.NET 2.x 直接重写此 managed-reference registry 会生成无法回读的数据，因此补丁保留原对象布局，校验并替换唯一的视频字节块和循环标记，随后完整回读验证。视频槽位会被占用，原卡面的展示也可能改变。生成的 smoke ZIP 仅用于验证，没有安装到游戏。

## preview.6 修复与验证

- 实际日志只能确认热更新 bundle 加载失败，不能据此断言所有登录问题的唯一原因。
- 与重置后资源比较，旧补丁新增了 TypeId 49、ScriptTypeIndex 0、Nodes 空的类型，目标 DLL 对象被改指向此类型。原类型的 ScriptTypeIndex 为 65535。
- 原因：直接读取 `AssetFileInfo.ScriptTypeIndex` 对现代格式不正确。修复为 `AssetsFile.GetScriptIndex(info)`，从实际类型表获取索引。运行时包与视频包都改用此 API。
- 新的 VerifyTypeLayout 校验平台、引擎版本、完整类型树和引用类型、每个对象的类型索引。已确认旧损坏包会被拒绝，新包保留原类型表。
- PC、Android 均在隔离副本完成首次安装、重复安装、首次备份恢复、修改冲突拒绝、原文件哈希不变测试。
- `__info` 是 Unity 缓存访问时间元数据，会随正常游戏访问改变。不参与实际资源状态判定，本地安装/恢复也不覆盖它。
- `Research/CriManaProbe/probe.py` 仅用于本机研究，加载本机游戏 DLL 到独立进程，不连接运行中的游戏。测试转换视频识别出 397 帧、30 FPS、一个 alpha 流，解码准备从 Dechead 到 Ready，与原版视频相同。
- 以上不替代 Unity 实际加载、完整游戏 UI 播放、Android 原生解码和性能测试。当前未进行这些验证，不自动写入用户游戏。

## preview.7 独立资源与裁剪

- 不再查找、写入或导出异画视频包。视频分块存放在程序集内独立的 RVA 数据字段中，每块最多 64 KiB，避免创建超大值类型。HybridCLR 源码 `InterpreterImage::InitFieldRVAs` 支持此类数据，不依赖尚未确认的 manifest-resource 流支持。
- `JixPortrait_<贴图名>` 有独立的 Create 工厂与缓存；仅此键进入新加载分支，原 Addressables 分支保持原样。使用已有的 AOT ScriptableObject 和 CRI 资产类型，不引入新的 Unity MonoBehaviour 类型。
- 实际编码器产出的尺寸、帧率、帧数与 USM SHA256 写入转换元数据，并用于构造新资产。通过反射设置原有序列化内部字段，不跨程序集直接访问 internal 字段。`Clear` / `ClearOne` 对新资产走 Destroy，原异画继续走 Addressables.Release。
- 取景范围是归一化坐标，按目标贴图尺寸锁定比例，拖动和缩放均限制在源画面内。预览和正式导出共用裁剪参数。处理非方形像素后裁剪，Lanczos 只向下缩放至最长边 2048；编码 q=1，按 8 像素对齐。
- 执行了两个角色的注入代码测试，验证多资源、同角色更新、缓存、原异画回退、内存数据一致、释放和重新加载。目标 AOT 元数据中 800 处注入方法引用均可解析。
- PC / Android 真实资源副本均通过三次安装、记录保留、原异画不变、冲突检测、首次备份恢复测试。旧槽位补丁明确拒绝叠加。
- 1280x720 样片裁剪为 528x720、30 FPS、397 帧后，原生 CRI 库识别透明流并达到 Ready。1080p 合成片验证输出不再缩到 1024。截图与控件检查覆盖 100% / 150%，拖动及滑杆均改变对应裁剪结果。
- **尚未完成游戏内加载、UI 播放与 Android 解码验证。**独立数据随程序集加载会增加内存与加载体积，不承诺无限角色数量或视频长度。

## preview.8 比例与颜色修正

- `CriMovieManager.Play` 和 `PlaAutoReleaseVideo` 只对独立键安装 `Jix.DynamicRendering.PortraitMesh`。网格在 `contentRect` 中等比居中，完整保留 `uvRect`，不改图形控件的宽高、锚点或关联。重复播放复用适配器；切回原视频、停止播放时还原原网格。
- preview.7 的独立资源没有显式 `AspectRatio` 字段，升级时从已知工厂 IL 中读取宽高；真实旧程序集升级通过校验。PC 升级样例 1368 处、Android 双角色样例 1115 处注入方法引用均能解析到目标元数据。
- 旧透明编码的原始 Y 平面中，完全不透明像素是 235，透明背景是 16。改为将 `alphaextract` 输出直接合并到 Y 平面并配中性 UV，避免灰度转 YUV 的范围压缩。编码后实体 255、背景 0，256 级渐变最大误差 1。
- RGB 到 MPEG-1 显式使用 BT.601 有限范围。测试输入包含 BT.709 标记，解码后色块 RGB 最大误差 5；未做色彩转换就丢弃矩阵标记会出现偏色。
- 保留 q=1 和标准量化矩阵。试验过全 8 自定义矩阵，虽提高部分样片指标，但会使高对比块的 DCT 系数截断，导致红色色块严重失真，因此未采用。没有用锐化或无依据的放大声称恢复源细节。
- MPEG 4:2:0 仅对齐偶数尺寸，不再补成 8 像素倍数。样片现在为 526x720、397 帧、30 FPS，原生解码器识别一个透明流并到达 Ready。相对同裁剪、同颜色转换的未压缩参考，前 60 帧 YUV SSIM 为 0.998497；不包含源视频自身损失，也不是游戏截图指标。
- 右侧新增直接替换按钮，操作前移至标题下方；标签按内容换行，窄窗口纵向滚动。测试覆盖 1400x860、1100x640，100% 和模拟 150% 缩放，按钮启用范围、点击打开、标签高度、横向溢出与行重叠。
- PC / Android 的隔离副本均完成三次替换、记录保留、异画不变、冲突拒绝、首次备份恢复。用户游戏文件未写入，**实际游戏播放、原生透明混合与 Android 解码仍待验证**。

## 运行方法

从仓库根目录执行：

```powershell
dotnet run --project Research/AnimationProbe -- catalog <catalog.json> 21002
dotnet run --project Research/AnimationProbe -- inspect <bundle路径>
dotnet run --project Research/AnimationProbe -- inspect <bundle路径> <新输出文件.usm>
```

分析器面向本次 catalog 的结构，尚未作为通用用户导入功能提供。
