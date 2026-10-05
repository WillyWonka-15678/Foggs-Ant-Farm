using System.Collections;
using UnityEngine;

/// <summary>
/// 放置木块时的视觉反馈：
///   区域方块闪烁（有效 = 木块颜色闪两下，无效 = 灰色暗闪一下）+ 描边
///   图标从区域升起：有效 → 飞进小人面板被吸收；无效 → 变暗掉落淡出
///   反转后：悬浮提示消失时，它的图标飞进 YOU 面板（由 FloatingPrompts 调用）
/// 挂在任意空物体上（比如 Session），不影响其他脚本。
/// </summary>
public class FeedbackFX : MonoBehaviour
{
    [Header("引用")]
    public SessionManager session;
    [Tooltip("箱子左半边的区域方块（对应 Worker Left）")]
    public Transform zoneLeft;
    [Tooltip("箱子右半边的区域方块（对应 Worker Right）")]
    public Transform zoneRight;

    [Header("区域方块")]
    [Range(0f, 1f)] public float idleAlpha = 0.06f;
    [Range(0f, 1f)] public float flashAlpha = 0.45f;
    public float flashDuration = 0.7f;
    [Tooltip("有效时闪几下")]
    public int validPulses = 2;

    [Header("描边")]
    public bool outline = true;
    [Range(0f, 1f)] public float outlineIdleAlpha = 0.3f;
    [Tooltip("线宽（米），0 = 自动按区域大小")]
    public float outlineWidth = 0f;

    [Header("图标（大小和高度以小人身高为 1）")]
    public float iconSize = 0.35f;
    public float riseHeight = 0.6f;
    public float riseTime = 0.35f;
    public float flyTime = 0.5f;
    public float fallTime = 0.6f;
    [Tooltip("没有灰色图标时，无效图标变暗的程度")]
    [Range(0f, 1f)] public float invalidDim = 0.45f;

    // ================= 内部 =================
    class Zone
    {
        public Transform t;
        public Bounds localBounds;   // 网格的真实尺寸（兼容 ProBuilder）
        public Material mat;
        public LineRenderer[] lines;
        public Color flashColor;
        public float timer;      // 剩余闪烁时间
        public int pulses;
    }

    Zone left, right;
    Material lineMat;
    Sprite fallbackSprite;
    bool subscribed;

    static readonly Color IdleWhite = Color.white;

    void Start()
    {
        left  = SetupZone(zoneLeft);
        right = SetupZone(zoneRight);
        TrySubscribe();
    }

    void OnDestroy()
    {
        if (!subscribed || session == null) return;
        if (session.workerLeft != null)  session.workerLeft.BlockReceived -= OnBlock;
        if (session.workerRight != null) session.workerRight.BlockReceived -= OnBlock;
    }

    void TrySubscribe()
    {
        if (subscribed || session == null) return;
        if (session.workerLeft != null)  session.workerLeft.BlockReceived += OnBlock;
        if (session.workerRight != null) session.workerRight.BlockReceived += OnBlock;
        subscribed = true;
    }

    // ================= 区域方块 =================

