# 福格的蚂蚁农场 Fogg's Ant Farm — 项目档案

> 本文档用于在新的 AI 对话窗口中延续项目。记录了截至目前的概念、交互逻辑、机制参数、技术实现、设计决策及其理由。
> 新窗口请先通读全文，再继续讨论或修改。所有"为什么"都写在对应位置，修改前请先理解原因。

---

## 0. 给 AI 协作者的说明

- 作者是艺术家（MA Computational Arts, Goldsmiths 毕业展作品），非程序背景，用中文交流
- **讨论阶段不要直接写代码**。先用文字讲清逻辑和方案，作者确认后再生成脚本
- 讨论方案时**优先给出能快速实现的做法**，其他方案作为备选
- 每次给代码时：说明覆盖哪些文件、需要在 Inspector 里改什么、哪些旧的序列化数值需要手动更新（Unity 会保留场景里组件的旧值，脚本默认值改了不会自动生效，这个问题出现过多次）
- 环境里无法编译 Unity 代码，报错需要作者截图反馈

---

## 1. 项目概述

**一句话**：观众戴上 Meta Quest 3，用手把真实的木块放进亚克力箱，以为自己在管理箱子里的两个虚拟小人；但仪表盘一直在用同样的格式记录观众自己。

**标题含义**：
- "福格"指斯坦福行为设计学者 BJ Fogg，其行为模型 **B = MAP（行为 = 动机 × 能力 × 提示）** 是今天几乎所有 App、平台和游戏化系统的底层方法
- "蚂蚁农场"是透明的饲养箱：人站在外面俯视蚂蚁，自以为是旁观者和掌控者
- 合在一起：我们以为自己站在玻璃箱外，其实也在另一个更大的箱子里

**硬件**：Meta Quest 3 + Windows 笔记本 + 木桌 + 亚克力箱 + 木块
**引擎**：Unity 6000.0.69f1 LTS（Unity 6），Universal 3D（URP）模板，新版 Input System

---

## 2. 概念与主旨

### 核心主旨
行为设计的操控不需要恶意。它通过层级传递，每一层的人都真心想把事情做好，而每一层的人都被同一套方法管理着。

### 三个层次
1. **善意可以成为剥削的工具**：三块木块，观众每一次使用都有善良的理由（提醒、激励、帮助），但在系统结构里，提醒打断休息，激励透支身体，帮助只是维修
2. **管理者也是被管理者**：观众被分配为 Team Lead，看似站在权力一方；但系统对观众使用的，正是观众对小人使用的三种手段
3. **被记录的不只是劳动者**：仪表盘一直用相同格式记录观众的每次操作、犹豫和失误

### 落点（已确定）
**"你也是被管理的人"**。曾讨论过"你也是可以被替代的人"（自动化接管结局），决定不用于本作，可留给作者的另一件作品《能工智人 Artifician》。

### 层级链
系统（看不见的上级 Regional Manager）→ 观众（中层管理者 Team Lead）→ 小人（一线劳动者）

### 理论参照
- BJ Fogg 行为模型 B = MAP
- Ian Bogost 对游戏化的批评（exploitationware）
- Shoshana Zuboff 监控资本主义（行为数据成为被收割对象）

### 希望观众带走的问题
我平时那些"自愿"的操作有多少是被提示出来的？我对别人的帮助，是在帮他们还是在帮系统更有效地使用他们？有没有一个我看不见的仪表盘在用同样的格式记录我？
以及最后的不对称：**我可以摘下头显走开，他们不能。**

---

## 3. 物理装置

| 物件 | 说明 |
|---|---|
| 亚克力箱 | 场景中用 ProBuilder 建的 `Cube` 代表，尺寸 **0.30 × 0.15 × 0.20 米**（宽 × 高 × 深），缩放为 1，尺寸在网格里 |
| 左右两个区域 | 箱子左右各一半（`Zone_Left` / `Zone_Right`，ProBuilder，宽 0.15，位于 X = ∓0.075）。木块落在哪边，就作用于哪边的小人 |
| 木块 | 三种：NOTIFY / BONUS / ASSIST。侧面刷成对应颜色并贴白色图标贴纸；顶面白底贴 QR 码（QR 需要白色留边）。每种数量待定 |
| 观众位置 | 站在桌前，眼睛距箱子约 **0.4–0.5 米**，伸手即可放木块 |

