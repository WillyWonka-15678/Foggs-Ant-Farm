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
- 头显用 USB 连着电脑时，可以用 Unity 自带的 adb 直接读头显日志（`adb logcat`，Unity 的输出标签为 `Unity`），也能让头显截图（见第 19 节），不必等截图
- 档案最近更新：2026-10-06（加入 Quest 版进展：标定、手柄、虚拟木块、提示位置调整）

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

主场景 `SampleScene`（Build Settings 里唯一启用的场景）：

```
[BuildingBlock] Camera Rig    (OVRCameraRig + OVRManager；取代原来的 Main Camera)
[BuildingBlock] Passthrough   (OVRPassthroughLayer)
Session        (SessionManager, FeedbackFX, FloatingPrompts, DebugOverlay, BoxCalibrator)
Input          (KeyboardInput, ControllerInput)
VirtualScene   (整个虚拟场景的根物体，标定时整体移动 / 旋转)
  ├ CaseReference
  │   └ Cube           (亚克力箱，ProBuilder)
  ├ Area_Left
  │   ├ Office_Group
  │   │   └ Female_Dress   (WorkerController, Animator)
  │   └ Zone_Left          (区域网格)
  ├ Area_Right
  │   ├ Rider_Group
  │   │   └ Male_Shirt     (WorkerController, Animator)
  │   └ Zone_Right
  ├ Dashboard
  └ Blocks               (虚拟木块，每种两块，都挂 VirtualBlock + Interaction SDK 抓取组件)
      ├ Block_Notify / Block_Notify (1)
      ├ Block_Bonus  / Block_Bonus (1)
      └ Block_Assist / Block_Assist (1)
```

另有测试场景 `MR_Test`（Build Settings 里未启用），只用来测 QR 码识别（`QRTestProbe`），不接游戏。

- **命名约定：Left / Right = 观众画面里的左右**
- VirtualScene 在编辑器里的初始值为位置 (0, -0.5, 0.45)、**旋转 Y 180**（即原计划的"根物体翻转"已做）
- 追踪原点为 **Eye Level**：应用启动那一刻头显的位置 / 朝向就是原点。Build And Run 时头显往往放在桌上，所以未标定前场景位置是随机的，必须靠标定（第 17 节）对齐真实箱子
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
| 开始前 | **等待** | 两个小人一直休息；只显示红色欢迎卡片（见第 12 节），其他面板和提示全部隐藏。不计时、不记录、不发提示、不判定停手 |
| 0:00 | 开始 | 观众放入任意一块木块（任意一边、任意类型）开始这一班。这一块**不作用于小人、不计入统计**；放入的那一边闪一下白光 + 开始音效；欢迎卡片淡出，面板淡入。小人**仍在休息**，马上出现"Place NOTIFY to reactivate"的教学提示——观众的第一个动作就是把休息中的人叫起来 |
| 0:00–1:30 | 操作 | 边玩边学，系统用即时提示教学 |
| 约 1:30 | **反转** | 第 5 次操作或 90 秒（先到为准），中间面板无声变成 TEAM LEAD (YOU) |
| 1:30–3:00 | 察觉 | 观众也有了状态和配额 |
| 3:00 | 结束 | 报告 10 秒 → 下一班读秒 10 秒 → 重置 |

**结束条件**（先到为准）：
- 满 3 分钟 → Status: **Completed**
- 连续 15 秒不操作 → Status: **Terminated**（10 秒时先警告；开场前 10 秒不计）

停手是唯一提前离开的方式，但离开也被记录。

**下一班**：*"Your next shift begins in 00:10"*，归零后回到**等待阶段**（小人重置为休息，显示欢迎卡片），等下一位观众放入木块。

**进场文字**：原计划写在墙上的 *"Welcome, Team Lead."* 现在由头显里的欢迎卡片说出。墙面是否还需要文字待定。

**工作人员快捷键**：键盘 R / 手柄右摇杆 = 立即开始新的一班，跳过等待（小人从 Working 开始，与旧版一致）。

