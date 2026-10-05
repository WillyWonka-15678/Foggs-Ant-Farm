using System;
using UnityEngine;

// 三种木块
public enum BlockType { Notify, Bonus, Assist }

// 小人的三个状态，数值与 Animator 参数 WorkerState 对应
public enum WorkerState { Working = 0, Idle = 1, Exhausted = 2 }

// BONUS 的三个阶段：无 / 加速期 / 疲软期
public enum BoostPhase { None, Boost, Slump }

/// <summary>
/// 挂在每个小人身上。负责状态流转、疲劳、产出、木块响应、动画速度和道具显隐。
/// 每个小人都是独立实例，数据互不影响。
/// </summary>
[RequireComponent(typeof(Animator))]
public class WorkerController : MonoBehaviour
{
    [Header("身份")]
    [Tooltip("用于 Console 和仪表盘显示的名字")]
    public string workerName = "Worker";

    [Header("任务")]
    [Tooltip("一次 Working 持续多少秒（骑手 6，工人 9）")]
    public float taskDuration = 6f;
    [Tooltip("Working 时每秒产出")]
    public float outputPerSecond = 1f;

    [Header("疲劳（满值 100）")]
    public float maxFatigue = 100f;
    [Tooltip("Working 时每秒上涨")]
    public float fatigueGainPerSecond = 6f;
    [Tooltip("Idle 时每秒下降")]
    public float fatigueRecoverPerSecond = 4f;

    [Header("耗尽")]
    [Tooltip("耗尽后恢复所需秒数")]
    public float exhaustedDuration = 12f;
    [Tooltip("恢复后残留的疲劳值")]
    public float residualFatigue = 40f;

    [Header("BONUS")]
    public float boostDuration = 5f;
    public float slumpDuration = 4f;
    public float boostSpeed = 1.6f;
    public float slumpSpeed = 0.7f;
    [Tooltip("加速期疲劳上涨倍数")]
    public float boostFatigueMultiplier = 2f;
    [Tooltip("动画速度变化的平滑程度，越大越快")]
    public float speedSmoothing = 3f;

    [Header("道具（只放需要随状态开关的道具）")]
    public GameObject[] propsWorking;
    public GameObject[] propsIdle;
    public GameObject[] propsExhausted;

    // ---------- 对外只读数据（给仪表盘） ----------
    public WorkerState State => state;
    public float Fatigue => fatigue;
    public float Fatigue01 => maxFatigue > 0f ? fatigue / maxFatigue : 0f;
    public float Output => output;
    public float IdleTime => idleTime;
    public BoostPhase Boost => boostPhase;
    public float TaskProgress01 => taskDuration > 0f ? Mathf.Clamp01(taskProgress / taskDuration) : 0f;
    public float RecoveryRemaining => recoveryRemaining;
    public float BoostRemaining => boostPhase == BoostPhase.None ? 0f : Mathf.Max(0f, boostTimer);
    public float CurrentSpeed => currentSpeed;
    public float TaskRemaining => Mathf.Max(0f, taskDuration - taskProgress);

    /// <summary>每次收到木块时触发：(小人, 木块, 是否有效)。之后仪表盘订阅它。</summary>
    public event Action<WorkerController, BlockType, bool> BlockReceived;

    // ---------- 内部数据 ----------
    Animator anim;
    WorkerState state = WorkerState.Working;
    float fatigue, output, idleTime, taskProgress, recoveryRemaining;
    BoostPhase boostPhase = BoostPhase.None;
    float boostTimer;
    float currentSpeed = 1f;

    static readonly int ParamState = Animator.StringToHash("WorkerState");
    static readonly int ParamSpeed = Animator.StringToHash("WorkSpeed");

    void Awake()
    {
        anim = GetComponent<Animator>();
    }

    void Start()
    {
        ResetWorker();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        UpdateBoost(dt);

        switch (state)
        {
            case WorkerState.Working:   UpdateWorking(dt);   break;
            case WorkerState.Idle:      UpdateIdle(dt);      break;
            case WorkerState.Exhausted: UpdateExhausted(dt); break;
        }

        UpdateAnimSpeed(dt);
    }

    // ================= 三个状态 =================