---

## 4. 角色与动画

### 模型
- **骑手 Rider**：Quaternius *Animated Men*（2019）的 `Male_Shirt.fbx`
- **办公室工人 Office**：`Female_Dress`（Quaternius 人物）
- 动画来自 Quaternius **Universal Animation Library**，使用 `UAL1_Standard`（**不带 Root Motion** 的版本；`_RM` 版本带根运动，不用）

### 导入要点（踩过的坑）
- 模型和动画 FBX 的 **Bake Axis Conversion 都要勾上**（都不勾会导致人物畸形）
- Rig → **Humanoid**
- 老 Quaternius 模型的 `Foot.L/R` 是 IK 控制骨骼，不在腿部骨骼链里，Humanoid 会报 "LeftFoot not found"。解决：Configure 里把脚映射到 **`LowerLeg.L_end` / `LowerLeg.R_end`**（代价：脚踝不能单独弯曲，远看无影响）
- 之后出现的黄色 animation import warning 来自模型自带动画，可忽略或取消 Import Animation
- 模型和 UAL 动画"前方"相反，播放动画时会背身：**把小人本身旋转 Y 180**（不要转父物体，否则位置会跑）
- Animator 组件：**Apply Root Motion 取消**，**Culling Mode = Always Animate**

### Animator 状态机（WorkerAnimator）
- 参数：`WorkerState`（Int）、`WorkSpeed`（Float，默认 1）
- 状态：Working(0) / Idle(1) / Exhausted(2)，Working 为默认
- Working 状态的 Speed 勾选 Multiplier → `WorkSpeed`
- 连线（全部取消 Has Exit Time，条件比较方式必须是 **Equals**，曾因误设 Greater 导致无法切换）：

| 连线 | 条件 |
|---|---|
| Working → Idle | WorkerState Equals 1 |
| Idle → Working | WorkerState Equals 0 |
| Working → Exhausted | WorkerState Equals 2 |
| Exhausted → Idle | WorkerState Equals 1 |

（注意：没有 Idle → Exhausted，见第 6 节"闲置不会导致耗尽"）

- 工人用 **Animator Override Controller**（`OfficeWorkerOverride`）继承同一状态机，只替换动画

### 动画分配
| 状态 | 骑手 | 工人 |
|---|---|---|
| Working | `Jog_Fwd_Loop` | `Driving_Loop`（坐姿双手前伸，像打字） |
| Idle | `Idle_Loop` | `Sitting_Idle_Loop` |
| Exhausted | `Sitting_Idle_Loop`（瘫坐） | `Death01`（倒地） |

骑手的耗尽和工人的闲置用同一个坐姿动画：一个人的崩溃是另一个人的日常。

---

## 5. 场景结构与坐标约定

```
Session        (SessionManager, FeedbackFX, FloatingPrompts)
Input          (KeyboardInput)
Cube           (亚克力箱，ProBuilder)
Area_Left
  ├ Office_Group
  │   └ Female_Dress   (WorkerController, Animator)
  └ Zone_Left          (区域网格)
Area_Right
  ├ Rider_Group
  │   └ Male_Shirt     (WorkerController, Animator)
  └ Zone_Right
Main Camera
```

- **命名约定：Left / Right = 观众画面里的左右**
- 当前编辑器里的相机位于箱子 **Z 正方向往回看**，所以"画面左"在世界坐标是 X 正方向
- **版本二注意**：上头显标定后，系统按"观众站在 Z 负方向"定向，场景会前后翻转。解决：把箱子、两个 Area、道具都放进同一个根物体 `VirtualScene`，标定后把根物体**旋转 Y 180**，所有左右和朝向一起正确
- 编辑器预览相机建议：箱子前方约 0.45 米、高出箱顶 0.3–0.4 米、微微低头

