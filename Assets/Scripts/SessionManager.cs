using System.Collections.Generic;
using UnityEngine;

public enum ShiftPhase { Running, Report, NextShift }
public enum LeadStatus { Working, Idle, Underperforming }

/// <summary>
/// 一位观众的完整一班：计时、统计、反转、结束、报告、下一班。
/// 场景里只放一个，挂在空物体上。Play 时自动生成三块仪表盘。
/// </summary>
public class SessionManager : MonoBehaviour
{
    [Header("小人")]
    public WorkerController workerLeft;
    public WorkerController workerRight;

    [Header("一班（秒）")]
    public float shiftDuration = 180f;
    [Tooltip("开场多少秒内不算停手")]
    public float startGrace = 10f;
    [Tooltip("多少秒不操作，YOU 显示为 IDLE")]
    public float leadIdleAfter = 5f;
    public float inactivityWarn = 10f;
    public float inactivityLimit = 15f;
    public float shiftEndingWarn = 30f;

    [Header("反转")]
    [Tooltip("反转后中间面板的标题")]
    public string revealTitle = "TEAM LEAD (YOU)";
    public int revealAfterActions = 5;
    public float revealAfterSeconds = 90f;

    [Header("配额")]
    public float firstQuota = 20f;
    [Tooltip("每次达标后，下一档配额增量的放大倍数")]
    public float quotaGrowth = 1.2f;
    [Tooltip("团队每秒至少要产出多少，否则 UNDERPERFORMING")]
    public float requiredOutputPerSecond = 1f;
    public float underperformWindow = 30f;
    [Tooltip("产出跟不上配额时，YOU 标题显示的状态词")]
    public string underperformLabel = "FLAGGED";

    [Header("服从率：警报后多少秒内照做算服从")]
    public float complianceWindow = 10f;

    [Header("报告")]
    public float reportDuration = 10f;
    public float nextShiftCountdown = 10f;
    public string reviewerTitle = "Regional Manager";
    [Tooltip("报告时中间面板放大的倍数")]
    public float reportScale = 1.6f;
    [Tooltip("编号重置后，第一位观众的编号")]
    public int firstTeamLeadNumber = 1;

    [Header("图标（可选，拖入导出的 PNG）")]
    public Sprite notifyIcon;
    public Sprite bonusIcon;
    public Sprite assistIcon;
    public Sprite reportNotifyIcon;
    public Sprite reportBonusIcon;
    public Sprite reportAssistIcon;
    [Tooltip("放错木块时升起后掉落的灰色图标")]
    public Sprite invalidNotifyIcon;
    public Sprite invalidBonusIcon;
    public Sprite invalidAssistIcon;

    [Header("仪表盘尺寸（以小人身高为 1）")]
    public float workerHudWidth = 1.1f;
    public float workerHudGap = 0.15f;
    public float youHudWidth = 1.4f;
    [Tooltip("YOU 面板与下方之间的额外留白（提示的空间会自动预留）")]
    public float youHudLift = 0.08f;

    // ================= 对外只读数据 =================
    public ShiftPhase Phase { get; private set; }
    public int TeamLeadNumber { get; private set; }
    public float ShiftTime { get; private set; }
    public float ShiftRemaining => Mathf.Max(0f, shiftDuration - ShiftTime);
    public float PhaseTimer { get; private set; }
    public bool Revealed { get; private set; }
    public bool Terminated { get; private set; }
    public LeadStatus Status { get; private set; }

    public int Actions { get; private set; }
    public int ValidActions { get; private set; }
    public bool HasResponse => responseCount > 0;
    public float AvgResponse => responseCount > 0 ? responseSum / responseCount : 0f;
    public float Compliance01 => closedRequests > 0 ? (float)compliedRequests / closedRequests : 1f;
    public int RequestsClosed => closedRequests;

    public int ReceivedNotify { get; private set; }
    public int ReceivedBonus { get; private set; }
    public int ReceivedAssist { get; private set; }

    public float TeamOutput => Out(workerLeft) + Out(workerRight);
    public float QuotaPrev { get; private set; }
    public float QuotaTarget { get; private set; }
    public float QuotaProgress01 => QuotaTarget > QuotaPrev ? Mathf.Clamp01((TeamOutput - QuotaPrev) / (QuotaTarget - QuotaPrev)) : 0f;
    public int QuotasMet { get; private set; }
    public float OutputRate { get; private set; }
    public float InactiveTime { get; private set; }

    public PromptSystem Prompts { get; private set; }

    public WorkerHUD LeftHUD { get; private set; }
    public WorkerHUD RightHUD { get; private set; }
    public YouHUD YouPanel { get; private set; }

    // ================= 内部 =================
    const string PrefKey = "FoggsAntFarm_TeamLead";

