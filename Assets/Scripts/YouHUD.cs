using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 中间的面板。等待阶段是欢迎卡片，反转前是 TEAM OUTPUT，反转后变成 TEAM LEAD #，结束时变成报告。
/// 反转后标题颜色 = 系统最近一次对观众用了哪块木块。
/// 由 SessionManager 自动生成，不需要手动挂。
/// </summary>
public class YouHUD : MonoBehaviour
{
    const float W = 460f;
    const float H_LIVE = 256f, H_REPORT = 360f;
    const float H_WELCOME = H_LIVE;   // 与进行中的 YOU 面板同样大小
    const float FadeSpeed = 3f;       // 与小人面板的淡入淡出速度一致
    float curH = H_REPORT;   // 与创建时的画布高度一致

    SessionManager s;
    WorkerHUD left, right;
    float baseHeight = 1f;

    public float BaseHeight => baseHeight;

    /// <summary>YOU 面板下方需要预留的空间（米），由 FloatingPrompts 设置，用来放观众的提示</summary>
    public float ReservedBelow { get; set; }
    bool placed;
    float currentY;
    bool hasCase;
    Bounds caseLocal;
    float scaleMul = 1f;
    float baseScale;
    public float WorldWidth => baseHeight * s.youHudWidth * scaleMul;
    public float WorldPanelHeight => WorldWidth * curH / W;
    public Vector3 PanelCenter => transform.position + transform.up * (WorldPanelHeight * 0.5f);

    RectTransform canvasRt;
    Image panel, header;
    TextMeshProUGUI title;

    // 等待阶段的欢迎卡片与 YOU 面板交叉淡入淡出
    CanvasGroup mainGroup, welcomeGroup;

    // 进行中
    GameObject live, revealOnly;
    TextMeshProUGUI quotaLabel, complianceLabel, statsLabel, receivedLabel, shiftLabel;
    UIBar quotaBar, complianceBar;

    // 报告
    GameObject report;
    TextMeshProUGUI reportText, reviewedText, footerText;
    readonly Image[] repIcons = new Image[3];
    readonly TextMeshProUGUI[] repCounts = new TextMeshProUGUI[3];

    public static YouHUD Create(SessionManager session, WorkerHUD l, WorkerHUD r)
    {
        var go = new GameObject("HUD_YOU");
        var hud = go.AddComponent<YouHUD>();
        hud.Init(session, l, r);
        return hud;
    }

    void Init(SessionManager session, WorkerHUD l, WorkerHUD r)
    {
        s = session; left = l; right = r;

        int n = 0; float sum = 0f;
        if (left != null)  { sum += left.WorkerHeight;  n++; }
        if (right != null) { sum += right.WorkerHeight; n++; }
        baseHeight = n > 0 ? sum / n : 1f;

        var c = HUDFactory.CreateCanvas(transform, "Canvas", W, H_REPORT);
        canvasRt = c;
        baseScale = baseHeight * s.youHudWidth / W;
        c.localScale = Vector3.one * baseScale;

        // YOU 面板的全部内容放在 Main 下，方便整体淡入淡出
        var main = HUDFactory.Rect(c, "Main", 0, 0, W, H_REPORT);
        mainGroup = main.gameObject.AddComponent<CanvasGroup>();

        panel = HUDFactory.Box(main, "Panel", 0, 0, W, H_REPORT, Palette.Panel);
        header = HUDFactory.Box(main, "Header", 0, 0, W, 52, Palette.Gray);
        title = HUDFactory.Label(main, "Title", 18, 0, W - 36, 52, 25, Color.white, bold: true);

        // ---------- 进行中 ----------
        var liveRt = HUDFactory.Rect(main, "Live", 0, 0, W, H_REPORT);
        live = liveRt.gameObject;

        quotaLabel = HUDFactory.Label(liveRt, "QuotaLabel", 18, 60, 424, 26, 19, Palette.Dim);
        quotaBar   = HUDFactory.Bar(liveRt, "QuotaBar", 18, 88, 424, 12, Palette.BarLight);

        var revRt = HUDFactory.Rect(liveRt, "RevealOnly", 0, 0, W, H_REPORT);
        revealOnly = revRt.gameObject;
        complianceLabel = HUDFactory.Label(revRt, "ComplianceLabel", 18, 108, 424, 26, 19, Palette.Dim);
        complianceBar   = HUDFactory.Bar(revRt, "ComplianceBar", 18, 136, 424, 12, Palette.Bonus);
        statsLabel      = HUDFactory.Label(revRt, "Stats", 18, 156, 424, 28, 19, Palette.BarLight);
        receivedLabel   = HUDFactory.Label(revRt, "Received", 18, 186, 424, 28, 19, Palette.BarLight);

        shiftLabel = HUDFactory.Label(liveRt, "Shift", 18, 218, 424, 28, 19, Palette.BarLight);

        // ---------- 报告 ----------
        var repRt = HUDFactory.Rect(main, "Report", 0, 0, W, H_REPORT);
        report = repRt.gameObject;

        reportText = HUDFactory.Label(repRt, "ReportText", 18, 58, 424, 176, 19, Palette.BarLight,
                                      TextAlignmentOptions.TopLeft);
        BlockType[] order = { BlockType.Notify, BlockType.Bonus, BlockType.Assist };
        for (int i = 0; i < 3; i++)
        {
            float x = 18 + i * 142;
            repIcons[i]  = HUDFactory.Icon(repRt, $"RepIcon{i}", x, 238, 44, 44);
            repIcons[i].sprite = s.ReportIcon(order[i]);
            repIcons[i].enabled = true;
            // 没有拖入图标时，用木块颜色的色块代替
            if (repIcons[i].sprite == null) repIcons[i].color = Palette.ForBlock(order[i]);
            repCounts[i] = HUDFactory.Label(repRt, $"RepCount{i}", x + 50, 238, 88, 44, 22, Color.white, bold: true);
        }
        reviewedText = HUDFactory.Label(repRt, "Reviewed", 18, 290, 424, 28, 18, Palette.Dim);
        footerText   = HUDFactory.Label(repRt, "Footer", 18, 320, 424, 32, 21, Palette.Yellow, bold: true);

        BuildWelcome(c);

        // 启动时处于等待阶段：直接显示欢迎卡片，不播放淡入
        bool waiting = s.Phase == ShiftPhase.Waiting;
        mainGroup.alpha = waiting ? 0f : 1f;
        welcomeGroup.alpha = waiting ? 1f : 0f;
    }

