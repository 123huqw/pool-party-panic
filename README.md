# 泳道老六 / Pool Party Panic

一个 3D 第三人称「节奏划水 + 互坑」游泳竞速原型。8 条泳道，双键交替划水，按错节奏会呛水，落后的人可以拽前面人的脚把他甩进水里。

> 原型阶段：所有场景内容都是**运行时用代码生成**的（`Assets/Scenes/SwimRace.unity` 是一个空场景），
> 所以工程里几乎没有美术资源，改玩法不用动场景。

---

## 玩法

### 核心：双键节奏划水

左右手交替按键前进。系统记录你**两次按键之间的间隔**，和理想间隔比对给出评级：

| 评级 | 条件 | 效果 |
|---|---|---|
| PERFECT | 落在理想间隔 ±0.06s | 速度 ×1.00，流畅度 +16 |
| GOOD | ±0.13s | 速度 ×0.88，流畅度 +8 |
| OK | ±0.24s | 速度 ×0.78 |
| 太慢 | 慢于 OK 窗口 | 速度 ×0.68 |
| 太急 | 快于 0.16s | 速度 ×0.60 |
| 乱划 | 疯狂连按 | 速度 ×0.42 |
| 同手连划 | 连按同一侧 | 速度 ×0.35 |

连续打好节奏会积累**流畅度**，满值时额外 +22% 速度。

### 换气

换气是独立计时条，不消耗体力。按 Space 在**绿色区间**换气可以拿到加速；抢太早或憋太久会依次进入「脸变直 → 脸红 → 沉底淹死」。

### 互坑（仅「互坑房」模式）

- **F 键拽脚 / 蹬人**：够到附近的人就能把他往后甩，自己获得前冲
- **组合技**：两次 F 的间隔落在「隐藏窗口」内（每次随机、约 10% 成功率）触发大招，把对手甩到身后 3.2 米并让他翻滚 1.7 秒
- **拉扯**：换泳道（Q/E）绕到别人旁边，会消耗体力
- **被甩飞后**：进入布娃娃状态，要按对 3 个随机提示键才能翻回来，按错呛水
- **复仇**：翻回来后进入 5 秒复仇状态，抓人距离 ×1.5、冷却减半
- **橡皮筋**：领先者会被减速，且更容易被后面的人抓到

### 两种房间

| 模式 | 说明 |
|---|---|
| 竞速房 | 严厉窒息惩罚，不能互坑 |
| 互坑房 | 换气只加分不淹死，可以拽脚蹬人 + 换泳道（默认） |

---

## 操作

| 操作 | 按键 |
|---|---|
| 左手划水 | `A` / `←` / `J` |
| 右手划水 | `D` / `→` / `K` |
| 换气 | `Space` / `W` / `↑` |
| 加速（耗体力） | `Shift` / `L` |
| 拽脚 / 蹬人 | `F` |
| 换泳道（仅互坑房） | `Q` 左 / `E` 右 |
| 被甩飞后挣扎 | 按屏幕提示的 3 个随机键 |
| 重新开始 | `R` |
| 暂停 | `Esc` |
| 回标题 | `Q`（结果页 / 暂停页） |
| 调参面板 | `F1` |
| 节拍器 | `M` |
| 标题页：难度 | `1` / `2` / `3` |
| 标题页：切模式 | `←` `→` 或 `Tab` |
| 标题页：开始 | `Enter` / `Space` |

---

## 运行

1. 装 **Unity 6000.6.4f1**（其他 Unity 6 版本应该也行，没验证过）
2. 用 Unity Hub 打开本仓库根目录
3. 打开 `Assets/Scenes/SwimRace.unity`
4. 点 Play

## 打包（Windows）

菜单 **File → Build Profiles → Windows → Build**，输出建议放纯 ASCII 路径。

也可以用编辑器里的一键工具：

- 菜单 **游泳原型 → 1. 初始化工程（材质 + 场景）**：重新生成 `Assets/Resources` 下的材质模板、空场景和 Build Settings
- 菜单 **游泳原型 → 2. 打包 Windows 版**：打包到 `Build/SwimRace.exe`
- 命令行：`Unity.exe -batchmode -projectPath . -executeMethod SwimSetup.SetupAndBuild -quit`