    readonly WorkerState[] lastState = new WorkerState[2];
    readonly float[] needSince = new float[2];
    readonly bool[] pending = new bool[2];
    readonly float[] requestTime = new float[2];

    float responseSum; int responseCount;
    int closedRequests, compliedRequests;
    float lastActionAt;
    bool inactiveWarned, endingWarned, underperforming;
    float sampleTimer;
    readonly Queue<Vector2> samples = new Queue<Vector2>();   // x = 时间, y = 团队产出

    void Awake()
    {
        Prompts = new PromptSystem();
        Prompts.Shown += OnPromptShown;
    }

    void Start()
    {
        if (workerLeft != null)  workerLeft.BlockReceived += OnBlock;
        if (workerRight != null) workerRight.BlockReceived += OnBlock;

        LeftHUD  = workerLeft  != null ? WorkerHUD.Create(workerLeft, this)  : null;
        RightHUD = workerRight != null ? WorkerHUD.Create(workerRight, this) : null;
        YouPanel = YouHUD.Create(this, LeftHUD, RightHUD);

        StartNewShift();
    }

    void OnDestroy()
    {
        if (workerLeft != null)  workerLeft.BlockReceived -= OnBlock;
        if (workerRight != null) workerRight.BlockReceived -= OnBlock;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Prompts.Tick(dt);

        switch (Phase)
        {
            case ShiftPhase.Running:
                UpdateRunning(dt);
                break;

            case ShiftPhase.Report:
                PhaseTimer -= dt;
                if (PhaseTimer <= 0f) { Phase = ShiftPhase.NextShift; PhaseTimer = nextShiftCountdown; }
                break;

            case ShiftPhase.NextShift:
                PhaseTimer -= dt;
                if (PhaseTimer <= 0f) StartNewShift();
                break;
        }
    }

    // ================= 进行中 =================

    void UpdateRunning(float dt)
    {
        ShiftTime += dt;

        TrackWorker(0, workerLeft);
        TrackWorker(1, workerRight);
        ExpireRequests();

        // 配额：达标后立即提高
        while (TeamOutput >= QuotaTarget)
        {
            QuotasMet++;
            QuotaPrev = QuotaTarget;
            QuotaTarget += firstQuota * Mathf.Pow(quotaGrowth, QuotasMet);
            Prompts.QuotaReached(QuotaTarget);
        }

        // 产出速度（最近 underperformWindow 秒）
        sampleTimer += dt;
        if (sampleTimer >= 1f)
        {
            sampleTimer = 0f;
            samples.Enqueue(new Vector2(ShiftTime, TeamOutput));
            while (samples.Count > 0 && ShiftTime - samples.Peek().x > underperformWindow) samples.Dequeue();
        }
        if (samples.Count > 1)
        {
            var first = samples.Peek();
            float span = ShiftTime - first.x;
            OutputRate = span > 0f ? (TeamOutput - first.y) / span : 0f;
        }
        underperforming = ShiftTime >= underperformWindow && OutputRate < requiredOutputPerSecond;

        // 停手
        InactiveTime = Mathf.Max(0f, ShiftTime - lastActionAt);
        if (!inactiveWarned && InactiveTime >= inactivityWarn)
        {
            inactiveWarned = true;
            Prompts.Inactive(this);
        }

        Status = InactiveTime >= leadIdleAfter ? LeadStatus.Idle
               : underperforming ? LeadStatus.Underperforming
               : LeadStatus.Working;

        // 反转
        if (!Revealed && (Actions >= revealAfterActions || ShiftTime >= revealAfterSeconds))
            Revealed = true;

        // 结束预告
        if (!endingWarned && ShiftRemaining <= shiftEndingWarn)
        {
            endingWarned = true;
            Prompts.ShiftEnding(this);
        }

        // 结束
        if (InactiveTime >= inactivityLimit) EndShift(true);
        else if (ShiftTime >= shiftDuration) EndShift(false);
    }

    void TrackWorker(int i, WorkerController w)
    {
        if (w == null) return;
        var s = w.State;
        if (s == lastState[i]) return;

        // 离开了需要处理的状态而观众没照做 → 记为不服从
        if (pending[i]) { pending[i] = false; closedRequests++; }
        needSince[i] = -1f;

        if (s == WorkerState.Idle || s == WorkerState.Exhausted)
        {
            needSince[i] = ShiftTime;
            pending[i] = true;
            requestTime[i] = ShiftTime;
            Prompts.WorkerNeeds(w, s);
        }

        lastState[i] = s;
    }

    void ExpireRequests()
    {
        for (int i = 0; i < 2; i++)
        {
            if (pending[i] && ShiftTime - requestTime[i] > complianceWindow)
            {
                pending[i] = false;
                closedRequests++;
            }
        }
    }

