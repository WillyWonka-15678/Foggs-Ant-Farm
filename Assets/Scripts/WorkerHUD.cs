using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 小人头顶的仪表盘。由 SessionManager 自动生成，不需要手动挂。
/// 标题颜色 = 小人现在需要的木块颜色。
/// </summary>
public class WorkerHUD : MonoBehaviour
{
    const float W = 380f, H = 228f;

    [Tooltip("闲置读秒条走满需要的秒数（只是视觉，满了也不会发生什么）")]
    public float idleBarSeconds = 10f;

    public float WorkerHeight { get; private set; }
    public float WorldWidth { get; private set; }
    public float WorldPanelHeight => WorldWidth * H / W;
    public Vector3 PanelCenter => transform.position + transform.up * (WorldPanelHeight * 0.5f);

    WorkerController w;
    SessionManager s;
    float headOffset;

    CanvasGroup group;
    Image header, headerIcon;
    TextMeshProUGUI title, fatigueLabel, row2Label, outputLabel, bonusLabel;
    UIBar fatigueBar, row2Bar;

    public static WorkerHUD Create(WorkerController worker, SessionManager session)
    {
        var go = new GameObject($"HUD_{worker.workerName}");
        var hud = go.AddComponent<WorkerHUD>();
        hud.Init(worker, session);
        return hud;
    }

    void Init(WorkerController worker, SessionManager session)
    {
        w = worker;
        s = session;

        // 以小人身高为单位决定面板大小和高度
        WorkerHeight = MeasureHeight(w);
        headOffset = WorkerHeight * (1f + s.workerHudGap);
        WorldWidth = WorkerHeight * s.workerHudWidth;

        var c = HUDFactory.CreateCanvas(transform, "Canvas", W, H);
        c.localScale = Vector3.one * (WorldWidth / W);
        group = c.gameObject.AddComponent<CanvasGroup>();

        HUDFactory.Box(c, "Panel", 0, 0, W, H, Palette.Panel);
        header = HUDFactory.Box(c, "Header", 0, 0, W, 48, Palette.Bonus);
        title = HUDFactory.Label(c, "Title", 16, 0, 300, 48, 24, Color.white, bold: true);
        headerIcon = HUDFactory.Icon(c, "HeaderIcon", W - 44, 3, 33, 42);

        fatigueLabel = HUDFactory.Label(c, "FatigueLabel", 16, 54, 348, 26, 19, Palette.Dim);
        fatigueBar   = HUDFactory.Bar(c, "FatigueBar", 16, 82, 348, 12, Palette.BarLight);

        row2Label = HUDFactory.Label(c, "Row2Label", 16, 100, 348, 26, 19, Palette.Dim);
        row2Bar   = HUDFactory.Bar(c, "Row2Bar", 16, 128, 348, 12, Palette.Bonus);

        outputLabel = HUDFactory.Label(c, "Output", 16, 150, 348, 30, 20, Palette.BarLight);
        bonusLabel  = HUDFactory.Label(c, "Bonus", 16, 184, 348, 30, 20, Palette.Dim);
    }

    static float MeasureHeight(WorkerController w)
    {
        Renderer[] rends = w.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (rends.Length == 0) rends = w.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 1f;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float h = b.max.y - w.transform.position.y;
        return h > 0.001f ? h : 1f;
    }

    void LateUpdate()
    {
        if (w == null) return;
        transform.position = w.transform.position + Vector3.up * headOffset;
        HUDFactory.FaceCamera(transform);

        // 报告期间隐藏小人面板，把视野让给报告
        float target = s.Phase == ShiftPhase.Running ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, target, Time.deltaTime * 3f);
        if (group.alpha <= 0f) return;

        Refresh();
    }

    void Refresh()
    {
        var st = w.State;
        var need = Palette.NeededBlock(st);

        // 标题：颜色 = 需要的木块
        header.color = Palette.ForBlock(need);
        title.text = $"{w.workerName.ToUpper()} · {st.ToString().ToUpper()}";
        var icon = s.BlockIcon(need);
        headerIcon.sprite = icon;
        headerIcon.enabled = icon != null;

        // 第一根条：疲劳
        float f = w.Fatigue01;
        fatigueLabel.text = $"FATIGUE  {w.Fatigue:0} / {w.maxFatigue:0}";
        fatigueBar.Set(f);
        if (f >= 0.9f)      fatigueBar.SetColor(Color.Lerp(Palette.Warn, Color.white, Mathf.PingPong(Time.time * 4f, 1f)));
        else if (f >= 0.7f) fatigueBar.SetColor(Palette.Warn);
        else                fatigueBar.SetColor(Palette.BarLight);

        // 第二根条：随状态改变含义
        switch (st)
        {
            case WorkerState.Working:
                row2Label.text = $"TASK  {w.TaskProgress01 * 100f:0}%   {w.TaskRemaining:0.0}s left";
                row2Bar.Set(w.TaskProgress01);
                row2Bar.SetColor(Palette.Bonus);
                break;

            case WorkerState.Idle:
                float iv = Mathf.Clamp01(w.IdleTime / Mathf.Max(0.01f, idleBarSeconds));
                row2Label.text = $"IDLE  {HUDFactory.ClockUp(w.IdleTime)}";
                row2Bar.Set(iv);
                row2Bar.SetColor(Color.Lerp(Palette.IdleLight, Palette.IdleDeep, iv));
                break;

            case WorkerState.Exhausted:
                float rec = 1f - w.RecoveryRemaining / Mathf.Max(0.01f, w.exhaustedDuration);
                row2Label.text = $"RECOVERY  {w.RecoveryRemaining:0.0}s";
                row2Bar.Set(rec);
                row2Bar.SetColor(Palette.Assist);
                break;
        }

        // 数字
        outputLabel.text = $"Output  {w.Output:0.0}     Speed  x{w.CurrentSpeed:0.00}";

        switch (w.Boost)
        {
            case BoostPhase.Boost:
                bonusLabel.text = $"BONUS  BOOST  {w.BoostRemaining:0.0}s";
                bonusLabel.color = Palette.Yellow;
                break;
            case BoostPhase.Slump:
                bonusLabel.text = $"BONUS  SLUMP  {w.BoostRemaining:0.0}s";
                bonusLabel.color = Palette.Warn;
                break;
            default:
                bonusLabel.text = "BONUS  —";
                bonusLabel.color = Palette.Dim;
                break;
        }
    }
}