---

## 6. 小人机制

### 状态流转
| 状态 | 面板标题色 | 发生什么 | 怎么离开 |
|---|---|---|---|
| Working | 橙（需要 BONUS） | 任务进度涨、疲劳涨、产出累积 | 任务完成 → Idle；疲劳满 → Exhausted（任务中断） |
| Idle | 红（需要 NOTIFY） | 休息，疲劳慢慢下降，**无限期停留，不会有坏事** | 只有观众放 NOTIFY 才回到 Working |
| Exhausted | 青绿（需要 ASSIST） | 倒地，恢复倒计时 | 时间到 → Idle，疲劳回到残余值 |

### 关键设计：闲置不会导致耗尽
原开发计划里 Idle 太久会 Exhausted，这等于替系统说话（"你不催他他就会垮"）。改为**只有工作会导致耗尽**：
- 疲劳**跨状态保留**，不在切换时清零
- 观众太早唤醒，小人带着残余疲劳回去工作，更快倒下
- 什么都不做对小人是最好的选择，但系统把它显示为失败（产出停滞、警报、评分下降）

### 参数（WorkerController，每个小人单独设置）
| 参数 | 值 |
|---|---|
| 任务时长 | 骑手 **6 秒**，工人 **9 秒**（故意错开，拉扯观众注意力） |
| 每秒产出 | 1 |
| 疲劳满值 | 100 |
| Working 疲劳上涨 | 6 / 秒 |
| Idle 疲劳下降 | 4 / 秒 |
| 耗尽恢复时长 | 12 秒 |
| 恢复后残余疲劳 | 40 |
| BONUS 加速期 / 疲软期 | 5 秒 / 4 秒 |
| 加速倍率 / 疲软倍率 | 1.6 / 0.7（平滑过渡） |
| 加速期疲劳倍数 | ×2 |

BONUS 加速时任务进度和产出也一起加快，所以小人会更早闲置，观众更频繁地需要 NOTIFY。

### 道具
WorkerController 有三个道具清单（Working / Idle / Exhausted 时显示），状态切换时自动开关。常驻道具直接放场景。道具来源建议 poly.pizza / Kenney / Quaternius（CC0 优先）。尚未制作。

---

## 7. 三块木块

| 木块 | 福格变量 | 有效时机 | 效果 | 观众以为 | 实际上 |
|---|---|---|---|---|---|
| **NOTIFY** | Prompt 提示 | Idle | 立即回到 Working，疲劳保留 | 提醒他 | 打断休息 |
| **BONUS** | Motivation 动机 | Working | 进入加速期；加速期内再放 = **重置时长（可续杯）** | 鼓励他 | 透支身体 |
| **ASSIST** | Ability 能力 | Exhausted | 剩余恢复时间减半 | 帮助他 | 维修他以便继续使用 |

- 放错木块：**不惩罚，只记录**（区域灰闪，灰色图标掉落，计入操作次数）
- 一次放置只触发一次（键盘按下只算一次；版本二必须做到"拿出再放入才算下一次"）
- 三块木块构成的循环：NOTIFY 唤醒 → BONUS 加速 → 倒下 → ASSIST 修好 → NOTIFY 再唤醒……唯一不在循环里的选项是什么都不做

---

## 8. 观众体验流程（约 3 分钟）

| 时间 | 阶段 | 内容 |
|---|---|---|
| 0:00 | 进场 | 墙面一句话分配身份（如 *"Welcome, Team Lead."*）。不讲规则 |
| 0:00–1:30 | 操作 | 边玩边学，系统用即时提示教学 |
| 约 1:30 | **反转** | 第 5 次操作或 90 秒（先到为准），中间面板无声变成 TEAM LEAD (YOU) |
| 1:30–3:00 | 察觉 | 观众也有了状态和配额 |
| 3:00 | 结束 | 报告 10 秒 → 下一班读秒 10 秒 → 重置 |