    Zone SetupZone(Transform t)
    {
        if (t == null) return null;

        var z = new Zone { t = t };
        var mf = t.GetComponent<MeshFilter>();
        z.localBounds = (mf != null && mf.sharedMesh != null)
            ? mf.sharedMesh.bounds
            : new Bounds(Vector3.zero, Vector3.one);
        var shader = Shader.Find("Sprites/Default");   // 半透明、无光照、URP 可用

        var r = t.GetComponent<Renderer>();
        if (r != null)
        {
            z.mat = new Material(shader);
            z.mat.color = WithAlpha(IdleWhite, idleAlpha);
            r.sharedMaterial = z.mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        if (outline)
        {
            if (lineMat == null) lineMat = new Material(shader);
            z.lines = new LineRenderer[6];   // 底面一圈、顶面一圈、四条竖边
            for (int i = 0; i < 6; i++)
            {
                var go = new GameObject($"Outline{i}");
                go.transform.SetParent(t, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = lineMat;
                lr.useWorldSpace = true;
                lr.loop = i < 2;
                lr.positionCount = i < 2 ? 4 : 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                z.lines[i] = lr;
            }
        }
        return z;
    }

    void LateUpdate()
    {
        UpdateZone(left);
        UpdateZone(right);
    }

    void UpdateZone(Zone z)
    {
        if (z == null) return;

        float intensity = 0f;
        Color target = IdleWhite;
        if (z.timer > 0f)
        {
            z.timer -= Time.deltaTime;
            float k = 1f - Mathf.Clamp01(z.timer / flashDuration);           // 0 → 1
            float wave = 0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * z.pulses * k); // 从亮开始
            intensity = (1f - k) * wave;
            target = z.flashColor;
        }

        if (z.mat != null)
        {
            Color idle = WithAlpha(IdleWhite, idleAlpha);
            Color peak = WithAlpha(target, flashAlpha);
            z.mat.color = Color.Lerp(idle, peak, intensity);
        }

        if (z.lines != null)
        {
            Color idleL = WithAlpha(IdleWhite, outlineIdleAlpha);
            Color peakL = WithAlpha(target, 1f);
            Color c = Color.Lerp(idleL, peakL, intensity);
            float width = outlineWidth > 0f ? outlineWidth : AutoWidth(z);

            var p = Corners(z);
            SetLine(z.lines[0], c, width, p[0], p[1], p[2], p[3]);
            SetLine(z.lines[1], c, width, p[4], p[5], p[6], p[7]);
            for (int i = 0; i < 4; i++) SetLine(z.lines[2 + i], c, width, p[i], p[i + 4]);
        }
    }

    static Vector3[] Corners(Zone z)
    {
        Vector3 a = z.localBounds.min, b = z.localBounds.max;
        Vector3[] local =
        {
            new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z),
            new Vector3(b.x, a.y, b.z), new Vector3(a.x, a.y, b.z),
            new Vector3(a.x, b.y, a.z), new Vector3(b.x, b.y, a.z),
            new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z),
        };
        var w = new Vector3[8];
        for (int i = 0; i < 8; i++) w[i] = z.t.TransformPoint(local[i]);
        return w;
    }

    static float AutoWidth(Zone z)
    {
        Vector3 size = Vector3.Scale(z.localBounds.size, z.t.lossyScale);
        float m = Mathf.Min(Mathf.Abs(size.x), Mathf.Min(Mathf.Abs(size.y), Mathf.Abs(size.z)));
        return Mathf.Max(0.0005f, m * 0.015f);
    }

    static void SetLine(LineRenderer lr, Color c, float width, params Vector3[] pts)
    {
        lr.startColor = lr.endColor = c;
        lr.startWidth = lr.endWidth = width;
        for (int i = 0; i < pts.Length; i++) lr.SetPosition(i, pts[i]);
    }

    void Flash(Zone z, Color c, int pulses)
    {
        if (z == null) return;
        z.flashColor = c;
        z.pulses = Mathf.Max(1, pulses);
        z.timer = flashDuration;
    }

    // ================= 事件 =================

    void OnBlock(WorkerController w, BlockType block, bool valid)
    {
        bool isLeft = session != null && w == session.workerLeft;
        Zone z = isLeft ? left : right;
        WorkerHUD hud = session != null ? (isLeft ? session.LeftHUD : session.RightHUD) : null;

        Color blockColor = Palette.ForBlock(block);
        Flash(z, valid ? blockColor : Palette.Gray, valid ? validPulses : 1);

        float unit = hud != null ? hud.WorkerHeight : 1f;
        Vector3 start = z != null ? z.t.TransformPoint(z.localBounds.center) : w.transform.position;
        var icon = valid ? SpawnIcon(block, unit, 1f) : SpawnInvalidIcon(block, unit);

        if (valid) StartCoroutine(FlyToPanel(icon, start, unit, () => hud != null ? hud.PanelCenter : start + Vector3.up * unit * 2f));
        else       StartCoroutine(RiseAndFall(icon, start, unit));
    }

    /// <summary>
    /// 反转后由悬浮提示调用：提示消失时，它的图标从提示的位置飞进 YOU 面板。
    /// </summary>
    public void SendToYou(BlockType kind, Vector3 from)
    {
        if (session == null || !session.Revealed || session.Phase != ShiftPhase.Running) return;
        var you = session.YouPanel;
        if (you == null) return;

        float unit = you.BaseHeight;
        var icon = SpawnIcon(kind, unit, 1f);
        StartCoroutine(FlyToPanel(icon, from, unit, () => you.PanelCenter, 0.25f));
    }

    // ================= 图标 =================

    SpriteRenderer SpawnIcon(BlockType block, float unit, float brightness)
    {
        var go = new GameObject($"FX_{block}");
        var sr = go.AddComponent<SpriteRenderer>();

        Sprite sp = session != null ? session.BlockIcon(block) : null;
        Color tint = Color.white;
        if (sp == null)
        {
            sp = FallbackSprite();
            tint = Palette.ForBlock(block);   // 没有图标时用木块颜色的方块
        }
        sr.sprite = sp;
        sr.color = new Color(tint.r * brightness, tint.g * brightness, tint.b * brightness, 1f);

        float h = sp.bounds.size.y > 0f ? sp.bounds.size.y : 1f;
        go.transform.localScale = Vector3.one * (unit * iconSize / h);
        return sr;
    }

    /// <summary>失败：优先用灰色图标；没有时退回变暗的彩色图标</summary>
    SpriteRenderer SpawnInvalidIcon(BlockType block, float unit)
    {
        Sprite gray = session != null ? session.InvalidIcon(block) : null;
        if (gray == null) return SpawnIcon(block, unit, invalidDim);

        var go = new GameObject($"FX_{block}_invalid");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = gray;
        sr.color = Color.white;
        float h = gray.bounds.size.y > 0f ? gray.bounds.size.y : 1f;
        go.transform.localScale = Vector3.one * (unit * iconSize / h);
        return sr;
    }

    Sprite FallbackSprite()
    {
        if (fallbackSprite == null)
        {
            var tex = Texture2D.whiteTexture;
            fallbackSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        }
        return fallbackSprite;
    }

    /// <summary>有效：升起 → 飞向面板 → 缩小淡出（被吸收）</summary>
    IEnumerator FlyToPanel(SpriteRenderer sr, Vector3 start, float unit, System.Func<Vector3> target, float riseScale = 1f)
    {
        Transform t = sr.transform;
        Vector3 baseScale = t.localScale;
        Color c0 = sr.color;
        Vector3 top = start + Vector3.up * unit * riseHeight * riseScale;

        for (float e = 0f; e < riseTime; e += Time.deltaTime)
        {
            float k = EaseOut(e / riseTime);
            t.position = Vector3.Lerp(start, top, k);
            sr.color = WithAlpha(c0, k);
            HUDFactory.FaceCamera(t);
            yield return null;
        }

        for (float e = 0f; e < flyTime; e += Time.deltaTime)
        {
            float k = EaseInOut(e / flyTime);
            t.position = Vector3.Lerp(top, target(), k);
            t.localScale = baseScale * Mathf.Lerp(1f, 0.35f, k);
            sr.color = WithAlpha(c0, Mathf.Lerp(1f, 0f, Mathf.Clamp01((k - 0.6f) / 0.4f)));
            HUDFactory.FaceCamera(t);
            yield return null;
        }

        Destroy(t.gameObject);
    }

    /// <summary>无效：升起一小段 → 停顿 → 掉落淡出</summary>
    IEnumerator RiseAndFall(SpriteRenderer sr, Vector3 start, float unit)
    {
        Transform t = sr.transform;
        Color c0 = sr.color;
        Vector3 top = start + Vector3.up * unit * riseHeight * 0.5f;

        for (float e = 0f; e < riseTime; e += Time.deltaTime)
        {
            float k = EaseOut(e / riseTime);
            t.position = Vector3.Lerp(start, top, k);
            sr.color = WithAlpha(c0, k);
            HUDFactory.FaceCamera(t);
            yield return null;
        }

        yield return new WaitForSeconds(0.15f);

        Vector3 bottom = start - Vector3.up * unit * 0.2f;
        for (float e = 0f; e < fallTime; e += Time.deltaTime)
        {
            float k = e / fallTime;
            t.position = Vector3.Lerp(top, bottom, k * k);   // 加速下落
            sr.color = WithAlpha(c0, 1f - k);
            HUDFactory.FaceCamera(t);
            yield return null;
        }

        Destroy(t.gameObject);
    }

    // ================= 工具 =================

    static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }
    static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