---

## ⚠️ 已知坑：绝对不要把内置着色器塞进「Always Included Shaders」

这是踩过的血坑，**打包必崩**，务必别再犯：

如果把 `GUI/Text Shader` 加到 **Project Settings → Graphics → Always Included Shaders**，
打包时会在写内置资源这一步直接失败（报错看起来完全和着色器无关，极具误导性）：

```
An asset is marked with HideFlags.DontSave but is included in the build:
Asset: 'Library/unity default resources'
Asset name: GUI/Text Shader
Assertion failed on expression: 'm_LockCount == 0'
Building - Failed to write file: Library/PlayerDataCache/Win64/Data/Resources/unity_builtin_extra
Error building Player: 3 errors
```

原因：`GUI/Text Shader` 属于 Unity 内置的 `Library/unity default resources`，带 `HideFlags.DontSave`。
强制包含它之后，打包器试图把它序列化进 `Resources/unity_builtin_extra`，写锁无法释放，
于是 `m_LockCount == 0` 断言失败，整个打包中断。

排查这个坑时走过的弯路（都无效）：清 `Library` 缓存、腾内存、换 Mono 后端、删材质/脚本/场景。
最后用「最小工程 + 只改一行设置」的 A/B 实验才定位到——**病根只在设置的那一行**。

> `Assets/Editor/SwimSetup.cs` 曾经的初始化代码里有 `AddAlwaysIncluded("GUI/Text Shader")`，
> 现在已删除并留了注释。**如果你要改这个文件，别再把它加回来。**

---

## 调参

所有手感参数集中在 `Assets/Scripts/Tuning.cs` 的 `Tuning` 静态类里，用 `[Tune(分组, 名称, 最小, 最大)]` 标注。

游戏内按 **F1** 打开实时调参面板（带滚动条和滑块），调好后：

- 点「复制参数」把所有值导出成文本，粘回 `Tuning.cs` 固化
- 点「恢复默认」还原

主要分组：划水 / 流畅度 / 水阻 / 换气 / 体力 / 互坑 / 组合技 / 甩飞 / 挣扎 / 复仇 / 橡皮筋 / NPC / 软体 / 追赶 / 换道。

---

## 代码结构

```
Assets/
├── Scenes/SwimRace.unity          空场景（内容全部运行时生成）
├── Resources/                     材质模板（Standard / 粒子 / 半透明水）
├── Editor/SwimSetup.cs            一键初始化工程 + 打包工具
└── Scripts/
    ├── Tuning.cs                  全部手感参数 + F1 面板的数据源
    ├── GameManager.cs             主循环、状态机（标题/入场/倒计时/比赛/结果）、输入、犯规判定
    ├── Swimmer.cs                 单个泳者的运动学：划水节奏判定、换气、体力、位置
    ├── SwimmerBody.cs             身体外观与动画：软体四肢、布娃娃甩飞、挣扎恢复、水花
    ├── NpcBrain.cs                NPC 的节奏模拟、卡壳、出手决策
    ├── PoolBuilder.cs             程序化生成泳池、泳道、水面、看台
    ├── Art.cs                     程序化美术：材质、贴图、字体、水面法线
    ├── Fx.cs                      特效与音效（水花、气泡、哨声、BGM 氛围）
    ├── ChaseCam.cs                第三人称跟随相机、抖屏
    ├── Foul.cs                    互坑规则：拽脚/蹬人/组合技的判定
    └── Hud.cs                     IMGUI 绘制的全部 UI（标题、计时、排名、调参面板）
```

---

## 待办

- [ ] 联机（计划用 Photon Fusion 或 Netcode for GameObjects）
- [ ] 道具系统：鲨鱼鳍背心、洗发水泡沫
- [ ] 地图陷阱：近视救生员探照灯、失控造浪机
- [ ] 真·美术资源替换程序化占位