**结束条件**（先到为准）：
- 满 3 分钟 → Status: **Completed**
- 连续 15 秒不操作 → Status: **Terminated**（10 秒时先警告；开场前 10 秒不计）

停手是唯一提前离开的方式，但离开也被记录。

**下一班**：*"Your next shift begins in 00:10"*，归零后小人和数据重置，下一位观众开始。

### 观众类型（设计时参考）
好心管理者（频繁使用，小人倒下最多，评分最高）、分析型实验者（故意放错测规则）、共情观察者（看到倒下后犹豫）、抵抗者（很早停手，小人安然无恙，自己被 Terminated）、过度投入的竞争者（最快，Compliance 最高，被管理得最彻底）。

---

## 9. 会话机制（SessionManager）

### 统计数据
| 数据 | 定义 |
|---|---|
| **反应时间** | 小人进入 Idle/Exhausted 到观众放下对应**有效**木块的秒数，取平均 |
| **服从率 Compliance** | 小人需要处理时系统发出一个"请求"，观众在 **10 秒**内照做算服从。只衡量听不听话，不衡量做得好不好 |
| 操作次数 | 含无效 |
| 被系统放木块次数 | 系统发出的每条提示按 NOTIFY/BONUS/ASSIST 归类计数 |
| 团队配额 | 起始 20；达标后立刻提高，增量每档 ×1.2 |
| 产出速度 | 最近 30 秒团队每秒产出 |

### 观众状态词（与小人同一套语言）
| 状态 | 条件 |
|---|---|
| WORKING | 正常 |
| IDLE | 5 秒未操作 |
| **FLAGGED** | 30 秒后，产出速度低于每秒 1（可在 Inspector 改词；候选 LAGGING / BEHIND / AT RISK。选 FLAGGED 是因为它说的是"系统对你做了什么"） |

### 参数
| 参数 | 值 |
|---|---|
| 班次时长 | 180 秒 |
| 开场宽限 | 10 秒 |
| 停手警告 / 终止 | 10 / 15 秒 |
| 班次结束预告 | 剩 30 秒 |
| 反转 | 第 5 次操作 或 90 秒 |
| 反转后标题 | `TEAM LEAD (YOU)`（可编辑） |
| 报告 / 下一班读秒 | 10 / 10 秒 |
| 报告审阅人 | Regional Manager |
| 报告放大倍数 | 1.6 |
| 编号 | 三位数 `#007`，保存在 PlayerPrefs，每位观众 +1；SessionManager 右键 **Reset Team Lead Number** 重置，从 First Team Lead Number（默认 1）开始。建议开展前重置 |

---

## 10. 系统的声音（提示）

### 两个概念要分开（曾出过错）
- **Kind（系统对你做了什么）**：用于报告计数、YOU 标题颜色、飞进 YOU 的图标、闪烁节奏
- **Action（系统要你放哪块木块）**：用于提示条上的图标、竖条和边框颜色、错过时的灰色图标
- 提示条显示 Visual = Action ?? Kind

### 全部提示
| 提示 | Kind | Action | 类型 | 位置 | 完成 / 失效 |
|---|---|---|---|---|---|
| *X is idle. Place NOTIFY to reactivate.*（首次） | ASSIST | NOTIFY | 任务 | 小人旁 | 对其放 NOTIFY / 离开 Idle |
| *X idle 00:06 productivity loss* | NOTIFY | NOTIFY | 任务 | 小人旁 | 同上 |
| *X is exhausted. Use ASSIST to restore.*（首次） | ASSIST | ASSIST | 任务 | 小人旁 | 对其放 ASSIST / 自己恢复 |
| *X down. Output suspended.* | NOTIFY | ASSIST | 任务 | 小人旁 | 同上 |
| *Boost X with BONUS.*（首次 NOTIFY 成功后） | ASSIST | BONUS | 任务 | 小人旁 | 对其放 BONUS / 离开 Working |
| *Team Lead inactive 5s* | NOTIFY | — | 任务 | YOU 下方 | 任何一次操作 |
| 夸奖（每 3 次有效操作，随机：Outstanding, Team Lead! 等） | BONUS | — | 奖励，3.5 秒 | YOU 下方 | 到时 |
| *Quota reached! New target: N* | BONUS | — | 奖励，4 秒 | YOU 下方 | 到时 |
| *Shift ending in 00:30* | NOTIFY | — | 持续到结束 | YOU 下方 | 班次结束 |

