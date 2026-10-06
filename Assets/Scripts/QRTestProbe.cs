using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Meta.XR.MRUtilityKit;

/// <summary>
/// QR 码识别测试工具（只放在 MR_Test 场景，不接游戏）。
///
/// 每个识别到的码：画一个方框，上方显示
///   内容 · 是否在追踪 · 尺寸 · 抖动（毫米）· 每秒更新次数
///
/// 计时：
///   放下码的瞬间按 A → 识别到时显示"识别用时"
///   拿起码的瞬间按 B → 判定消失时显示"消失用时"
///
/// 所有结果写入日志，配合 DebugOverlay 在头显里查看。
/// 场景要求：MRUK（勾选 QR Code Tracking Enabled）、Camera Rig、Scene 权限。
/// </summary>
public class QRTestProbe : MonoBehaviour
{
    [Header("显示")]
    public float frameWidth = 0.002f;
    [Tooltip("标签宽度（米）")]
    public float labelWidth = 0.12f;
    [Tooltip("抖动统计用最近多少帧")]
    public int jitterSamples = 60;

    class Code
    {
        public MRUKTrackable t;
        public string payload;
        public LineRenderer frame;
        public Transform label;
        public TextMeshProUGUI text;
        public readonly Queue<Vector3> samples = new Queue<Vector3>();
        public Vector3 lastPos;
        public int moves;
        public float rateTimer, rate;
        public bool wasTracked;
        public float addedAt;
    }

    readonly Dictionary<MRUKTrackable, Code> codes = new Dictionary<MRUKTrackable, Code>();

    DebugOverlay overlay;
    bool permission;
    int addedTotal;
    string lastEvent = "—";

    float armDetectAt = -1f;   // 按 A 的时间
    float armRemoveAt = -1f;   // 按 B 的时间

    // ================= 启动 =================

