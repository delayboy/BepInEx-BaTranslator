# 蔚蓝档案注入式翻译插件

本项目基于以下两个先行项目进行探索构建，使用内存函数hook方案，**目前尚未排除封号风险！！！**，**请谨慎考虑使用本项目**。

- 蔚蓝档案steam端静态文本翻译补丁工具项目（无封号风险）：https://github.com/delayboy/BlueArchive-Translator
- BepInEx 少女庭园 UI 内存汉化补丁（使用内存方案，目前未封号）：https://github.com/delayboy/girlsgarden_translate

**使用方式**

下载【BepInEx.6.0.0-be.785-Ba-BaTranslator-v0.1.0.7z】压缩包，拷贝到游戏安装目录下（和BlueArchive.exe放到同一目录），提示文件覆盖时，选择确定。替换完成后，启动游戏会弹出黑色控制台窗口，证明安装成功。首次运行会加载所需文件，导致卡顿或无法登录的情况（无法登录属于正常现象），等待30s左右，关闭游戏，重新登录问题大概率会解决。 

**卸载插件**

直接删除winhttp.dll即可使插件失效，电脑高手可自行清理。

**项目简介**

《蔚蓝档案（Blue Archive）》Steam PC 版的**繁体中文→简体中文**注入式BepInEx 6 IL2CPP 插件。通过 Hook NGUI `UILabel.set_text`，对含汉字的文本做 OpenCC 繁转简后写回。

> BepInEx 插件设计参考《少女庭园》的 TMP 日译中插件。针对BA重构代码，替换为繁简转换方案，技术路线高度简化。

---

## 0. TL;DR

- **游戏**：Blue Archive Steam（IL2CPP, Unity 2021.3.56f2, GameAssembly.dll 247MB）
- **框架**：BepInEx 6 Bleeding Edge `be.785`，IL2CPP 版
- **目标**：繁→简（不是日译中）。字形问题（字体不显示简体）已由静态字体文件替换解决，本插件只做字面转换
- **Hook 点**：`UILabel.set_text`（NGUI，非 TMP/uGUI）。dump.cs 验证 BA 全部文本经此入口
- **转换**：`OpenCCNET.ZhConverter.TWToHans(text, false)` + `ConcurrentDictionary` 缓存
- **语言枚举**：`Language { Kr=0, Jp=1, Th=2, Tw=3, En=4 }`，无简中

---

## 1. 技术路线

### 1.1 框架选型

BA 是 IL2CPP 游戏，**BepInEx 5（Mono）完全无效**。必须用 BepInEx 6 BE 的 IL2CPP 版。

### 1.2 Il2CppInterop 兼容性修补（关键前置，必读）

BA 的 Unity 2021.3.56f2 是 2025 末的 LTS 补丁版，内部 il2cpp 运行时结构已是"Unity 6 形态"。be.785 捆绑的 `Il2CppInterop.Runtime.dll`（v1.5.3）的 `GenericMethod_GetMethod_Hook` 在新版 il2cpp 上会选错目标函数 → 类初始化时 `AccessViolation` 闪退。

**这是上游已知 bug**（Il2CppInterop#235 / BepInEx#1244，至今 open）。MelonLoader 已修（commit `11bdcbfa`），把 shim 选择的 `Last()` 改 `First()`。

本地修复（两个文件改动，均位于 `D:\SteamLibrary\steamapps\common\BlueArchive\Il2CppInterop-1.5.3\`，重编后覆盖 `BepInEx\core\Il2CppInterop.Runtime.dll`）：

1. `Il2CppInterop.Runtime/Injection/InjectorHelpers.cs` — `Setup()` 里让 Unity 2021 也走 Unity6 hook 路径：
   ```csharp
   if (Il2CppInteropRuntime.Instance.UnityVersion.Major >= 6000
       || Il2CppInteropRuntime.Instance.UnityVersion.Major == 2021)
       GenericMethodGetMethodHook_Unity6.ApplyHook();
   else
       GenericMethodGetMethodHook.ApplyHook();
   ```
2. `Il2CppInterop.Runtime/Injection/Hooks/GenericMethod_GetMethod_Unity6_Hook.cs` — override `TargetMethodNotFound()` 跳过而非抛异常（hook 找不到时不崩，仅少装一个透传钩子，不影响 BA）。

> 原版 DLL 备份为 `BepInEx/core/Il2CppInterop.Runtime.dll.orig.bak`。升级 BepInEx BE 后会被覆盖，需重打补丁。源码见 `Il2CppInterop-1.5.3` 目录。

### 1.3 文本入口定位（dump.cs 离线分析）

**不要用 `FindObjectsOfType<TMP_Text>()` 做运行时扫描**——BA 主 UI 是 NGUI，返回 0，且反复扫描游戏进程有反作弊检测风险。

离线方法：用 Il2CppDumper 生成 `dump.cs`，grep 继承关系：

```
grep ": TMP_Text" dump.cs        # 只有 Unity 自带 TextMeshPro/TextMeshProUGUI，无 BA 自定义子类
grep "class UILabel" dump.cs     # public class UILabel : UIWidget  ← BA 文本唯一入口
```

### 1.4 字体不是问题

这是本方案能走通的前提：
1. BA 自带繁中资源（`Language.Tw=3`），文本本身是繁体，字形已存在
2. 用户已通过静态替换游戏字体文件，让繁中字体支持简体字形

所以 hook 只需做字面 `學→学` 转换，无需处理字体 atlas / TTF 切换。

---

## 2. 代码结构

### 文件清单（`translate-master/MuvluvUiTranslate/`）

| 文件 | 职责 |
| :--- | :--- |
| `Plugin.cs` | BasePlugin 入口。Load 时同步初始化 OpenCC（失败则不挂 hook，游戏原样运行不崩）。PatchAll |
| `Config.cs` | 单个 `Translation/Enabled` 开关（BepInEx ConfigFile） |
| `TmpTextPatches.cs` | `UILabel.set_text` Prefix hook + 繁简转换 + 缓存 |
| `MuvluvUiTranslate.csproj` | `CopyLocalLockFileAssemblies=true` 保证 OpenCCNET.dll/JiebaNet.dll 复制到输出 |

### 核心逻辑（TmpTextPatches.cs）

```csharp
[HarmonyPatch(typeof(UILabel), "set_text")]
internal static class UILabel_SetTextPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref string value) => TextFlow.ProcessIncoming(ref value);
}

