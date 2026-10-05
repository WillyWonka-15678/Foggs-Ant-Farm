using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 中间的面板。反转前是 TEAM OUTPUT，反转后变成 TEAM LEAD #，结束时变成报告。
/// 反转后标题颜色 = 系统最近一次对观众用了哪块木块。
/// 由 SessionManager 自动生成，不需要手动挂。
/// </summary>
public class YouHUD : MonoBehaviour
{
    const float W = 460f;
    const float H_LIVE = 256f, H_REPORT = 360f;
    float curH = H_REPORT;   // 与创建时的画布高度一致

    SessionManager s;
    WorkerHUD left, right;
    float baseHeight = 1f;

    public float BaseHeight => baseHeight;

    /// <summary>YOU 面板下方需要预留的空间（米），由 FloatingPrompts 设置，用来放观众的提示</summary>
    public float ReservedBelow { get; set; }
    bool placed;
    float currentY;
    float scaleMul = 1f;
    float baseScale;
    public float WorldWidth => baseHeight * s.youHudWidth * scaleMul;
    public float WorldPanelHeight => WorldWidth * curH / W;
    public Vector3 PanelCenter => transform.position + transform.up * (WorldPanelHeight * 0.5f);

    RectTransform canvasRt;
    Image panel, header;
    TextMeshProUGUI title;

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

        panel = HUDFactory.Box(c, "Panel", 0, 0, W, H_REPORT, Palette.Panel);
        header = HUDFactory.Box(c, "Header", 0, 0, W, 52, Palette.Gray);
        title = HUDFactory.Label(c, "Title", 18, 0, W - 36, 52, 25, Color.white, bold: true);

        // ---------- 进行中 ----------
        var liveRt = HUDFactory.Rect(c, "Live", 0, 0, W, H_REPORT);
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
        var repRt = HUDFactory.Rect(c, "Report", 0, 0, W, H_REPORT);
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

        // 小人面板的顶边
        float top = float.MinValue;
        if (left != null)  top = Mathf.Max(top, left.transform.position.y  + left.WorldPanelHeight);
        if (right != null) top = Mathf.Max(top, right.transform.position.y + right.WorldPanelHeight);

        // 放大：报告期间平滑放大
        bool running = s.Phase == ShiftPhase.Running;
        float targetScale = running ? 1f : Mathf.Max(1f, s.reportScale);
        scaleMul = Mathf.Lerp(scaleMul, targetScale, 1f - Mathf.Exp(-6f * Time.deltaTime));
        canvasRt.localScale = Vector3.one * (baseScale * scaleMul);

        float targetY;
        if (running)
        {
            // YOU 放在最上面：小人面板顶边 + 留白 + 提示预留空间（提示越叠越多，YOU 被慢慢顶高）
            targetY = top + baseHeight * s.youHudLift + ReservedBelow;
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
        transform.position = p;
    }

    void Refresh()
    {
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
