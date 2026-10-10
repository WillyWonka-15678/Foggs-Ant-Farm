using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// 可以用手抓取的虚拟木块。
///   松手时如果落在箱子左半边 / 右半边 → 对那边的小人放置一次
///   松手后手里那块缩小消失，原位"长出"一块新的
///   （实际上是同一块瞬移回原位再放大；消失的是一个只有外观的残影，
///     这样抓取组件不用重新生成，不会出现抓不起来的问题）
///
/// 和键盘、手柄一样只负责"哪一边放了哪块"，其他脚本不用改。
/// 挂在虚拟木块上（木块需要先用 Building Blocks 加上抓取功能）。
/// </summary>
public class VirtualBlock : MonoBehaviour
{
    [Header("木块")]
    public BlockType type = BlockType.Notify;

    [Header("引用（不填会自动从场景里找）")]
    public SessionManager session;
    public Transform zoneLeft;
    public Transform zoneRight;

    [Header("判定")]
    [Tooltip("区域上方多高以内松手也算放进去（米）")]
    public float aboveZone = 0.12f;

    [Header("图标（自动贴在四个侧面和顶面）")]
    public bool showIcons = true;
    public bool iconOnTop = true;
    [Tooltip("图标占面的比例，留出一圈木块底色作为边框")]
    [Range(0.5f, 1f)] public float iconFill = 0.88f;

    [Header("消失 / 重新出现")]
    [Tooltip("松手处的残影缩小消失用多少秒")]
    public float vanishTime = 0.2f;
    [Tooltip("原位的新木块放大出现用多少秒")]
    public float spawnTime = 0.25f;

    // 对外事件（SoundFX 用）：所有木块共用，不需要逐个连线
    public static event System.Action<VirtualBlock> Grabbed;
    /// <summary>松手的那一刻（残影开始消失）</summary>
    public static event System.Action<VirtualBlock> Dropped;
    /// <summary>新木块在原位开始长出来</summary>
    public static event System.Action<VirtualBlock> Respawned;

    // ---------- 所有木块共用的状态（BoxCalibrator / ControllerInput 用） ----------
    static readonly List<VirtualBlock> all = new List<VirtualBlock>();
    static int heldCount;

    /// <summary>现在是否有木块正被手 / 手柄抓着</summary>
    public static bool AnyHeld => heldCount > 0;

    /// <summary>标定期间为 true：木块看得见但拿不起来，松手也不算放置</summary>
    public static bool Locked { get; private set; }

    /// <summary>锁定 / 解锁全部木块的抓取（标定开始和结束时由 BoxCalibrator 调用）</summary>
    public static void SetLocked(bool locked)
    {
        Locked = locked;
        foreach (var b in all) if (b != null) b.ApplyLock();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        all.Clear();
        heldCount = 0;
        Locked = false;
    }

    IPointable pointable;
    Rigidbody rb;
    Transform homeParent;
    Vector3 homeLocalPos;
    Quaternion homeLocalRot;
    Vector3 homeLocalScale;
    Transform iconRoot;
    Coroutine returning;
    int holders;   // 当前有几只手 / 手柄抓着它

    void Start()
    {
        if (session == null) session = FindFirstObjectByType<SessionManager>();
        if (zoneLeft == null || zoneRight == null)
        {
            var fx = FindFirstObjectByType<FeedbackFX>();
            if (fx != null)
            {
                if (zoneLeft == null) zoneLeft = fx.zoneLeft;
                if (zoneRight == null) zoneRight = fx.zoneRight;
            }
        }

        // 记住原位（相对父物体，标定移动场景后原位也跟着走）
        homeParent = transform.parent;
        homeLocalPos = transform.localPosition;
        homeLocalRot = transform.localRotation;
        homeLocalScale = transform.localScale;

        // 不受重力，松手后停在空中，由脚本送回原位（现实里没有桌子的碰撞体，掉下去会一直往下落）
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        if (showIcons) BuildIcons();

        pointable = GetComponentInChildren<IPointable>();
        if (pointable != null) pointable.WhenPointerEventRaised += OnPointer;
        else Debug.LogWarning($"[VirtualBlock] {name}: 没有找到抓取组件，请先用 Building Blocks 给它加上抓取功能");
    }

    void OnEnable()
    {
        all.Add(this);
        if (Locked) ApplyLock();
    }

    void OnDisable()
    {
        all.Remove(this);
        if (holders > 0) { holders = 0; heldCount = Mathf.Max(0, heldCount - 1); }
    }

    void OnDestroy()
    {
        if (pointable != null) pointable.WhenPointerEventRaised -= OnPointer;
    }