// TextFlow.ProcessIncoming:
// 1. null/空 或 Enabled=false → 放行
// 2. 不含 CJK 汉字(\u4E00-\u9FFF) → 放行（英文/日文/数字不处理）
// 3. ConcurrentDictionary 缓存命中 → 直接用
// 4. 未命中 → ZhConverter.TWToHans(value, false) → 写缓存 → 返回
value = Cache.GetOrAdd(value, k => ZhConverter.TWToHans(k, false));
```

**关键设计点**：
- `ContainsHan` 预筛：纯英文/假名/标点文本直接返回，避免无谓转换与缓存膨胀
- `ConcurrentDictionary.GetOrAdd`：打字机效果、逐帧重设同一串只查一次表
- `try/catch` 吞噪：钩子内异常绝不冒泡到游戏渲染线程
- `TWToHans(text, false)`：等价 python `OpenCC('t2s.json')`，且带 TW 词汇表（比原版 t2s 更准）。`false` 关成语转换（BA 用不上，省加载）

---

## 3. 构建与部署

### 依赖

- .NET SDK 8+（项目用 net6.0 target，但需要 SDK 8/9/10 构建，`global.json` 已设 `rollForward=latestMajor`）
- 游戏目录 `D:\SteamLibrary\steamapps\common\BlueArchive`
- BepInEx 6 BE be.785 已安装且运行通过（含本地 Il2CppInterop 补丁）

### 构建

```bash
cd translate-master/MuvluvUiTranslate
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\BlueArchive"
```

构建直接输出到 `BepInEx/plugins/MuvluvUiTranslate/`，包含 DLL + OpenCC 词库（`Dictionary/`、`JiebaResource/`）。

### 输出目录结构

```
BepInEx/plugins/MuvluvUiTranslate/
├── MuvluvUiTranslate.dll          # 插件本体
├── OpenCCNET.dll                  # OpenCC NET 移植
├── JiebaNet.*.dll                 # 分词依赖
├── Dictionary/                    # 14 个 OpenCC 词库（TWVariants/STCharacters 等）
├── JiebaResource/                 # 10 个 jieba 分词资源
├── runtimes/                      # 传递依赖的平台库
└── (System.Drawing.Common 等)    # 传递依赖，非必需但复制了
```

### 配置

首次运行生成 `BepInEx/config/MuvluvUiTranslate.cfg`：

```ini
[Translation]
Enabled = true   # 关闭则所有文本原样放行
```

---

## 4. 环境全貌（game root 下）

```
BlueArchive/
├── BepInEx/                          # 框架
│   ├── core/Il2CppInterop.Runtime.dll  # ★ 本地补丁版，原版备份 .orig.bak
│   ├── unity-libs/2021.3.56.zip      # ★ 实为 2021.3.45 改名（服务器无 3.56）
│   └── plugins/MuvluvUiTranslate/    # 本插件产物
├── BepInEx-6abdba47.../              # be.785 源码（只读参考）
├── Il2CppInterop-1.5.3/              # ★ 补丁构建源，重编用
├── dump.cs                            # 游戏全部 il2cpp 类（42万行，离线分析用）
├── 相关文档/                          # ExcelDB 密钥逆向、字体分析等历史资料
└── translate-master/                  # 本插件源码
```

### 关键文档（`相关文档/`）

- `6月到8月游戏密钥hook点迁移总结.md` — 跨版本 hook 迁移方法论 + CE 特征码
- `PC端蔚蓝档案-UI剧情文本汉化解包逆向计划.md` — ExcelDB.db SQLCipher 密钥逆向全过程
- `字体配置分析.md` — 字体文件结构

---

## 5. 遗留问题与已知风险

### 5.1 ★ Il2CppInterop 补丁需随框架升级重打

`BepInEx/core/Il2CppInterop.Runtime.dll` 当前是本地重编的补丁版。任何 BepInEx BE 升级（覆盖 core/）都会还原为有 bug 的原版，需重跑 `Il2CppInterop-1.5.3/` 的构建并替换。

### 5.2 ★ OpenCC 传递依赖冗余

`System.Drawing.Common`/`System.Windows.Extensions`/`runtimes/` 等是 jieba.NET 误拉的传递依赖，OpenCC 转换路径用不到。当前全部复制到 plugins 目录，未做精简。**功能不受影响**，但目录臃肿，且 `System.Drawing.Common` 在非 Windows 平台会抛 `PlatformNotSupportedException`（BA 仅 Windows，暂无碍）。如需精简，用 `<PackageReference Include="OpenCCNET" ExcludeAssets="runtime" />` + 显式只引用需要的 dll，但风险是可能破坏 jieba 初始化。

### 5.3 updateLabel / prefab 烘焙路径未覆盖

当前只 hook `set_text`。dump.cs 显示 NGUI 还有 `UpdateLabel()`（重新生成文本主入口）和 prefab 反序列化直写 `mText` 字段两条可能漏网路径。若实测发现部分文本未转换，按以下顺序排查：

1. F8 普查（已随旧 UiTranslateManager 删除，如需恢复可参考 git 历史）
2. 补 hook `UILabel.UpdateLabel` Postfix，对 `mText` 字段做转换后回写
3. 补 hook `UILabel.OnEnable` Postfix（prefab 烘焙兜底）

### 5.4 词库路径依赖

`Plugin.InitOpenCC()` 用 `Path.GetDirectoryName(Assembly.Location)` 定位词库。IL2CPP 下插件 dll 的 `Assembly.Location` 指向 `plugins/MuvluvUiTranslate/MuvluvUiTranslate.dll`，词库在同目录 `Dictionary/`。若 BepInEx 改变加载方式导致 Location 异常，需改用 `AppDomain.CurrentDomain.GetAssemblies()` 反查或硬编码 `Paths.PluginPath`。

### 5.5 未验证：BA 反作弊

BA 用 nProtect GameGuard（`nxinface.enconfig.json`）。BepInEx 注入本身是被检测的主因，本插件的 Harmony hook 是在注入成功之后。目前未触发检测，但 OpenCC 首次转换耗时（词库加载）若卡在敏感时序上有理论风险。已通过"启动时同步初始化"规避：词库在 Chainloader 阶段加载完毕，hook 命中时只查表。

### 5.6 未验证：性能

`ConcurrentDictionary` 缓存命中后是纯内存查表，但首帧击穿缓存时每个新文本串要做 jieba 分词 + 多表查表转换。BA UI 文本量大时启动初期可能有瞬时开销，未做 profile。若玩家体感卡顿，可考虑预热（启动时遍历 dump 常见文本转换一次）。

---

## 6. 后续 Agent 上手清单

1. **先读** `相关文档/6月到8月游戏密钥hook点迁移总结.md` 了解 BA modding 总体环境与反作弊处理
2. **确认 Il2CppInterop 补丁在位**：`BepInEx/core/Il2CppInterop.Runtime.dll` 大小应为 ~288KB（原版 ~284KB），若被还原则重编 `Il2CppInterop-1.5.3/`
3. **改 hook 范围**：参考 §5.3，是更新换代最可能动的地方
4. **字体若出问题**：本插件不处理字体，看 `字体配置分析.md` 和用户的静态字体替换方案
5. **构建命令**：见 §3，一行 `dotnet build -c Release -p:GameDir=...`
6. 仓库根的 `README.md` 和几份计划书是**旧项目**(少女庭园 TMP)的，别被误导

---

## 7. 关键文件锚点速查

| 想找什么 | 去哪 |
| :--- | :--- |
| 游戏 il2cpp 全部类型 | `dump.cs`（grep 继承关系） |
| Il2CppInterop 补丁源 | `Il2CppInterop-1.5.3/Il2CppInterop.Runtime/Injection/` |
| 插件源码 | `translate-master/MuvluvUiTranslate/` |
| 字体结构 | `相关文档/字体配置分析.md` |
| DB 密钥逆向 | `相关文档/PC端蔚蓝档案-UI剧情文本汉化解包逆向计划.md` |
| 部署插件 | `BepInEx/plugins/MuvluvUiTranslate/` |
| 框架配置 | `BepInEx/config/` |
| 工作日志 | `BepInEx/LogOutput.log` |