    /// <summary>欢迎卡片：贴在画布底边（和 YOU 面板同一个挂点），面板高度变化时不会跳动</summary>
    void BuildWelcome(RectTransform c)
    {
        var rt = HUDFactory.Rect(c, "Welcome", 0, 0, W, H_WELCOME);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        welcomeGroup = rt.gameObject.AddComponent<CanvasGroup>();

        // 整张卡片铺满 NOTIFY 红：欢迎卡片本身就是系统发出的第一个"提示"（Prompt）
        Color red = Palette.Notify;
        red.a = 0.95f;
        HUDFactory.Box(rt, "Panel", 0, 0, W, H_WELCOME, red);
        HUDFactory.Box(rt, "Header", 0, 0, W, 52, Color.Lerp(Palette.Notify, Color.black, 0.25f));
        HUDFactory.Label(rt, "Title", 18, 0, W - 36, 52, 25, Color.white, bold: true).text = "Welcome, Team Lead.";

        HUDFactory.Label(rt, "Ready", 18, 70, 424, 30, 21, Color.white).text = "Your team is ready.";
        HUDFactory.Label(rt, "Begin", 18, 106, 424, 60, 21, Color.white,
                         TextAlignmentOptions.TopLeft, wrap: true, bold: true).text = "Place any block into the box to begin your shift.";

        // 小字：红底上灰色看不清，用半透明白色，依然比正文弱
        HUDFactory.Label(rt, "Consent", 18, 204, 424, 40, 13, new Color(1f, 1f, 1f, 0.7f),
                         TextAlignmentOptions.BottomLeft, wrap: true).text =
            "By beginning your shift, you agree that your performance may be recorded.";
    }

    void LateUpdate()
    {
        Place();
        HUDFactory.FaceCamera(transform);
        Refresh();
    }

    void Place()
    {
        Vector3 p;
        if (left != null && right != null)
            p = (left.transform.position + right.transform.position) * 0.5f;
        else if (left != null)  p = left.transform.position;
        else if (right != null) p = right.transform.position;
        else return;

        // 放大：报告期间平滑放大。等待阶段的欢迎卡片和进行中的 YOU 面板在同一位置、同一大小
        bool running = s.Phase == ShiftPhase.Running || s.Phase == ShiftPhase.Waiting;
        float targetScale = running ? 1f : Mathf.Max(1f, s.reportScale);
        scaleMul = Mathf.Lerp(scaleMul, targetScale, 1f - Mathf.Exp(-6f * Time.deltaTime));
        canvasRt.localScale = Vector3.one * (baseScale * scaleMul);

        float targetY;
        if (running)
        {
            // 固定在小人面板正上方，不受系统提示影响
            float top = float.MinValue;
            if (left != null)  top = Mathf.Max(top, left.transform.position.y  + left.WorldPanelHeight);
            if (right != null) top = Mathf.Max(top, right.transform.position.y + right.WorldPanelHeight);
            targetY = top + baseHeight * s.youHudLift;
        }
        else
        {
            // 报告：降到原来小人面板的位置，占据视野中央
            float bottom = float.MaxValue;
            if (left != null)  bottom = Mathf.Min(bottom, left.transform.position.y);
            if (right != null) bottom = Mathf.Min(bottom, right.transform.position.y);
            targetY = bottom;
        }

        if (!placed) { currentY = targetY; placed = true; }
        currentY = Mathf.Lerp(currentY, targetY, 1f - Mathf.Exp(-6f * Time.deltaTime));
        p.y = currentY;

        // 往远离观众的方向推
        p += BackOffset();
        transform.position = p;
    }