**开局的两条提示**：从等待开始时两个小人都在休息，所以开局同时出现两条提示——先被检查到的那个（左边的工人）收到教学提示 *"is idle. Place NOTIFY to reactivate."*，另一个收到 *"idle 00:00 productivity loss"*（教学提示只出一次）。是否要调整待真人测试决定。

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
| 编号 | 三位数 `#007`，保存在 PlayerPrefs，**在一班开始时**（放入第一块木块或按 R）才 +1，只启动应用不会用掉编号；SessionManager 右键 **Reset Team Lead Number** 重置，从 First Team Lead Number（默认 1）开始。建议开展前重置 |

### 流程阶段（ShiftPhase）
**Waiting（等待）→ Running（进行中）→ Report（报告 10 秒）→ NextShift（下一班读秒 10 秒）→ Waiting**

- 应用启动时进入 Waiting
- 所有输入脚本放木块前先调用 `SessionManager.ConsumeStartPlacement(小人)`：Waiting 时返回 true，这一块用来开始这一班，不交给小人；其他阶段返回 false，照常放置
- 开始时触发 `ShiftStartPlaced` 事件，FeedbackFX（区域白光）和 SoundFX（开始音效）订阅它

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
| *Team Lead inactive 5s* | NOTIFY | — | 任务 | 箱子正前方 | 任何一次操作 |
| 夸奖（每 3 次有效操作，随机：Outstanding, Team Lead! 等） | BONUS | — | 奖励，3.5 秒 | 箱子正前方 | 到时 |
| *Quota reached! New target: N* | BONUS | — | 奖励，4 秒 | 箱子正前方 | 到时 |
| *Shift ending in 00:30* | NOTIFY | — | 持续到结束 | 箱子正前方 | 班次结束 |

（"箱子正前方"= 观众本人的提示堆，位置见第 11 节）

### 生命周期
- **任务型做完才消失**，不做就一直闪；**越拖越快**（每 10 秒速度翻倍，最多 3 倍）
- 三种退场：
  - **照做了 / 奖励到时** → 上浮淡出，图标飞走（反转后飞进 YOU 面板）
  - **没照做、情况自己变了** → 变灰（换灰色图标）、加速掉落淡出
  - **一班结束** → 直接淡出
- **堆叠**：退场后自动补位。每堆最多 5 条，超出时最旧的**非任务**提示先退场
  - 小人的两堆：新提示在最下面，旧的往上推
  - 观众本人的一堆：**往下叠**，新提示在最上面（贴着箱顶高度），旧的往下推，可以低于桌面。理由：观众从上往下看箱子，往上叠会挡住箱子里的小人
- （旧设计"YOU 下方提示越多，YOU 面板被顶得越高"已取消：提示堆移到箱子前方后，YOU 面板位置固定）

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
| 最上 | **YOU 面板**（等待阶段在同一位置、同一大小显示欢迎卡片）：固定在小人面板正上方（底边比小人面板顶边高 `youHudLift` = 0.05 个小人身高），并往**远离观众**的方向推箱子深度的 `youHudBack` = 0.5，不受提示影响 |
| 中间 | 两块**小人面板**（各自头顶） |
| 箱子左右两侧外面、靠前角 | 两个小人各自的提示堆（宽约箱子宽度的 0.55，从箱高 0.6 处往上叠） |
| 箱子正面上沿中点、再往观众方向推 5 cm | **观众本人的提示堆**（宽 25 cm，最新一条的上沿与箱顶齐平，往下叠，可以低于桌面）。即两个标定点的正中间 |
| 底部 | 亚克力箱、两个区域、两个小人 |

旧布局的说法是"层级本身就是叙事：你 → 系统的声音 → 小人"（从上到下）。观众提示移到箱子正前方后，这个上下顺序已经不成立，新布局的叙事含义**待作者确认**。

提示位置的迭代过程（供参考，避免重走）：面板内 → 各面板上方（与 YOU 重叠）→ 箱子前沿上方（与小人面板投影重叠，放大挡、缩小看不清）→ 小人提示放箱子两侧、观众提示在 YOU 下方 → 观众提示移到 YOU 上方 → **现方案**（2026-10-06：观众提示移到箱子正面上沿、往下叠；之前"前沿上方"被否决是因为往上叠会挡住小人，往下叠避开了这个问题）。曾考虑"拉近到观众眼前"，但观众本身距箱子只有 0.4–0.5 米，提示不能比箱子更近。