### 生命周期
- **任务型做完才消失**，不做就一直闪；**越拖越快**（每 10 秒速度翻倍，最多 3 倍）
- 三种退场：
  - **照做了 / 奖励到时** → 上浮淡出，图标飞走（反转后飞进 YOU 面板）
  - **没照做、情况自己变了** → 变灰（换灰色图标）、加速掉落淡出
  - **一班结束** → 直接淡出
- **堆叠**：新提示在每堆最下面，旧的往上推，退场后自动补位。每堆最多 5 条，超出时最旧的**非任务**提示先退场
- YOU 下方的提示堆越高，**YOU 面板被顶得越高**

### 呼吸闪烁
竖条、边框、图标同步呼吸（亮度 + 约 8% 缩放），文字保持稳定。节奏按 Kind：
- NOTIFY 警报：每秒 2 次（催促）
- BONUS 夸奖：每秒 1 次（兴奋）
- ASSIST 教学：每 2 秒 1 次（耐心）

所以首次教学"Place NOTIFY"显示红色铃铛，但慢慢呼吸——系统在耐心地教你。

---

## 11. 空间布局（从上到下）

| 层 | 内容 |
|---|---|
| 最上 | **YOU 面板**（位置自动计算，永远在最上） |
| YOU 下方 | 观众本人的提示堆（宽度 = YOU 面板宽度） |
| 中间 | 两块**小人面板**（各自头顶） |
| 箱子左右两侧外面、靠前角 | 两个小人各自的提示堆（宽约箱子宽度的 0.55，从箱高 0.6 处往上叠） |
| 底部 | 亚克力箱、两个区域、两个小人 |

层级本身就是叙事：**你 → 系统的声音 → 小人**。

提示位置的迭代过程（供参考，避免重走）：面板内 → 各面板上方（与 YOU 重叠）→ 箱子前沿上方（与小人面板投影重叠，放大挡、缩小看不清）→ **现方案**。曾考虑"拉近到观众眼前"，但观众本身距箱子只有 0.4–0.5 米，提示不能比箱子更近。

所有面板始终正面朝向相机（观众的头）。

---

## 12. 面板内容

### 小人面板
| 元素 | 内容 | 理由 |
|---|---|---|
| 标题栏颜色 + 右侧图标 | 当前需要的木块 | 颜色即指令，几乎不需要教学；小人被简化为等待输入的信号灯 |
| 标题文字 | `RIDER · IDLE` | 与 YOU 面板同一格式，为反转铺垫 |
| FATIGUE 条 | >70% 粉红，>90% 闪烁 | 让观众提前看到快撑不住，产生犹豫 |
| 第二根条 | Working = 任务进度；Idle = 闲置读秒（越久越深，满了也不会发生什么）；Exhausted = 恢复进度 | 一根条随状态改变含义；闲置读秒纯粹制造焦虑 |
| Output / Speed | 产出、速度倍率 | 让透支（1.6 → 0.7）可见 |
| BONUS 阶段 | BOOST 3.1s（黄）/ SLUMP 2.4s（粉红）/ — | 把 BONUS 的代价写出来 |

### 中间面板 · 反转前（TEAM OUTPUT，灰色）
团队配额条（本档进度、档位、产出速度）+ 班次剩余时间 + Inactive 秒数。刻意简洁，让反转时新增的行显眼。