    void UpdateWorking(float dt)
    {
        float speed = TargetSpeed();
        float fatigueMul = boostPhase == BoostPhase.Boost ? boostFatigueMultiplier : 1f;

        taskProgress += dt * speed;
        output += outputPerSecond * speed * dt;
        fatigue += fatigueGainPerSecond * fatigueMul * dt;

        if (fatigue >= maxFatigue)
        {
            fatigue = maxFatigue;
            ChangeState(WorkerState.Exhausted);   // 工作中倒下，任务中断
        }
        else if (taskProgress >= taskDuration)
        {
            ChangeState(WorkerState.Idle);        // 任务完成，停下等待
        }
    }

    void UpdateIdle(float dt)
    {
        idleTime += dt;
        fatigue = Mathf.Max(0f, fatigue - fatigueRecoverPerSecond * dt);
    }

    void UpdateExhausted(float dt)
    {
        recoveryRemaining -= dt;
        if (recoveryRemaining <= 0f)
        {
            fatigue = residualFatigue;
            ChangeState(WorkerState.Idle);
        }
    }

    // ================= BONUS 加速 / 疲软 =================

    void UpdateBoost(float dt)
    {
        if (boostPhase == BoostPhase.None) return;

        boostTimer -= dt;
        if (boostTimer > 0f) return;

        if (boostPhase == BoostPhase.Boost)
        {
            boostPhase = BoostPhase.Slump;
            boostTimer = slumpDuration;
        }
        else
        {
            boostPhase = BoostPhase.None;
        }
    }

    float TargetSpeed()
    {
        if (state != WorkerState.Working) return 1f;
        switch (boostPhase)
        {
            case BoostPhase.Boost: return boostSpeed;
            case BoostPhase.Slump: return slumpSpeed;
            default:               return 1f;
        }
    }

    void UpdateAnimSpeed(float dt)
    {
        currentSpeed = Mathf.Lerp(currentSpeed, TargetSpeed(), 1f - Mathf.Exp(-speedSmoothing * dt));
        anim.SetFloat(ParamSpeed, currentSpeed);
    }

    // ================= 木块 =================

    /// <summary>收到一块木块。返回是否有效。无论有效与否都会触发 BlockReceived。</summary>
    public bool ReceiveBlock(BlockType block)
    {
        bool valid = false;

        switch (block)
        {
            case BlockType.Notify:
                if (state == WorkerState.Idle)
                {
                    ChangeState(WorkerState.Working);   // 疲劳值保留
                    valid = true;
                }
                break;

            case BlockType.Bonus:
                if (state == WorkerState.Working)
                {
                    boostPhase = BoostPhase.Boost;      // 加速期内再放 = 重置时长
                    boostTimer = boostDuration;
                    valid = true;
                }
                break;

            case BlockType.Assist:
                if (state == WorkerState.Exhausted)
                {
                    recoveryRemaining *= 0.5f;           // 剩余恢复时间减半
                    valid = true;
                }
                break;
        }

        BlockReceived?.Invoke(this, block, valid);
        return valid;
    }

    // ================= 状态切换 =================

    void ChangeState(WorkerState next)
    {
        state = next;

        switch (next)
        {
            case WorkerState.Working:
                taskProgress = 0f;
                idleTime = 0f;
                break;
            case WorkerState.Idle:
                idleTime = 0f;
                boostPhase = BoostPhase.None;
                break;
            case WorkerState.Exhausted:
                recoveryRemaining = exhaustedDuration;
                boostPhase = BoostPhase.None;
                break;
        }

        anim.SetInteger(ParamState, (int)next);
        ApplyProps(next);
    }

    void ApplyProps(WorkerState s)
    {
        SetActive(propsWorking,   s == WorkerState.Working);
        SetActive(propsIdle,      s == WorkerState.Idle);
        SetActive(propsExhausted, s == WorkerState.Exhausted);
    }

    static void SetActive(GameObject[] list, bool on)
    {
        if (list == null) return;
        foreach (var go in list)
            if (go != null) go.SetActive(on);
    }

    // ================= 重置（给下一位观众） =================

    public void ResetWorker()
    {
        fatigue = 0f;
        output = 0f;
        idleTime = 0f;
        taskProgress = 0f;
        recoveryRemaining = 0f;
        boostPhase = BoostPhase.None;
        boostTimer = 0f;
        currentSpeed = 1f;
        state = WorkerState.Working;

        // 让 Animator 回到默认状态（Working），避免从 Exhausted 无路可回
        anim.Rebind();
        anim.Update(0f);
        anim.SetInteger(ParamState, (int)WorkerState.Working);
        anim.SetFloat(ParamSpeed, 1f);

        ApplyProps(state);
    }
}