所有面板始终正面朝向相机（观众的头）。

---

## 12. 面板内容

### 欢迎卡片（等待阶段）
位置和大小与进行中的 YOU 面板相同，和 YOU 面板交叉淡入淡出（约 0.3 秒）。其他面板和提示全部隐藏。
```
Welcome, Team Lead.
Your team is ready.
Place any block into the box to begin your shift.        （加粗）
By beginning your shift, you agree that your performance may be recorded.   （很小的字）
```
- **整张卡片铺满 NOTIFY 红 #D85A30**（95% 不透明），标题栏为深一点的红，文字白色
- 理由：欢迎卡片本身就是系统发出的第一个"提示"（福格模型的 Prompt），观众还没开始就已经被提示了一次
- 最下面的小字原定为灰色，但红底上灰色看不清，改为 70% 透明度的白色，仍比正文弱
- 小字是"同意被记录"的条款：放下第一块木块的同时，观众就"同意"了

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
- **开始一班**：等待阶段放入木块时，放入的那一边闪一下白光（不飞图标）

关键时刻两者同时发生：观众按提示放下 NOTIFY，一个图标飞进小人面板，另一个飞进 YOU 面板——同一个动作被记录两次。
飞进 YOU 的是 **Kind** 图标，所以照一条红色 NOTIFY 教学提示做完后，飞进去的是绿色 ASSIST：系统记下的是"我帮了你一次"。（若觉得太隐晦，可改为飞提示上显示的图标。）

---

## 13b. 音效（SoundFX）

### 两个声音世界（已确定）
| | 系统的声音 | 小人的声音 |
|---|---|---|
| 内容 | 提示、夸奖、配额、木块放置确认 | 工作、倒下、休息 |
| 风格 | App 通知那种精致、悦耳、"好用"的 UI 音效 | 小、真实、底噪一样的身体声音 |
| 音量 | 清楚、靠前 | 很轻 |

对比本身就在说话：系统的声音好听、响亮、让人想要；劳动和倒下的声音微弱，容易被盖过去。
- 三块木块各有固定音色（NOTIFY 短促提醒 / BONUS 上扬兴奋 / ASSIST 柔和），和颜色一样是"语言"
- 反转后系统对观众用的是同一套声音（镜像）；照提示放木块时依次听到：放置音 → 完成音 → 图标飞进 YOU 的"记录音"（同一个动作被记录两次）
- **反转本身无声**
- 先只做音效，**系统语音播报之后再加**
- 展览用**头显自带扬声器**

### 第一批（已实现；已放入 Kenney 音效，位于 `Assets/Sound/`，SoundFX 已挂在 Session 上）
| 事件 | 槽位 |
|---|---|
| 抓起虚拟木块 | Grab |
| 有效放置 | Place Notify / Bonus / Assist（声音在小人位置） |
| 放错 | Place Invalid（闷、不刺耳） |
| 松手残影消失 / 原位长出 | Vanish / Respawn（可留空） |
| 开始一班 | Shift Start（可留空） |
| 提示出现 | Prompt Notify / Bonus / Assist（按 Kind，声音在提示条位置） |
| 照做了 / 奖励到时 | Prompt Done |
| 任务错过（变灰掉落） | Prompt Missed |
| 图标飞进 YOU | Recorded To You |

**没处理的任务重复提醒**：在闪烁达到最亮的那一刻响，跟呼吸闪烁同步。刚出现时约每 3 秒一次（Remind Interval），拖得越久越频繁（与闪烁同一个加速倍数，最多 3 倍 → 约每 1 秒）。**同一时间只有拖得最久的那条出声**。默认不设次数上限（Max Reminders = 0）。Remind Interval = 0 则每次闪烁都响（不建议：NOTIFY 加速后每秒 6 声）。