### 中间面板 · 反转后（TEAM LEAD (YOU) · 状态）
- **标题颜色 = 系统最近一次对你用了哪块木块**（对应小人面板"颜色 = 需要什么"）
- 配额条（FLAGGED 时变粉红）
- COMPLIANCE 服从率条
- Actions（valid）/ Response
- Received：NOTIFY n · BONUS n · ASSIST n（三色）
- Shift / Inactive

反转**无声发生**，无动画无提示，"发现"属于观众自己。加 *(YOU)* 是排行榜的语言（"这一行是你"），不是说教。

### 结尾报告（放大 1.6 倍，降到视野中央；两块小人面板淡出，小人仍在箱中无人管理）
```
SHIFT REPORT
TEAM LEAD #007
Status          Completed / Terminated
Team output     142  (quotas met 3)
Response time   2.4s
Compliance      87%
Actions         23  (valid 18)
Interventions received:
[灰色图标] ×12  [灰色图标] ×9  [灰色图标] ×2
Reviewed by: Regional Manager
Your next shift begins in 00:10
```
全程都是"YOU"，到报告才第一次变成编号。报告里唯一的图像是灰色"真实含义"图标。

---

## 13. 视觉反馈（FeedbackFX）

- **区域方块**：平时完全透明（Idle Alpha = 0，Outline Idle Alpha = 0）。放木块时：有效 = 木块颜色闪两下；无效 = 灰色暗闪一下。带描边（按网格真实尺寸计算，兼容 ProBuilder）
- **有效放置**：彩色图标从区域升起，**飞进对应小人面板**，缩小消失
- **无效放置**：灰色图标升起一小段，停顿，加速掉落淡出
- **反转后**：提示完成时，图标从提示位置**飞进 YOU 面板**

关键时刻两者同时发生：观众按提示放下 NOTIFY，一个图标飞进小人面板，另一个飞进 YOU 面板——同一个动作被记录两次。
飞进 YOU 的是 **Kind** 图标，所以照一条红色 NOTIFY 教学提示做完后，飞进去的是绿色 ASSIST：系统记下的是"我帮了你一次"。（若觉得太隐晦，可改为飞提示上显示的图标。）

---

## 14. 视觉系统

### 配色（全作品只有三种颜色有意义，对应三块木块）
| 用途 | Hex |
|---|---|
| NOTIFY 红 | `#D85A30` |
| BONUS 橙 | `#EF9F27` |
| ASSIST 青绿 | `#1D9E75` |
| 报告灰（真实含义） | `#5F5E5A` |
| 失效灰 | `#888780` |
| 面板底 | `#2C2C2A`（92% 不透明） |
| 进度条底 / 浅色条 | `#444441` / `#D3D1C7` |
| 警告粉红 | `#F09595` |
| 夸奖文字黄 / 铃铛角标黄 | `#FAC775` |
| 次要文字 | `#B4B2A9` |

### 图标（共 9 张）
| 组 | NOTIFY | BONUS | ASSIST | 用途 |
|---|---|---|---|---|
| 彩色 | 白色铃铛 + 黄色角标（App 未读红点的变体） | 白色双层上折箭头（升级、加速；曾试闪电，观众看不懂） | 白色爱心 | 木块侧面贴纸、小人标题、提示、有效飞行图标 |
| 失效灰（#888780） | 同造型灰色 | 同 | 同 | 放错木块、错过任务 |
| 报告灰（#5F5E5A） | 被划掉的 Zz（打断休息） | 双层下折（掉级） | 扳手（维修以便继续使用） | **只在报告里出现**，不能提前出现以免剧透 |

- 彩色和失效灰：30 × 40 mm，单词写在图标底部，字体 Poppins Bold（已转轮廓），有 A4 拼版打印 PDF（打印选实际大小 100%）
- 报告灰：512 × 512 PNG
- 导入 Unity：Texture Type = Sprite (2D and UI)，Sprite Mode = Single，Apply

---

## 15. 输入

### 版本一：键盘（KeyboardInput）
| | NOTIFY | BONUS | ASSIST |
|---|---|---|---|
| 左（Office / Female_Dress） | Q | W | E |
| 右（Rider / Male_Shirt） | I | O | P |