    /// <summary>开关这块木块上的"可抓取"组件（Interaction SDK 的各种 Interactable）。木块本身照常显示</summary>
    void ApplyLock()
    {
        if (Locked)
        {
            // 只关掉当前开着的，解锁时原样恢复（本来就关着的不会被误开）
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is IInteractable && mb.enabled) { mb.enabled = false; lockedOff.Add(mb); }
            }
        }
        else
        {
            foreach (var mb in lockedOff) if (mb != null) mb.enabled = true;
            lockedOff.Clear();
        }
    }

    readonly List<MonoBehaviour> lockedOff = new List<MonoBehaviour>();

    // ================= 抓取事件 =================

    void OnPointer(PointerEvent evt)
    {
        switch (evt.Type)
        {
            case PointerEventType.Select:
                holders++;
                if (holders == 1) { heldCount++; Grabbed?.Invoke(this); }
                // 新木块还在放大时就被抓起：立刻恢复正常大小
                if (returning != null) { StopCoroutine(returning); returning = null; }
                transform.localScale = homeLocalScale;
                break;

            case PointerEventType.Unselect:
            case PointerEventType.Cancel:
                // 只有真的被抓着的木块松开才算（没抓起就收到取消事件时不处理）
                if (holders == 0) break;
                holders--;
                if (holders == 0)
                {
                    heldCount = Mathf.Max(0, heldCount - 1);
                    Released();
                }
                break;
        }
    }

    void Released()
    {
        WorkerController target = null;
        string side = "outside";

        if (Inside(zoneLeft))       { target = session != null ? session.workerLeft  : null; side = "LEFT"; }
        else if (Inside(zoneRight)) { target = session != null ? session.workerRight : null; side = "RIGHT"; }

        if (Locked)
        {
            // 标定期间：不算放置，也不会开始一班
            Debug.Log($"[Hand] {type} released during calibration (ignored)");
        }
        else if (target != null && session != null && session.ConsumeStartPlacement(target))
        {
            // 等待阶段：这一块只用来开始这一班，不交给小人
            Debug.Log($"[Hand] {type} → {side}: shift begins");
        }
        else if (target != null)
        {
            WorkerState before = target.State;
            bool valid = target.ReceiveBlock(type);
            string result = valid ? "<color=#5DCAA5>VALID</color>" : "<color=#888780>No effect</color>";
            Debug.Log($"[Hand] {target.workerName} ← {type} ({before}) {result}");
        }
        else
        {
            Debug.Log($"[Hand] {type} released {side}");
        }

        Dropped?.Invoke(this);
        SpawnGhost();
        returning = StartCoroutine(Respawn());
    }

    /// <summary>木块中心是否在区域的水平范围内，且高度在区域底部到"顶部 + aboveZone"之间</summary>
    bool Inside(Transform zone)
    {
        if (zone == null) return false;
        var mf = zone.GetComponent<MeshFilter>();
        Bounds b = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);

        Vector3 p = zone.InverseTransformPoint(transform.position);
        float extraY = aboveZone / Mathf.Max(0.0001f, Mathf.Abs(zone.lossyScale.y));

        return p.x >= b.min.x && p.x <= b.max.x
            && p.z >= b.min.z && p.z <= b.max.z
            && p.y >= b.min.y && p.y <= b.max.y + extraY;
    }

    // ================= 图标 =================

    void BuildIcons()
    {
        Sprite icon = session != null ? session.BlockIcon(type) : null;
        if (icon == null)
        {
            Debug.LogWarning($"[VirtualBlock] {name}: SessionManager 里没有 {type} 的彩色图标，跳过贴图");
            return;
        }

        // 木块的真实尺寸（米）
        var mf = GetComponent<MeshFilter>();
        Vector3 ext = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds.extents : Vector3.one * 0.5f;
        Vector3 ls = transform.lossyScale;
        float hx = Mathf.Abs(ext.x * ls.x), hy = Mathf.Abs(ext.y * ls.y), hz = Mathf.Abs(ext.z * ls.z);

        // 一个"抵消缩放"的容器：木块是 4×5×4 的非等比缩放，直接贴会把图标拉变形
        var root = new GameObject("Icons").transform;
        iconRoot = root;
        root.SetParent(transform, false);
        root.localScale = new Vector3(1f / Mathf.Max(1e-6f, ls.x), 1f / Mathf.Max(1e-6f, ls.y), 1f / Mathf.Max(1e-6f, ls.z))
                          * 1f;
        // 让容器的世界缩放为 1：再乘以父物体链除木块以外的缩放修正
        Vector3 rootLossy = root.lossyScale;
        root.localScale = Vector3.Scale(root.localScale,
            new Vector3(1f / Mathf.Max(1e-6f, rootLossy.x), 1f / Mathf.Max(1e-6f, rootLossy.y), 1f / Mathf.Max(1e-6f, rootLossy.z)));

        const float eps = 0.0006f;   // 稍微浮出表面，避免闪烁
        float aspect = icon.bounds.size.x / Mathf.Max(1e-6f, icon.bounds.size.y);

        // 四个侧面：(法线方向, 面宽, 面高)
        AddFace(root, icon, new Vector3( hx + eps, 0, 0), Vector3.right,   Vector3.up, 2 * hz, 2 * hy, aspect);
        AddFace(root, icon, new Vector3(-hx - eps, 0, 0), Vector3.left,    Vector3.up, 2 * hz, 2 * hy, aspect);
        AddFace(root, icon, new Vector3(0, 0,  hz + eps), Vector3.forward, Vector3.up, 2 * hx, 2 * hy, aspect);
        AddFace(root, icon, new Vector3(0, 0, -hz - eps), Vector3.back,    Vector3.up, 2 * hx, 2 * hy, aspect);

        // 顶面：图标的"上"朝向远离观众的方向，从桌前低头看是正的
        if (iconOnTop)
            AddFace(root, icon, new Vector3(0, hy + eps, 0), Vector3.up, AwayFromViewer(), 2 * hx, 2 * hz, aspect);
    }

    /// <summary>
    /// "远离观众"的方向（木块局部坐标），并吸附到木块最接近的一条边上，保证顶面图标是正的。
    /// 用两个区域的连线判断（左区 → 右区 = 观众的右手方向），不依赖启动时相机在哪。
    /// </summary>
    Vector3 AwayFromViewer()
    {
        Vector3 away = Vector3.forward;
        if (zoneLeft != null && zoneRight != null)
        {
            Vector3 right = Center(zoneRight) - Center(zoneLeft);
            right.y = 0f;
            if (right.sqrMagnitude > 1e-8f) away = Vector3.Cross(right.normalized, Vector3.up);
        }

        Vector3 d = transform.InverseTransformDirection(away);
        d.y = 0f;
        if (Mathf.Abs(d.x) > Mathf.Abs(d.z)) return new Vector3(Mathf.Sign(d.x), 0f, 0f);
        return new Vector3(0f, 0f, d.z >= 0f ? 1f : -1f);
    }

    static Vector3 Center(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        return r != null ? r.bounds.center : t.position;
    }

    void AddFace(Transform root, Sprite icon, Vector3 localPos, Vector3 normal, Vector3 up,
                 float faceW, float faceH, float aspect)
    {
        var go = new GameObject("Icon");
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPos;
        // 精灵从 -Z 方向看是正面，所以让它的 +Z 朝向木块内部
        go.transform.localRotation = Quaternion.LookRotation(-normal, up);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = icon;

        // 在面里按比例放到最大，四周留边
        float h = Mathf.Min(faceH * iconFill, faceW * iconFill / Mathf.Max(1e-6f, aspect));
        float s = h / Mathf.Max(1e-6f, icon.bounds.size.y);
        go.transform.localScale = new Vector3(s, s, s);
    }

    // ================= 消失 / 重新出现 =================

    /// <summary>在松手位置留一个只有外观的残影（没有抓取组件），缩小后删除</summary>
    void SpawnGhost()
    {
        var ghost = new GameObject($"{name}_Ghost").transform;
        ghost.SetPositionAndRotation(transform.position, transform.rotation);
        ghost.localScale = transform.lossyScale;

        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (mf != null && mr != null)
        {
            ghost.gameObject.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var gr = ghost.gameObject.AddComponent<MeshRenderer>();
            gr.sharedMaterials = mr.sharedMaterials;
            gr.shadowCastingMode = mr.shadowCastingMode;
        }

        // 图标容器里只有 SpriteRenderer，直接复制
        if (iconRoot != null)
        {
            var icons = Instantiate(iconRoot.gameObject, ghost).transform;
            icons.localPosition = iconRoot.localPosition;
            icons.localRotation = iconRoot.localRotation;
            icons.localScale = iconRoot.localScale;
        }

        StartCoroutine(Vanish(ghost));
    }

    IEnumerator Vanish(Transform ghost)
    {
        Vector3 from = ghost.localScale;
        var sprites = ghost.GetComponentsInChildren<SpriteRenderer>();

        for (float t = 0f; t < vanishTime; t += Time.deltaTime)
        {
            float k = t / vanishTime;
            ghost.localScale = from * (1f - k * k);   // 先慢后快
            foreach (var s in sprites) { var c = s.color; c.a = 1f - k; s.color = c; }
            yield return null;
        }

        Destroy(ghost.gameObject);
    }

    IEnumerator Respawn()
    {
        // 等一帧：让抓取系统先处理完松手，再移动木块
        yield return null;

        transform.SetPositionAndRotation(HomePos(), HomeRot());
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.position = transform.position;
            rb.rotation = transform.rotation;
        }
        Respawned?.Invoke(this);

        // 从很小放大到正常大小（不从 0 开始，避免碰撞体尺寸为 0 的警告）
        for (float t = 0f; t < spawnTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0.01f, 1f, t / spawnTime);
            transform.localScale = homeLocalScale * k;
            yield return null;
        }

        transform.localScale = homeLocalScale;
        returning = null;
    }

    Vector3 HomePos() => homeParent != null ? homeParent.TransformPoint(homeLocalPos) : homeLocalPos;
    Quaternion HomeRot() => homeParent != null ? homeParent.rotation * homeLocalRot : homeLocalRot;
}