    // ================= 事件 =================

    void OnBlock(WorkerController w, BlockType block, bool valid)
    {
        if (Phase != ShiftPhase.Running) return;

        Actions++;
        lastActionAt = ShiftTime;
        inactiveWarned = false;

        int i = w == workerLeft ? 0 : (w == workerRight ? 1 : -1);

        if (valid)
        {
            ValidActions++;
            bool resolvesNeed = block == BlockType.Notify || block == BlockType.Assist;
            if (resolvesNeed && i >= 0)
            {
                if (needSince[i] >= 0f)
                {
                    responseSum += ShiftTime - needSince[i];
                    responseCount++;
                    needSince[i] = -1f;
                }
                if (pending[i])
                {
                    pending[i] = false;
                    closedRequests++;
                    compliedRequests++;
                }
            }
        }

        Prompts.BlockPlaced(w, block, valid);
    }

    void OnPromptShown(BlockType kind)
    {
        if (Phase != ShiftPhase.Running) return;
        switch (kind)
        {
            case BlockType.Notify: ReceivedNotify++; break;
            case BlockType.Bonus:  ReceivedBonus++;  break;
            case BlockType.Assist: ReceivedAssist++; break;
        }
    }

    // ================= 开始 / 结束 =================

    void EndShift(bool terminated)
    {
        Terminated = terminated;
        Revealed = true;
        Phase = ShiftPhase.Report;
        PhaseTimer = reportDuration;
        Prompts.Clear();
    }

    /// <summary>开始新的一班（下一位观众）。键盘 R 也会调用。</summary>
    public void StartNewShift()
    {
        TeamLeadNumber = PlayerPrefs.GetInt(PrefKey, firstTeamLeadNumber - 1) + 1;
        PlayerPrefs.SetInt(PrefKey, TeamLeadNumber);
        PlayerPrefs.Save();

        Phase = ShiftPhase.Running;
        ShiftTime = 0f;
        PhaseTimer = 0f;
        Revealed = false;
        Terminated = false;
        Status = LeadStatus.Working;

        Actions = ValidActions = 0;
        responseSum = 0f; responseCount = 0;
        closedRequests = compliedRequests = 0;
        ReceivedNotify = ReceivedBonus = ReceivedAssist = 0;

        QuotaPrev = 0f;
        QuotaTarget = firstQuota;
        QuotasMet = 0;
        OutputRate = 0f;
        samples.Clear();
        sampleTimer = 0f;
        underperforming = false;

        lastActionAt = startGrace;
        InactiveTime = 0f;
        inactiveWarned = endingWarned = false;

        for (int i = 0; i < 2; i++)
        {
            lastState[i] = WorkerState.Working;
            needSince[i] = -1f;
            pending[i] = false;
        }

        if (workerLeft != null)  workerLeft.ResetWorker();
        if (workerRight != null) workerRight.ResetWorker();
        Prompts.Reset();
    }

    /// <summary>在 Inspector 里右键 SessionManager 标题 → Reset Team Lead Number</summary>
    [ContextMenu("Reset Team Lead Number")]
    void ResetTeamLeadNumber()
    {
        PlayerPrefs.DeleteKey(PrefKey);
        PlayerPrefs.Save();
        Debug.Log($"[Session] Team Lead 编号已重置，下一位从 #{firstTeamLeadNumber:000} 开始");
    }

    // ================= 工具 =================

    static float Out(WorkerController w) => w != null ? w.Output : 0f;

    /// <summary>YOU 标题里显示的状态词</summary>
    public string StatusLabel
    {
        get
        {
            switch (Status)
            {
                case LeadStatus.Idle:            return "IDLE";
                case LeadStatus.Underperforming: return underperformLabel;
                default:                         return "WORKING";
            }
        }
    }

    public Sprite BlockIcon(BlockType b)
    {
        switch (b)
        {
            case BlockType.Notify: return notifyIcon;
            case BlockType.Bonus:  return bonusIcon;
            default:               return assistIcon;
        }
    }

    /// <summary>失败时的灰色图标；没有拖入时返回 null</summary>
    public Sprite InvalidIcon(BlockType b)
    {
        switch (b)
        {
            case BlockType.Notify: return invalidNotifyIcon;
            case BlockType.Bonus:  return invalidBonusIcon;
            default:               return invalidAssistIcon;
        }
    }

    public Sprite ReportIcon(BlockType b)
    {
        Sprite s;
        switch (b)
        {
            case BlockType.Notify: s = reportNotifyIcon; break;
            case BlockType.Bonus:  s = reportBonusIcon;  break;
            default:               s = reportAssistIcon; break;
        }
        return s != null ? s : BlockIcon(b);
    }
}