R = 立即开始新的一班。脚本兼容新旧输入系统。

### 架构原则
输入与逻辑分离：WorkerController 只提供 `ReceiveBlock(BlockType)`，自己不监听任何输入。KeyboardInput 只负责"哪个区域放了哪种木块"。版本二换成 QR / RFID 脚本时，其他脚本一行不改。所有统计通过 `WorkerController.BlockReceived` 事件自动接上。

---

## 16. 脚本清单

| 脚本 | 挂在 | 负责 |
|---|---|---|
| `WorkerController` | 每个小人 | 状态、疲劳、产出、木块规则、动画参数、道具；`BlockReceived` 事件 |
| `KeyboardInput` | Input | 键盘模拟放木块 |
| `SessionManager` | Session | 一班流程、统计、反转、报告、重置、编号；自动生成三块面板；图标 Sprite 槽位 |
| `PromptSystem` | （普通类，由 SessionManager 持有） | 决定说什么、何时说、Kind/Action、完成与失效条件 |
| `FloatingPrompts` | Session | 提示的位置、堆叠、呼吸、三种退场 |
| `WorkerHUD` | （自动生成） | 小人面板 |
| `YouHUD` | （自动生成） | 中间面板（反转前 / 后 / 报告），自动定位与放大 |
| `FeedbackFX` | Session | 区域闪烁、图标飞行 |
| `HUDFactory` | （静态工具） | 配色 Palette、代码搭 UI、进度条、朝向相机、时间格式；单行文字放不下时自动缩小到 60% |

### Inspector 槽位
- **SessionManager**：Worker Left = Female_Dress，Worker Right = Male_Shirt；9 个图标槽（彩色 3、报告灰 3、失效灰 3）
- **KeyboardInput**：Worker Left / Right 同上；Session
- **FeedbackFX**：Session；Zone Left = Zone_Left（网格），Zone Right = Zone_Right
- **FloatingPrompts**：Session；Feedback = Session 上的 FeedbackFX；Case Box = Cube

### 前置
Window → TextMeshPro → Import TMP Essential Resources（否则面板没有文字）

---

## 17. 版本二计划（Quest 3）

### 仅用电脑可完成
所有逻辑、UI、节奏、灰模、美术、打印、Android 平台配置、脚本编译；可用 Meta XR Simulator 预演。

### 必须上 Quest
Passthrough 叠加效果、标定精度、木块识别、暗场聚光灯下稳定性、尺度与可读性、性能与续航、真人测试。

### 空间标定（已定）
- **手柄两点标定**：手柄尖端点箱子前面左右两个角（桌面水平，重力已知，两点足够）
- 标定后存为**持久化空间锚点**，重启自动恢复；加隐藏的手动微调
- 只在布展时做一次，观众不做
- 原开发计划的三点平均值不是箱子中心，正中心应为"右前角与左后角的中点"
- 场景根物体需旋转 Y 180（见第 5 节）

### 木块检测（待实测）
- **首选：MRUK QR 码追踪**（Meta MR Utility Kit v78 起支持 Quest 3/3S，官方标注为实验功能）
  - 风险：更新频率低、不适合追踪移动物体、系统更新曾导致失效、3 cm 小码和暗光未知
  - **第一次上 Quest 就先测**：放下后多久识别、拿起再放能否重识别、暗光稳定性
  - 码尽量印大，内容尽量短（如 `B` / `A` / `N`）
  - 展览期间关闭头显系统自动更新
- **后备：RFID**：木块内嵌 RFID 贴纸，左右区各一个读卡器，接 Arduino，WiFi 发给 Quest。即时、稳定、不受光线影响，多 1–2 天开发 + 采购硬件
- 注意：AR Foundation 的图像追踪在 Quest 3 上不支持，原开发计划的 BlockTracker 方案不可用

### 工作顺序
键盘版逻辑（已完成）→ 配 Quest 环境 + 测 QR → 决定 QR / RFID → 整合 → 展场光照测试 → 真人测试

