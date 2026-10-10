using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一条系统消息。
///   Target     = 相关的小人；null 表示属于观众本人（显示在箱子正前方的观众提示堆）
///   IsResolved = 任务完成条件；不为 null 的是"任务型"消息，做完才消失
///   IsVoid     = 失效条件：情况自己变了，任务不再成立
///   Duration   = 大于 0 时定时消失（奖励型）；小于等于 0 时一直保留
/// </summary>
public class PromptMessage
{
    /// <summary>系统对观众做了什么（报告计数、YOU 标题色、飞进 YOU 的图标、闪烁节奏）</summary>
    public BlockType Kind;
    /// <summary>系统要观众放哪块木块；null = 不需要特定木块</summary>
    public BlockType? Action;
    /// <summary>提示条上显示的颜色和图标：有要求的木块就显示它，否则显示 Kind</summary>
    public BlockType Visual => Action ?? Kind;
    public Func<string> Text;
    public float Duration;
    public WorkerController Target;
    public Func<bool> IsResolved;
    public Func<bool> IsVoid;
    public bool IsTask => IsResolved != null;
}

/// <summary>
/// 系统的声音。每条消息都被归类成三种木块之一：
///   教学提示 = ASSIST，警报 = NOTIFY，夸奖 = BONUS
/// 由 SessionManager 持有和调用，不需要挂在物体上。
/// </summary>
public class PromptSystem
{
    /// <summary>每发出一条消息触发一次（报告里的 Interventions received）</summary>
    public event Action<BlockType> Shown;
    /// <summary>每发出一条消息触发一次（悬浮提示用它来显示）</summary>
    public event Action<PromptMessage> Posted;

    /// <summary>系统最近一次对观众用了哪块木块（YOU 标题颜色用）</summary>
    public BlockType Kind { get; private set; }
    public bool HasLastKind { get; private set; }

    bool hintIdle, hintBonus, hintExhausted;
    int validCount;
    int totalActions;
    readonly Dictionary<WorkerController, int[]> validByWorker = new Dictionary<WorkerController, int[]>();

    static readonly string[] Praise =
    {
        "Outstanding, Team Lead!",
        "Great job! Keep it up!",
        "Efficiency rising!",
        "Your team is thriving!",
        "Top performance!"
    };

    public void Reset()
    {
        HasLastKind = false;
        hintIdle = hintBonus = hintExhausted = false;
        validCount = 0;
        totalActions = 0;
        validByWorker.Clear();
    }

    // 保留给 SessionManager 调用，悬浮提示自己处理生命周期
    public void Clear() { }
    public void Tick(float dt) { }

    int Valid(WorkerController w, BlockType b)
    {
        return validByWorker.TryGetValue(w, out var arr) ? arr[(int)b] : 0;
    }

    void Post(BlockType kind, Func<string> text, float duration,
              WorkerController target = null, Func<bool> resolved = null, Func<bool> isVoid = null,
              BlockType? action = null)
    {
        Kind = kind;
        HasLastKind = true;
        Shown?.Invoke(kind);
        Posted?.Invoke(new PromptMessage
        {
            Kind = kind, Action = action, Text = text, Duration = duration,
            Target = target, IsResolved = resolved, IsVoid = isVoid
        });
    }

    // ---------------- 触发点 ----------------

    /// <summary>某个小人进入需要处理的状态 → 任务型消息</summary>
    public void WorkerNeeds(WorkerController w, WorkerState s)
    {
        string n = w.workerName;

        if (s == WorkerState.Idle)
        {
            int before = Valid(w, BlockType.Notify);
            Func<bool> done = () => Valid(w, BlockType.Notify) > before;
            Func<bool> gone = () => w.State != WorkerState.Idle;

            if (!hintIdle)
            {
                hintIdle = true;
                Post(BlockType.Assist, () => $"{n} is idle. Place NOTIFY to reactivate.", -1f, w, done, gone, BlockType.Notify);
            }
            else
            {
                Post(BlockType.Notify, () => $"{n} idle  {HUDFactory.ClockUp(w.IdleTime)}  productivity loss", -1f, w, done, gone, BlockType.Notify);
            }
        }
        else if (s == WorkerState.Exhausted)
        {
            int before = Valid(w, BlockType.Assist);
            Func<bool> done = () => Valid(w, BlockType.Assist) > before;
            Func<bool> gone = () => w.State != WorkerState.Exhausted;

            if (!hintExhausted)
            {
                hintExhausted = true;
                Post(BlockType.Assist, () => $"{n} is exhausted. Use ASSIST to restore.", -1f, w, done, gone, BlockType.Assist);
            }
            else
            {
                Post(BlockType.Notify, () => $"{n} down. Output suspended.", -1f, w, done, gone, BlockType.Assist);
            }
        }
    }

    /// <summary>观众放了一块木块（有效或无效都要调用）</summary>
    public void BlockPlaced(WorkerController w, BlockType b, bool valid)
    {
        totalActions++;
        if (!valid) return;

        if (!validByWorker.TryGetValue(w, out var arr))
        {
            arr = new int[3];
            validByWorker[w] = arr;
        }
        arr[(int)b]++;
        validCount++;

        if (b == BlockType.Notify && !hintBonus)
        {
            hintBonus = true;
            string n = w.workerName;
            int before = Valid(w, BlockType.Bonus);
            Post(BlockType.Assist, () => $"Boost {n} with BONUS.", -1f, w,
                 () => Valid(w, BlockType.Bonus) > before,
                 () => w.State != WorkerState.Working,
                 BlockType.Bonus);
            return;
        }

        if (validCount % 3 == 0)
        {
            string p = Praise[UnityEngine.Random.Range(0, Praise.Length)];
            Post(BlockType.Bonus, () => p, 3.5f);
        }
    }

    public void QuotaReached(float newTarget)
    {
        Post(BlockType.Bonus, () => $"Quota reached! New target: {newTarget:0}", 4f);
    }

    /// <summary>观众停手 → 任务型：任何一次操作即完成</summary>
    public void Inactive(SessionManager s)
    {
        int before = totalActions;
        Post(BlockType.Notify,
             () => $"Team Lead inactive  {Mathf.Max(0f, s.inactivityLimit - s.InactiveTime):0}s",
             -1f, null,
             () => totalActions > before,
             () => s.Phase != ShiftPhase.Running);
    }

    /// <summary>班次即将结束 → 一直显示到结束</summary>
    public void ShiftEnding(SessionManager s)
    {
        Post(BlockType.Notify, () => $"Shift ending in {HUDFactory.ClockDown(s.ShiftRemaining)}",
             -1f, null, null, () => s.Phase != ShiftPhase.Running);
    }
}