    Vector3 BackOffset()
    {
        var box = s.caseBox;
        if (box == null || s.youHudBack == 0f) return Vector3.zero;

        if (!hasCase)
        {
            var mf = box.GetComponent<MeshFilter>();
            caseLocal = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            hasCase = true;
        }

        // 观众在箱子的哪一侧，就往另一侧推
        float sign = -1f;
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 camLocal = box.InverseTransformPoint(cam.transform.position);
            sign = camLocal.z >= caseLocal.center.z ? -1f : 1f;
        }
        Vector3 back = box.forward; back.y = 0f;
        back = back.sqrMagnitude > 1e-6f ? back.normalized : Vector3.forward;

        float depth = Mathf.Abs(caseLocal.size.z * box.lossyScale.z);
        return back * sign * depth * s.youHudBack;
    }

    void Refresh()
    {
        // 等待阶段：YOU 面板淡出、欢迎卡片淡入；开始后反过来
        bool waiting = s.Phase == ShiftPhase.Waiting;
        float step = Time.deltaTime * FadeSpeed;
        mainGroup.alpha = Mathf.MoveTowards(mainGroup.alpha, waiting ? 0f : 1f, step);
        welcomeGroup.alpha = Mathf.MoveTowards(welcomeGroup.alpha, waiting ? 1f : 0f, step);
        welcomeGroup.gameObject.SetActive(welcomeGroup.alpha > 0f);
        if (waiting && mainGroup.alpha <= 0f) return;   // YOU 面板已完全隐藏，不用更新

        bool running = s.Phase == ShiftPhase.Running;
        live.SetActive(running);
        report.SetActive(!running);
        SetHeight(running ? H_LIVE : H_REPORT);

        if (running) RefreshLive();
        else RefreshReport();
    }

    void SetHeight(float h)
    {
        if (Mathf.Approximately(curH, h)) return;
        curH = h;
        canvasRt.sizeDelta = new Vector2(W, h);
        panel.rectTransform.sizeDelta = new Vector2(W, h);
    }

    void RefreshLive()
    {
        var pr = s.Prompts;

        // 标题
        if (s.Revealed)
        {
            header.color = pr.HasLastKind ? Palette.ForBlock(pr.Kind) : Palette.Gray;
            title.text = $"{s.revealTitle} · {s.StatusLabel}";
        }
        else
        {
            header.color = Palette.Gray;
            title.text = "TEAM OUTPUT";
        }

        // 配额（反转前后都显示）
        float got = s.TeamOutput - s.QuotaPrev;
        float need = s.QuotaTarget - s.QuotaPrev;
        quotaLabel.text = $"TEAM QUOTA  {got:0} / {need:0}    Level {s.QuotasMet + 1}    Rate {s.OutputRate:0.0}/s";
        quotaBar.Set(s.QuotaProgress01);
        quotaBar.SetColor(s.Revealed && s.Status == LeadStatus.Underperforming ? Palette.Warn : Palette.BarLight);

        // 只在反转后显示
        revealOnly.SetActive(s.Revealed);
        if (s.Revealed)
        {
            complianceLabel.text = $"COMPLIANCE  {s.Compliance01 * 100f:0}%";
            complianceBar.Set(s.Compliance01);

            string resp = s.HasResponse ? $"{s.AvgResponse:0.0}s" : "—";
            statsLabel.text = $"Actions {s.Actions}  (valid {s.ValidActions})     Response {resp}";
            receivedLabel.text =
                $"Received  <color=#D85A30>NOTIFY {s.ReceivedNotify}</color>   " +
                $"<color=#EF9F27>BONUS {s.ReceivedBonus}</color>   " +
                $"<color=#1D9E75>ASSIST {s.ReceivedAssist}</color>";
        }

        shiftLabel.text = $"Shift  {HUDFactory.ClockDown(s.ShiftRemaining)} left     Inactive  {s.InactiveTime:0}s";

    }

    void RefreshReport()
    {
        header.color = Palette.Gray;
        title.text = "SHIFT REPORT";

        string resp = s.HasResponse ? $"{s.AvgResponse:0.0}s" : "—";
        reportText.text =
            $"TEAM LEAD #{s.TeamLeadNumber:000}\n" +
            $"Status<pos=48%>{(s.Terminated ? "Terminated" : "Completed")}\n" +
            $"Team output<pos=48%>{s.TeamOutput:0}  (quotas met {s.QuotasMet})\n" +
            $"Response time<pos=48%>{resp}\n" +
            $"Compliance<pos=48%>{s.Compliance01 * 100f:0}%\n" +
            $"Actions<pos=48%>{s.Actions}  (valid {s.ValidActions})\n" +
            "Interventions received:";

        repCounts[0].text = $"×{s.ReceivedNotify}";
        repCounts[1].text = $"×{s.ReceivedBonus}";
        repCounts[2].text = $"×{s.ReceivedAssist}";

        reviewedText.text = $"Reviewed by: {s.reviewerTitle}";
        footerText.text = s.Phase == ShiftPhase.NextShift
            ? $"Your next shift begins in {HUDFactory.ClockDown(s.PhaseTimer)}"
            : "Shift complete.";
    }
}