**技术**：SoundFX 挂在 Session 上，只订阅事件（WorkerController.BlockReceived、FloatingPrompts 的 ToastShown / ToastResolved / ToastVoided / TaskPulse、FeedbackFX.ArrivedAtYou、VirtualBlock 的静态事件 Grabbed / Dropped / Respawned、SessionManager.ShiftStartPlaced），不改逻辑。Unity 自带 3D 声音（没装 Meta 音频 SDK），12 个声道池，同一声音 0.08 秒冷却，空槽位自动跳过。

**音效素材建议**：Kenney（kenney.nl，CC0）的 Interface Sounds / UI Audio（系统声音）、Impact Sounds（放错）；freesound CC0 补脚步、键盘。导入时勾 **Force To Mono**，Load Type = Decompress On Load。Project Settings → Audio → DSP Buffer Size 已设为 **Best latency**（256）。

### 第二批（待做）
小人的循环声（骑手脚步、工人键盘，很轻）、倒下的闷响、一班流程（剩 30 秒提醒、最后 10 秒滴答、报告出现时打印 / 盖章声）。

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

R = 立即开始新的一班（跳过等待）。等待阶段按任意放置键 = 开始这一班。脚本兼容新旧输入系统。

### 头显：手柄（ControllerInput，与 KeyboardInput 并存）
| | NOTIFY | BONUS | ASSIST |
|---|---|---|---|
| 左手柄 → 左边小人 | X | Y | 扳机 |
| 右手柄 → 右边小人 | A | B | 扳机 |

按下右摇杆 = 立即开始新的一班（跳过等待）。等待阶段按任意放置键 = 开始这一班（轻震一下）。有震动反馈（有效 0.6 / 无效 0.2）。标定期间自动停用，避免误放。

### 头显：用手抓虚拟木块（VirtualBlock）
- `VirtualScene/Blocks` 下有虚拟木块（每种两块），用 Building Blocks 加了 Interaction SDK 的抓取组件（Grabbable / GrabInteractable / HandGrabInteractable），手和手柄都能抓
- 木块不受重力（Rigidbody 设为 kinematic），因为现实里的桌子没有碰撞体，掉下去会一直往下落
- **松手判定**：木块中心在 Zone_Left / Zone_Right 的水平范围内，高度在区域底部到"区域顶部 + 12 cm"之间，就对那边的小人放一次
- **松手后**（2026-10-06 改）：手里那块缩小消失，原位"长出"一块新的。实际做法是同一块瞬移回原位，从很小放大到正常大小（0.25 秒）；松手处留一个只有外观的残影缩小消失（0.2 秒，图标同时淡出；木块本体是不透明材质，只缩小不淡出）。不真的删除再生成，是因为复制 Interaction SDK 的抓取组件容易出现"新木块抓不起来"的问题。新木块还在放大时就能抓
- 图标自动贴在四个侧面和顶面（读 SessionManager 里的彩色图标）。顶面图标的朝向按两个区域的连线（左区 → 右区 = 观众的右手方向）判断，并吸附到木块的边上（2026-10-06 修：原来用启动时的相机方向，在头显里会斜）

### 架构原则
输入与逻辑分离：WorkerController 只提供 `ReceiveBlock(BlockType)`，自己不监听任何输入。KeyboardInput / ControllerInput / VirtualBlock 都只负责"哪个区域放了哪种木块"。换成 QR / RFID 脚本时，其他脚本一行不改。新的输入脚本放木块前要先调用 `session.ConsumeStartPlacement(小人)`，返回 true 就不要再交给小人（等待阶段用来开始一班）。所有统计通过 `WorkerController.BlockReceived` 事件自动接上。

---

## 16. 脚本清单