    IEnumerator Start()
    {
        // 等 MRUK 准备好
        while (MRUK.Instance == null) yield return null;

        MRUK.Instance.SceneSettings.TrackableAdded.AddListener(OnAdded);
        MRUK.Instance.SceneSettings.TrackableRemoved.AddListener(OnRemoved);

        overlay = FindFirstObjectByType<DebugOverlay>();
        bool scene = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene);
        permission = scene;
        Debug.Log($"[QR] Probe ready. Scene permission: {(scene ? "granted" : "NOT granted")}");
        if (!scene) Debug.LogWarning("[QR] 没有空间数据权限，QR 识别不会工作。检查 OVR Manager 的 Permission Requests On Startup → Scene");
    }

    void OnDestroy()
    {
        if (MRUK.Instance == null) return;
        MRUK.Instance.SceneSettings.TrackableAdded.RemoveListener(OnAdded);
        MRUK.Instance.SceneSettings.TrackableRemoved.RemoveListener(OnRemoved);
    }

    // ================= 事件 =================

    void OnAdded(MRUKTrackable t)
    {
        if (t.TrackableType != OVRAnchor.TrackableType.QRCode) return;

        var c = new Code
        {
            t = t,
            payload = t.MarkerPayloadString ?? "(binary)",
            addedAt = Time.realtimeSinceStartup,
            wasTracked = t.IsTracked,
            lastPos = t.transform.position
        };
        c.frame = BuildFrame(t);
        BuildLabel(c);
        codes[t] = c;

        Debug.Log($"[QR] + ADDED  \"{c.payload}\"  size {SizeMM(t)}");
        addedTotal++;
        lastEvent = $"+ {c.payload}";
        StopDetectTimer(c.payload, "added");
    }

    void OnRemoved(MRUKTrackable t)
    {
        if (!codes.TryGetValue(t, out var c)) return;
        Debug.Log($"[QR] − REMOVED \"{c.payload}\"  (lived {Time.realtimeSinceStartup - c.addedAt:0.0}s)");
        StopRemoveTimer(c.payload, "removed");

        if (c.label != null) Destroy(c.label.gameObject);
        codes.Remove(t);
    }

    // ================= 每帧 =================

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.RawButton.A))
        {
            armDetectAt = Time.realtimeSinceStartup;
            Debug.Log("[QR] Timer: waiting for detection…");
        }
        if (OVRInput.GetDown(OVRInput.RawButton.B))
        {
            armRemoveAt = Time.realtimeSinceStartup;
            Debug.Log("[QR] Timer: waiting for loss…");
        }

        foreach (var c in codes.Values) UpdateCode(c);

        // 固定显示在调试面板顶部
        if (overlay != null)
        {
            permission = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene);
            bool supported = OVRAnchor.TrackerConfiguration.QRCodeTrackingSupported;
            bool requested = MRUK.Instance != null && MRUK.Instance.SceneSettings.TrackerConfiguration.QRCodeTrackingEnabled;
            int tracked = 0;
            foreach (var c in codes.Values) if (c.t != null && c.t.IsTracked) tracked++;

            overlay.Extra =
                "<b>QR</b>  " +
                $"permission {Flag(permission)}   supported {Flag(supported)}   enabled in MRUK {Flag(requested)}\n" +
                $"codes now {codes.Count} (tracked {tracked})   seen total {addedTotal}   last {lastEvent}";
        }
    }

    static string Flag(bool b) => b ? "<color=#5DCAA5>YES</color>" : "<color=#F09595>NO</color>";

    void UpdateCode(Code c)
    {
        if (c.t == null) return;

        // 追踪状态变化（拿起 / 放回时可能只是状态切换，而不是删除再新增）
        bool tracked = c.t.IsTracked;
        if (tracked != c.wasTracked)
        {
            Debug.Log($"[QR] \"{c.payload}\" {(tracked ? "TRACKED again" : "LOST tracking")}");
            if (tracked) StopDetectTimer(c.payload, "re-tracked");
            else         StopRemoveTimer(c.payload, "lost");
            c.wasTracked = tracked;
        }

        // 更新频率：位置每秒变化几次
        Vector3 p = c.t.transform.position;
        if ((p - c.lastPos).sqrMagnitude > 1e-8f) { c.moves++; c.lastPos = p; }
        c.rateTimer += Time.deltaTime;
        if (c.rateTimer >= 1f) { c.rate = c.moves / c.rateTimer; c.moves = 0; c.rateTimer = 0f; }

        // 抖动：最近 N 帧位置的均方根偏差
        c.samples.Enqueue(p);
        while (c.samples.Count > Mathf.Max(2, jitterSamples)) c.samples.Dequeue();
        float jitter = Jitter(c.samples);

        // 方框颜色
        Color col = tracked ? Palette.Assist : Palette.Gray;
        c.frame.startColor = c.frame.endColor = col;

        // 标签
        c.label.position = p + Vector3.up * 0.03f;
        HUDFactory.FaceCamera(c.label);
        c.text.text =
            $"<b>{c.payload}</b>  {(tracked ? "<color=#5DCAA5>● tracked</color>" : "<color=#888780>○ lost</color>")}\n" +
            $"size {SizeMM(c.t)}   jitter {jitter:0.0} mm   {c.rate:0.0} upd/s";
    }

    // ================= 计时 =================

    void StopDetectTimer(string payload, string how)
    {
        if (armDetectAt < 0f) return;
        float dt = Time.realtimeSinceStartup - armDetectAt;
        Debug.Log($"[QR] ⏱ DETECT \"{payload}\" ({how}) after <color=#FAC775>{dt:0.00}s</color>");
        armDetectAt = -1f;
    }

    void StopRemoveTimer(string payload, string how)
    {
        if (armRemoveAt < 0f) return;
        float dt = Time.realtimeSinceStartup - armRemoveAt;
        Debug.Log($"[QR] ⏱ LOSS \"{payload}\" ({how}) after <color=#FAC775>{dt:0.00}s</color>");
        armRemoveAt = -1f;
    }

    // ================= 工具 =================

    static float Jitter(Queue<Vector3> q)
    {
        if (q.Count < 2) return 0f;
        Vector3 mean = Vector3.zero;
        foreach (var v in q) mean += v;
        mean /= q.Count;
        float sum = 0f;
        foreach (var v in q) sum += (v - mean).sqrMagnitude;
        return Mathf.Sqrt(sum / q.Count) * 1000f;   // 毫米
    }

    static string SizeMM(MRUKTrackable t)
    {
        if (t.PlaneRect.HasValue)
        {
            var r = t.PlaneRect.Value;
            return $"{r.width * 1000f:0}×{r.height * 1000f:0} mm";
        }
        return "?";
    }

    LineRenderer BuildFrame(MRUKTrackable t)
    {
        var go = new GameObject("QRFrame");
        go.transform.SetParent(t.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.startWidth = lr.endWidth = frameWidth;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Rect r = t.PlaneRect ?? new Rect(-0.015f, -0.015f, 0.03f, 0.03f);
        lr.positionCount = 4;
        lr.SetPosition(0, new Vector3(r.xMin, r.yMin, 0f));
        lr.SetPosition(1, new Vector3(r.xMax, r.yMin, 0f));
        lr.SetPosition(2, new Vector3(r.xMax, r.yMax, 0f));
        lr.SetPosition(3, new Vector3(r.xMin, r.yMax, 0f));
        return lr;
    }

    void BuildLabel(Code c)
    {
        const float W = 520f, H = 90f;
        var go = new GameObject($"QRLabel_{c.payload}");
        var canvas = HUDFactory.CreateCanvas(go.transform, "Canvas", W, H);
        canvas.localScale = Vector3.one * (labelWidth / W);
        canvas.GetComponent<Canvas>().sortingOrder = 800;

        HUDFactory.Box(canvas, "Panel", 0, 0, W, H, new Color(0f, 0f, 0f, 0.85f));
        c.text = HUDFactory.Label(canvas, "Text", 12, 4, W - 24, H - 8, 22, Color.white,
                                  TextAlignmentOptions.MidlineLeft, wrap: true);
        c.label = go.transform;
    }
}
