using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 头显里的调试面板（因为没有 Link 看不到 Unity 的 Console）。
/// 显示：帧率、班次状态、最近的日志（报错为红色、警告为黄色）。
/// 开关：头显里按下左摇杆；编辑器里按 F1。默认隐藏，展览时保持隐藏即可。
/// 面板跟随视线，浮在视野左下方。
/// 挂在任意物体上（比如 Session）。
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    [Tooltip("可选：显示班次状态")]
    public SessionManager session;

    [Header("显示")]
    public bool startVisible = false;
    [Tooltip("保留最近多少行日志")]
    public int maxLines = 10;

    [Header("位置（相对观众的头，单位米）")]
    public float distance = 0.4f;
    public float offsetRight = -0.2f;
    public float offsetUp = -0.12f;
    [Tooltip("面板宽度（米）")]
    public float width = 0.22f;
    [Tooltip("跟随视线的平滑速度")]
    public float follow = 5f;

    const float W = 520f, H = 520f;

    /// <summary>其他脚本可以在这里放一段固定显示的状态（不会被日志挤掉）</summary>
    public string Extra { get; set; }

    readonly Queue<string> lines = new Queue<string>();
    string lastWarning = "";
    GameObject root;
    CanvasGroup group;
    TextMeshProUGUI statusText, logText;
    bool visible;
    float fps = 60f;
    string lastError = "";

    void Awake()
    {
        Application.logMessageReceived += OnLog;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
    }

    void Start()
    {
        Build();
        SetVisible(startVisible);
        Debug.Log($"[Debug] Overlay ready · {Application.platform} · Unity {Application.unityVersion}");
    }

    // ================= 搭建 =================

    void Build()
    {
        root = new GameObject("DebugOverlay");
        var c = HUDFactory.CreateCanvas(root.transform, "Canvas", W, H);
        c.localScale = Vector3.one * (width / W);
        group = c.gameObject.AddComponent<CanvasGroup>();
        // 永远画在其他面板上面
        c.GetComponent<Canvas>().sortingOrder = 1000;

        HUDFactory.Box(c, "Panel", 0, 0, W, H, new Color(0f, 0f, 0f, 0.95f));
        HUDFactory.Box(c, "Header", 0, 0, W, 40, Palette.Gray);
        var title = HUDFactory.Label(c, "Title", 14, 0, W - 28, 40, 20, Color.white, bold: true);
        title.text = "DEBUG  (L-stick / F1 to hide)";

        statusText = HUDFactory.Label(c, "Status", 14, 46, W - 28, 190, 16, Palette.BarLight,
                                      TextAlignmentOptions.TopLeft, wrap: true);
        logText = HUDFactory.Label(c, "Log", 14, 240, W - 28, H - 250, 15, Color.white,
                                   TextAlignmentOptions.BottomLeft, wrap: true);
        logText.textWrappingMode = TextWrappingModes.NoWrap;
        // 每行超出面板的部分直接裁掉（不能用 Ellipsis：它会在第一条长日志处截断后面全部内容）
        logText.overflowMode = TextOverflowModes.Masking;
        logText.enableAutoSizing = false;
    }

    void SetVisible(bool on)
    {
        visible = on;
        if (root != null) root.SetActive(on);
        if (on) SnapToView();
    }

    // ================= 每帧 =================

    void Update()
    {
        // 帧率（平滑）
        float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        fps = Mathf.Lerp(fps, 1f / dt, 0.05f);

        if (TogglePressed()) SetVisible(!visible);
    }

    bool TogglePressed()
    {
        bool pressed = OVRInput.GetDown(OVRInput.RawButton.LThumbstick);
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) pressed = true;
#else
        if (Input.GetKeyDown(KeyCode.F1)) pressed = true;
#endif
        return pressed;
    }

    void LateUpdate()
    {
        if (!visible || root == null) return;

        // 跟随视线
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 target = TargetPosition(cam.transform);
            root.transform.position = Vector3.Lerp(root.transform.position, target, 1f - Mathf.Exp(-follow * Time.deltaTime));
            Vector3 dir = root.transform.position - cam.transform.position;
            if (dir.sqrMagnitude > 1e-6f) root.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        RefreshText();
    }

    Vector3 TargetPosition(Transform head)
    {
        return head.position + head.forward * distance + head.right * offsetRight + head.up * offsetUp;
    }

    void SnapToView()
    {
        var cam = Camera.main;
        if (cam != null && root != null) root.transform.position = TargetPosition(cam.transform);
    }

    void RefreshText()
    {
        Color fpsCol = fps >= 70f ? new Color(0.36f, 0.79f, 0.65f) : fps >= 55f ? Palette.Yellow : Palette.Warn;
        string s = $"FPS <color=#{ColorUtility.ToHtmlStringRGB(fpsCol)}>{fps:0}</color>";

        if (session != null)
        {
            s += $"    Phase {session.Phase}    Shift {HUDFactory.ClockUp(session.ShiftTime)}\n";
            s += $"Revealed {(session.Revealed ? "yes" : "no")}    Actions {session.Actions} (valid {session.ValidActions})    Status {session.StatusLabel}\n";
            s += $"Output {session.TeamOutput:0.0} / {session.QuotaTarget:0}    Compliance {session.Compliance01 * 100f:0}%";
        }

        if (!string.IsNullOrEmpty(Extra))
            s += "\n" + Extra;
        if (!string.IsNullOrEmpty(lastError))
            s += $"\n<color=#F09595>Last error: {lastError}</color>";
        if (!string.IsNullOrEmpty(lastWarning))
            s += $"\n<color=#FAC775>Last warning: {lastWarning}</color>";

        statusText.text = s;
        logText.text = string.Join("\n", lines);
    }

    // ================= 日志 =================

    void OnLog(string message, string stackTrace, LogType type)
    {
        string first = message.Split('\n')[0];
        if (first.Length > 70) first = first.Substring(0, 70);

        string line;
        switch (type)
        {
            case LogType.Error:
            case LogType.Exception:
            case LogType.Assert:
                line = $"<color=#F09595>{first}</color>";
                lastError = first;
                break;
            case LogType.Warning:
                line = $"<color=#FAC775>{first}</color>";
                if (!first.Contains("FoveatedRendering")) lastWarning = first;   // 忽略无关的注视点渲染提示
                break;
            default:
                line = first;
                break;
        }

        lines.Enqueue($"{Time.realtimeSinceStartup:0.0}  {line}");
        while (lines.Count > Mathf.Max(1, maxLines)) lines.Dequeue();
    }
}