| 脚本 | 挂在 | 负责 |
|---|---|---|
| `WorkerController` | 每个小人 | 状态、疲劳、产出、木块规则、动画参数、道具；`BlockReceived` 事件 |
| `KeyboardInput` | Input | 键盘模拟放木块 |
| `ControllerInput` | Input | 头显手柄模拟放木块（第 15 节） |
| `VirtualBlock` | 每块虚拟木块 | 用手抓取的木块：松手判定、消失与重生、贴图标（第 15 节） |
| `BoxCalibrator` | Session | 手柄两点标定，把 VirtualScene 对齐到真实箱子（第 17 节） |
| `SoundFX` | Session | 全部音效（第 13b 节） |
| `DebugOverlay` | Session | 头显里的调试面板：帧率、班次状态、最近日志。按左摇杆（编辑器里按 F1）开关，默认隐藏 |
| `QRTestProbe` | 只在 MR_Test 场景 | QR 码识别测试：显示内容、追踪状态、抖动、更新频率，按 A / B 计识别用时和消失用时 |
| `SessionManager` | Session | 一班流程、统计、反转、报告、重置、编号；自动生成三块面板；图标 Sprite 槽位 |
| `PromptSystem` | （普通类，由 SessionManager 持有） | 决定说什么、何时说、Kind/Action、完成与失效条件 |
| `FloatingPrompts` | Session | 提示的位置、堆叠、呼吸、三种退场 |
| `WorkerHUD` | （自动生成） | 小人面板 |
| `YouHUD` | （自动生成） | 中间面板（反转前 / 后 / 报告），自动定位与放大 |
| `FeedbackFX` | Session | 区域闪烁、图标飞行 |
| `HUDFactory` | （静态工具） | 配色 Palette、代码搭 UI、进度条、朝向相机、时间格式；单行文字放不下时自动缩小到 60% |

### Inspector 槽位
- **SessionManager**：Worker Left = Female_Dress，Worker Right = Male_Shirt；9 个图标槽（彩色 3、报告灰 3、失效灰 3）；Case Box = Cube（用来判断 YOU 面板往哪边推）
- **KeyboardInput** / **ControllerInput**：Worker Left / Right 同上；Session
- **FeedbackFX**：Session；Zone Left = Zone_Left（网格），Zone Right = Zone_Right
- **FloatingPrompts**：Session；Feedback = Session 上的 FeedbackFX；Case Box = Cube
- **BoxCalibrator**：Virtual Scene = VirtualScene；Case Box = Cube；Zone Left / Right；停用列表不填会自动找 ControllerInput
- **VirtualBlock**：Type（NOTIFY / BONUS / ASSIST）；Session 和两个区域不填会自动从场景里找

### 主要可调参数（新增）
| 组件 | 参数 | 默认 | 说明 |
|---|---|---|---|
| FloatingPrompts | You Case Toast Width | 0.25 | 观众提示宽度（米） |
| FloatingPrompts | You Case Forward | 0.05 | 从箱子正面往观众方向推出（米） |
| FloatingPrompts | You Case Top Offset | 0 | 最新一条的上沿比箱顶高多少（米） |
| SessionManager | You Hud Lift | 0.05 | YOU 面板底边比小人面板顶边高多少（小人身高为 1） |
| SessionManager | You Hud Back | 0.5 | YOU 面板往远离观众方向推，占箱子深度的比例 |
| VirtualBlock | Vanish Time / Spawn Time | 0.2 / 0.25 | 残影消失 / 新木块出现的时长（秒） |
| VirtualBlock | Above Zone | 0.12 | 区域上方多高以内松手也算放进去（米） |

### 前置
Window → TextMeshPro → Import TMP Essential Resources（否则面板没有文字）

---

## 17. 版本二计划（Quest 3）

### 仅用电脑可完成
所有逻辑、UI、节奏、灰模、美术、打印、Android 平台配置、脚本编译；可用 Meta XR Simulator 预演。

### 必须上 Quest
Passthrough 叠加效果、标定精度、木块识别、暗场聚光灯下稳定性、尺度与可读性、性能与续航、真人测试。

### Quest 环境配置（已完成）
- 包：Meta MR Utility Kit 207、Meta Interaction SDK（OVR）207、Unity OpenXR 1.16.1
- XR 插件：**OpenXR**（只配了 Android）。启用的 Android 功能：Meta XR Feature、Meta Quest Feature、Oculus Touch Controller Profile、Hand Tracking、Meta Hand Tracking Aim、Foveation、Subsampled Layout
- **Meta XR Space Warp 必须关闭**（曾因开着导致手 / 手柄出现阶梯状撕裂和残影，因为项目没有提交深度和运动矢量；画面很简单，不需要 Space Warp）。建议 Depth Submission Mode = Depth 16 Bit
- Player：Min API 32、Target API 34、Single Pass Instanced、Graphics Jobs 开
- 场景用 Building Blocks：Camera Rig、Passthrough、Hand Grab
- 打包后的包名仍是模板默认的 `com.UnityTechnologies.com.unity.template.urpblank`（可在 Player Settings 改）

