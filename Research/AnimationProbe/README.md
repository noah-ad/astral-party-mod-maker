# 动态立绘与原生视频槽位调查

本目录记录对《吉星派对》Addressables catalog、Unity bundle、热更新程序集和 CRI Mana 视频资源的离线分析。分析器只读输入；可选提取命令写入新文件，不覆盖游戏资源。

## 已验证资源

- 静态手牌 `UT_HandCard_21002` 是 Texture2D，不是动画本体。
- 对应原生异画视频键是 `VHandCard_13021002`，类型为 `CriWare.Assets.CriManaUsmAsset`。
- PC 视频 bundle：`5eff7bac736d3da4f1f9bc92e083b8ca/642cabe780796a1393ddc285e5d3c4e9/__data`。
- Android 视频 bundle：`cd9ab624d763af6ab2ec6b0974a4e754/0db15afb325f78a327b3a2ae7ab07e00/__data`。
- 对象 PathId：`-1812358608357914351`。
- 视频字节位于 `references -> CriSerializedBytesAssetImpl -> data.Array`，以 `CRID` 开头；原资源的 `assetInfo.loop = 1`。
- PC 与 Android 的 `AstralParty.Runtime.dll` 分别位于各平台的热更新程序集 bundle 中，目标平台标识为 19 和 13。

CRIWARE 对 `CriManaUsmAsset` 的说明：

- https://game.criware.jp/manual/unity_plugin_en/latest/contents/classCriWare_1_1Assets_1_1CriManaUsmAsset.html
- https://game.criware.jp/manual/unity_plugin_en/latest/contents/addon4u_assetsupport_assets_sofdec_playback.html

## 当前实现

preview.12 不再向热更新 DLL 注入独立视频资源，动态立绘、手牌和事件共用一个游戏已有的视频槽位：

1. FFmpeg 将视频或 GIF 解码、裁剪并生成颜色与透明度画面。
2. CriCodecs 将两路 MPEG-1 数据封装成 USM。
3. 工具只替换 `VHandCard_13021002` 对象内原有的 USM 字节，并保持循环播放。
4. 角色立绘通过 `JixMapAnimatedPortrait` 精确映射到 `(VHandCard_13021002, true)`，其它返回值保持不变。
5. 手牌、事件和地图事件通过 `JixMapAnimatedCard` 克隆当前 `CardView`，只替换副本的 Key / IsVideo；卡牌渲染器继续使用原有前景视频图层，并记录请求 key，拒绝异步返回到已被复用图层的旧视频。
6. 游戏原有的 `UI.HeroPanel`、卡牌视频图层和 `CriMovieManager` 负责加载、显示与循环，补丁不新增播放器或资源类型。

原静态 Texture2D 不会修改。这个方案一次只能绑定一个动态目标，并占用原异画槽位；原游戏其它使用该槽位的位置也可能显示替换后的视频。

技能动画使用另一条独立路径：解析 Addressables catalog，把仍在使用的 `Talent*` 图集归到对应角色 / 皮肤；视频按原生比例裁剪并采样到原帧数后，只重建现有 Sprite 图集像素。AnimationClip、Sprite 元数据、热更新 DLL 和异画槽位都不修改。

## 封包保护

- 修改前要求程序集包与视频包属于同一平台和同一资源根目录。
- 只接受 Windows 64 位平台 19 与 Android 平台 13。
- 目标 DLL 必须只有一个，视频包必须只有一个带 CRID 数据的 `VHandCard_13021002`。
- 使用 `AssetsFile.GetScriptIndex(info)` 保留现代 Unity 对象的实际脚本类型索引。
- 重写后比较引擎版本、平台、TypeTree 开关、完整类型树、引用类型及每个对象的 PathId / 类型索引。
- 重写后再次打开两个 bundle，逐字节验证 DLL 和 USM，并验证视频名与循环标记。
- 检测到旧 `Jix.DynamicPortrait` / `JixLoadIndependentPortrait` 补丁或空 TextAsset 类型树时停止，不在旧实验包上叠加。
- 导出先在临时目录完成；任何校验失败都不会发布替换 ZIP。

`__info` 是 Unity 缓存元数据。直接覆盖 ZIP 会携带它以保持手机 / B 服目录结构，但本地安装、状态判断和恢复不覆盖其中会随访问变化的时间字段。

## 转换与画质

- 取景范围使用归一化坐标，按目标立绘比例锁定；拖动和缩放都限制在源画面内。
- 非方形像素会先归一化，避免视频被挤压。
- 最长边只在超过 2048 时使用 Lanczos 下采样，不靠放大声称恢复源细节。
- MPEG 4:2:0 只做偶数尺寸对齐，不再强制补到 8 的倍数。
- 透明度直接写入 alpha 流的 Y 平面，保留 0..255 覆盖范围。
- BT.709 等输入先正常解码，再显式转换为 MPEG-1 使用的 BT.601 有限范围。
- 去绿幕默认关闭；开启时会处理深浅绿色，绿色服装仍有被误抠除的风险。

本机 CRI 原生库在隔离进程中可识别测试 USM 的 397 帧、30 FPS 和一个透明流，并完成解码准备到 `Ready`。这不等同于完整游戏 UI 渲染。

## 验证范围

PC 与 Android 的真实资源副本均通过：

- 原始输入哈希不变；
- 替换 ZIP 与恢复 ZIP 路径一致；
- 两个 bundle 的类型结构不变；
- 内嵌 USM 逐字节回读；
- 首次安装、同角色重复替换和持久记录；
- 第二角色占用同一槽位时拒绝；
- 文件被其它程序修改时拒绝恢复；
- 恢复后两个 bundle 与首次替换前逐字节一致。
- 当前 PC 运行时的手牌和事件补丁通过 Cecil 二次写入、所有注入 helper 回读及完整导出 ZIP 校验。
- `Talent-001` 真实 bundle 副本通过 52 帧重建、首帧回读和源 bundle 哈希不变校验。

所有安装测试均在隔离副本中执行，没有自动写入用户游戏。真实 PC / Android 游戏内播放、透明混合、性能和版本校验仍需人工验证。

## 运行方法

从仓库根目录执行：

```powershell
dotnet run --project Research/AnimationProbe -- catalog <catalog.json> 21002
dotnet run --project Research/AnimationProbe -- inspect <bundle路径>
dotnet run --project Research/AnimationProbe -- inspect <bundle路径> <新输出文件.usm>
dotnet run --project Research/AnimationProbe -- verify-native-slot <补丁后的AstralParty.Runtime.dll>
```

分析器面向当前已研究资源结构，不作为通用 Unity 资源编辑器。