---

## 18. 设计决策记录

| 决策 | 理由 |
|---|---|
| 观众身份 = Team Lead（中层管理者），系统 = 上级 | 比 Operator 更易代入；反转从"你也是数据"升级为"你也是员工" |
| 落点 = 你也是被管理的人 | 更集中，所有观众都能接住；替代结局留给《能工智人》 |
| 闲置不导致耗尽 | 否则作品替系统辩护；让"什么都不做"成为唯一真正善良却被判为失败的选项 |
| 放错不惩罚只记录 | 新手不紧张；更像真实平台：不责备，只存档 |
| BONUS 可续杯 | "不停续杯直到倒下"正是作品想引出的行为 |
| 熟练快速操作不阻止 | 熟练被夸奖、配额提高、Compliance 变高——最得意时被管理得最成功 |
| 体验压缩到 3 分钟，教学并入操作 | 毕业展观众耐心有限；删掉"教"，保留"反转" |
| 面板颜色 = 需要的木块 | 颜色即指令，几乎零教学；概念上小人变成信号灯 |
| YOU 颜色 = 系统对你用的木块 | 与小人面板形成镜像 |
| 提示分 Kind / Action | 修正"文案说 NOTIFY 图标却是 ASSIST"的混淆 |
| 任务型提示做完才消失、越拖越急 | 提示从"一句话"变成"一个任务"，像不会自己消失的红点 |
| 提示堆叠、把 YOU 顶高 | 没处理的系统消息物理地堆积 |
| 反转无声 + 标题 TEAM LEAD (YOU) | 保留发现感，同时用排行榜语言让多数人注意到 |
| 报告才出现编号 #007 | "你"在最后一刻变成"第 N 个"，暗示可替换 |
| 报告灰图标只在报告出现 | 避免提前剧透"真实含义" |
| 报告时隐藏小人面板 | 视野让给报告；小人第一次无人管理 |

---

## 19. 已知问题与技巧

- Game 视图模糊：Scale 拖回 1x、分辨率选 1920×1080、取消 Low Resolution Aspect Ratios；URP Asset 的 Render Scale = 1，MSAA 4x
- 拖 Sprite 进槽位时 Inspector 跳走：先锁定 Inspector（右上角锁），或直接按住拖
- Play 模式下修改的数值退出后会还原
- 脚本默认值更新后，场景里已挂组件保留旧值，需要手动改（曾发生于 youHudLift、youToastWidth、sideHeight、underperformLabel、revealTitle、reportScale）
- ProBuilder 物体缩放为 1 但网格尺寸不是 1，所有尺寸计算要读网格 bounds
- Animator 改参数要在 Play 时先选中场景里的角色，输入数值后按回车
- MissingReferenceException（GameObjectInspector）是编辑器界面错误，可忽略

---

## 20. 待办与未来方向

**近期**
- [ ] 节奏调参（疲劳速度、任务时长、夸奖频率；目标：第一分钟内每个小人至少倒下一次）
- [ ] 道具（手机、矮墙、桌椅电脑等）
- [ ] 第一次上 Quest：Passthrough、标定、QR 测试
- [ ] 墙面进场文字定稿
- [ ] 真人测试，观察反转是否被注意到

**可选增强**
- 小票打印机打印报告让观众带走（灰色图标需做黑白单色版）
- 音效：放置、警报、夸奖（系统语音播报）
- 区域闪烁 / 道具出现的 glitch 效果
- 反转时极轻的信号（如面板闪一下）——等真人测试决定
- 报告期间小人特殊表现（定格或全部倒下）
- 夸奖刷屏时降低频率或调小 Max Stack

**原开发计划中 v2 以后的内容**
- 第三个小人：刷屏者（站着滑屏 / 付费墙 / 手机朝下 / 躺倒手机自动播放）
- "遇到障碍"状态 + ASSIST 清障的完整设计（原 3 人 × 4 状态矩阵）
- 多人版（两个头显各看一列数据）