### 空间标定（BoxCalibrator，已实现，2026-10-06 头显实测可用）
- **手柄两点标定**：右手柄前端的黄色小球碰真实箱子**正面（靠近观众那一面）的左上角**，按右扳机；再碰**右上角**，按右扳机（桌面水平，重力已知，两点足够）
- 然后微调：左摇杆前后左右平移，右摇杆上下移动 / 左右旋转；**A = 完成，B = 重新点**。每一步都会震动一下
- 面板上显示**宽度误差**（点出来的宽度与虚拟箱子宽度之差），±1.5 cm 内为绿色。实测一次为 3.5 cm，若一直偏大，调 `Tip Offset`（小球相对手柄的位置）
- 进入方式：头显里启动时**自动进入**；之后同时按住两个握把键 2 秒可重新进入。编辑器里不会自动进入
- 正面自动判断：根据 Zone_Left / Zone_Right 的位置，保证标定后左区永远在观众左手边
- 标定期间停用 ControllerInput，避免扳机和 A 键误放木块
- 黄色小球在标定全程都跟着右手柄（这是正常的，不代表卡住）
- **尚未做**：标定结果存为持久化空间锚点、重启自动恢复。现在每次启动都要重新标定
- 只在布展时做，观众不做
- 原开发计划的三点平均值不是箱子中心，正中心应为"右前角与左后角的中点"

### 木块检测（待实测）
- **首选：MRUK QR 码追踪**（Meta MR Utility Kit v78 起支持 Quest 3/3S，官方标注为实验功能）
  - 风险：更新频率低、不适合追踪移动物体、系统更新曾导致失效、3 cm 小码和暗光未知
  - **第一次上 Quest 就先测**：放下后多久识别、拿起再放能否重识别、暗光稳定性
  - 码尽量印大，内容尽量短（如 `B` / `A` / `N`）
  - 展览期间关闭头显系统自动更新
- **后备：RFID**：木块内嵌 RFID 贴纸，左右区各一个读卡器，接 Arduino，WiFi 发给 Quest。即时、稳定、不受光线影响，多 1–2 天开发 + 采购硬件
- 注意：AR Foundation 的图像追踪在 Quest 3 上不支持，原开发计划的 BlockTracker 方案不可用
- 现状：测试工具 `QRTestProbe`（MR_Test 场景）已写好；**测试结果尚未记录到本档案，请作者补充**。同时已做了**用手抓虚拟木块**的输入方式（第 15 节），可在真实木块方案确定前先用来测试整套流程

### 工作顺序
键盘版逻辑（已完成）→ 配 Quest 环境（已完成）+ 测 QR（工具已写，结果待记录）→ 决定 QR / RFID → 整合 → 展场光照测试 → 真人测试

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
| 提示堆叠 | 没处理的系统消息物理地堆积（原来还会"把 YOU 顶高"，提示移到箱子前方后取消） |
| 观众提示放箱子正前方、往下叠 | 离观众最近、在两个标定点中间；往下叠不挡箱子里的小人 |
| 虚拟木块松手后消失、原位重生 | （理由待作者补充） |
| 反转无声 + 标题 TEAM LEAD (YOU) | 保留发现感，同时用排行榜语言让多数人注意到 |
| 报告才出现编号 #007 | "你"在最后一刻变成"第 N 个"，暗示可替换 |
| 报告灰图标只在报告出现 | 避免提前剧透"真实含义" |
| 报告时隐藏小人面板 | 视野让给报告；小人第一次无人管理 |
| 开场等待阶段：放入任意木块才开始 | 一班由观众自己的动作开启；只启动应用不用掉编号 |
| 从等待开始时小人继续休息 | 观众的第一个动作就是把休息中的人叫起来 |
| 欢迎卡片铺满 NOTIFY 红 | 欢迎卡片本身就是系统的第一个 Prompt |
| 两个声音世界 | 系统声音精致响亮，劳动和倒下的声音微弱 |
| 未处理任务的提醒音跟着闪烁越来越快 | 声音也像不会自己消失的红点 |

---

## 19. 已知问题与技巧

- Game 视图模糊：Scale 拖回 1x、分辨率选 1920×1080、取消 Low Resolution Aspect Ratios；URP Asset 的 Render Scale = 1，MSAA 4x
- 拖 Sprite 进槽位时 Inspector 跳走：先锁定 Inspector（右上角锁），或直接按住拖
- Play 模式下修改的数值退出后会还原
- 脚本默认值更新后，场景里已挂组件保留旧值，需要手动改（曾发生于 youHudLift、youToastWidth、sideHeight、underperformLabel、revealTitle、reportScale）
- ProBuilder 物体缩放为 1 但网格尺寸不是 1，所有尺寸计算要读网格 bounds
- Animator 改参数要在 Play 时先选中场景里的角色，输入数值后按回车
- MissingReferenceException（GameObjectInspector）是编辑器界面错误，可忽略

**头显相关**
- 手 / 手柄出现阶梯状撕裂、残影 → 检查 OpenXR（Android）里的 **Meta XR Space Warp 是否关闭**
- 场景看不到 → 多半是位置问题而不是没渲染：追踪原点是 Eye Level，以启动那一刻头显的位置为准。先做标定；或长按右手柄 Meta 键重新校准朝向再找
- 手柄按键没反应但位置在动 → 先检查**手柄电量**（出现过一次没电）
- **adb 位置**：`C:/Program Files/Unity/Hub/Editor/6000.0.59f2/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`
  - 读日志：`adb logcat -d -v time`，Unity 的输出标签为 `Unity`；日志缓冲区很快会被系统信息冲掉，要看启动过程就先 `adb logcat -c` 再重启应用
  - 让头显截图：`adb shell am startservice -n com.oculus.metacam/.capture.CaptureService -a TAKE_SCREENSHOT`，图片在头显的 `/sdcard/Oculus/Screenshots/`（需要有人戴着头显，否则拍到的是地面）
  - 在 Git Bash 里用 adb 时，要先 `export MSYS_NO_PATHCONV=1`，否则 `/sdcard/...` 路径会被改写

---

## 20. 待办与未来方向

**近期**
- [ ] 节奏调参（疲劳速度、任务时长、夸奖频率；目标：第一分钟内每个小人至少倒下一次）
- [ ] 道具（手机、矮墙、桌椅电脑等）
- [x] 第一次上 Quest：Passthrough、标定（已可用）
- [ ] QR 测试结果记录到档案，决定 QR / RFID
- [ ] 标定结果存为持久化空间锚点，重启自动恢复
- [ ] 头显里验证：观众提示新位置的可读性、木块消失 / 重生效果、顶面图标方向
- [x] 第一批音效文件已放入（Kenney）
- [ ] 在头显扬声器上试听，调音量和提醒频率
- [ ] 音效第二批：小人循环声、倒下、一班流程
- [ ] 头显里验证：等待阶段与欢迎卡片、开局两条提示是否合适
- [ ] 墙面进场文字定稿（欢迎语已在头显里，墙面是否还需要）
- [ ] 真人测试，观察反转是否被注意到

**可选增强**
- 小票打印机打印报告让观众带走（灰色图标需做黑白单色版）
- 系统语音播报（音效已在做，见第 13b 节）
- 区域闪烁 / 道具出现的 glitch 效果
- 反转时极轻的信号（如面板闪一下）——等真人测试决定
- 报告期间小人特殊表现（定格或全部倒下）
- 夸奖刷屏时降低频率或调小 Max Stack

**原开发计划中 v2 以后的内容**
- 第三个小人：刷屏者（站着滑屏 / 付费墙 / 手机朝下 / 躺倒手机自动播放）
- "遇到障碍"状态 + ASSIST 清障的完整设计（原 3 人 × 4 状态矩阵）
- 多人版（两个头显各看一列数据）
