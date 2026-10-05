using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 悬浮在空中的系统提示，像砖块一样往上叠。
///   左边小人的消息 → 箱子左前角外侧
///   右边小人的消息 → 箱子右前角外侧
///   观众本人的消息 → YOU 面板下方（叠得越多，YOU 被顶得越高）
/// 新消息出现在最下面，旧消息往上推。
///
/// 三种退场：
///   照做了（任务完成）/ 奖励到时 → 上浮淡出，图标飞走（反转后飞进 YOU 面板）
///   没照做，情况自己变了       → 变灰、掉落淡出
///   一班结束                   → 直接淡出
/// 任务没完成时一直呼吸闪烁，而且越拖越快。
/// 挂在 Session 物体上。
/// </summary>
public class FloatingPrompts : MonoBehaviour
{
    [Header("引用")]
    public SessionManager session;
    [Tooltip("可选：提示完成时图标飞进 YOU 面板")]
    public FeedbackFX feedback;
    [Tooltip("代表亚克力箱的物体（Cube）")]
    public Transform caseBox;

    [Header("小人提示：箱子两侧外面（以箱子尺寸为 1）")]
    public float sideToastWidth = 0.55f;
    public float sideMargin = 0.05f;
    [Tooltip("最下面一条的高度：0 = 箱子底面，1 = 箱子顶面")]
    [Range(0f, 1.5f)] public float sideHeight = 0.6f;
    public float forwardOffset = 0f;

    [Header("观众提示：YOU 面板下方")]
    public float youToastWidth = 1f;
    [Tooltip("提示堆与上下之间的留白，以提示高度为单位")]
    public float youGap = 0.25f;

    [Header("没有 Case Box 时：小人面板上方")]
    public float panelToastWidth = 1.1f;
    public float panelGap = 0.08f;

    [Header("堆叠")]
    [Tooltip("提示之间的间距，以提示高度为单位")]
    public float stackSpacing = 0.15f;
    [Tooltip("每一堆最多几条；超出时最旧的非任务提示先退场")]
    public int maxStack = 5;
    [Tooltip("位置变化的平滑速度")]
    public float stackSmoothing = 10f;

    [Header("动画")]
    public float fadeIn = 0.25f;
    public float fadeOut = 0.35f;
    public float fallTime = 0.6f;
    [Tooltip("出现时上滑的距离，以提示高度为单位")]
    public float slide = 0.6f;

    [Header("呼吸闪烁（每秒次数）")]
    public float pulseNotify = 2f;
    public float pulseBonus = 1f;
    public float pulseAssist = 0.5f;
    [Tooltip("任务每拖这么多秒，闪烁速度增加一倍")]
    public float escalateSeconds = 10f;
    [Tooltip("闪烁最多加快到几倍")]
    public float maxEscalation = 3f;
    [Range(0f, 1f)] public float borderMinAlpha = 0.15f;
    [Range(0f, 1f)] public float borderMaxAlpha = 0.9f;
    [Range(0f, 1f)] public float iconMinBrightness = 0.5f;
    [Range(0f, 0.3f)] public float iconPulseScale = 0.08f;

    const float W = 440f, H = 76f, B = 3f;

    enum Phase { In, Hold, OutUp, OutFall, OutQuiet }
    enum Slot { Left, Right, You }

    class Toast
    {
        public Slot slot;
        public Transform root;
        public CanvasGroup group;
        public Image strip, icon;
        public Image[] border;
        public TextMeshProUGUI text;
        public PromptMessage msg;
        public Phase phase;
        public float t, age;
        public float worldW, worldH;
        public float stackY;          // 当前在堆里的高度（米）
        public bool placed;
        public Vector3 leaveFrom;     // 退场时的位置
    }

    readonly List<Toast> stackLeft = new List<Toast>();
    readonly List<Toast> stackRight = new List<Toast>();
    readonly List<Toast> stackYou = new List<Toast>();
    readonly List<Toast> leaving = new List<Toast>();

    bool subscribed;
    Bounds caseLocal;
    bool hasCase;

    void Start()
    {
        if (caseBox != null)
        {
            var mf = caseBox.GetComponent<MeshFilter>();
            caseLocal = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds
                                                              : new Bounds(Vector3.zero, Vector3.one);
            hasCase = true;
        }
        TrySubscribe();
    }

    void OnDestroy()
    {
        if (subscribed && session != null && session.Prompts != null)
            session.Prompts.Posted -= OnPosted;
    }

    void TrySubscribe()
    {
        if (subscribed || session == null || session.Prompts == null) return;
        session.Prompts.Posted += OnPosted;
        subscribed = true;
    }

    // ================= 收到消息 =================

    void OnPosted(PromptMessage m)
    {
        Slot slot = SlotFor(m.Target);
        var toast = Build(slot);
        toast.msg = m;
        toast.phase = Phase.In;
        Apply(toast);

        var stack = StackOf(slot);
        stack.Insert(0, toast);   // 新消息在最下面

        // 超出上限：最旧的非任务提示先退场
        if (stack.Count > maxStack)
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (!stack[i].msg.IsTask) { Leave(stack[i], Phase.OutQuiet); break; }
            }
        }
    }

    Slot SlotFor(WorkerController target)
    {
        if (session != null && target != null)
        {
            if (target == session.workerLeft)  return Slot.Left;
            if (target == session.workerRight) return Slot.Right;
        }
        return Slot.You;
    }

    List<Toast> StackOf(Slot s) => s == Slot.Left ? stackLeft : s == Slot.Right ? stackRight : stackYou;

    // ================= 尺寸与搭建 =================

    float YouToastWorldWidth()
    {
        return session != null && session.YouPanel != null ? session.YouPanel.WorldWidth * youToastWidth : 0.2f;
    }

    float ToastWorldWidth(Slot slot)
    {
        if (slot == Slot.You) return YouToastWorldWidth();
        if (hasCase) return Mathf.Abs(caseLocal.size.x * caseBox.lossyScale.x) * sideToastWidth;

        float unit = 1f;
        if (slot == Slot.Left && session.LeftHUD != null)   unit = session.LeftHUD.WorkerHeight;
        if (slot == Slot.Right && session.RightHUD != null) unit = session.RightHUD.WorkerHeight;
        return unit * panelToastWidth;
    }

    Toast Build(Slot slot)
    {
        var go = new GameObject($"Toast_{slot}");
        var toast = new Toast { slot = slot, root = go.transform };
        toast.worldW = ToastWorldWidth(slot);
        toast.worldH = toast.worldW * H / W;

        var c = HUDFactory.CreateCanvas(go.transform, "Canvas", W, H);
        c.localScale = Vector3.one * (toast.worldW / W);
        toast.group = c.gameObject.AddComponent<CanvasGroup>();
        toast.group.alpha = 0f;

        HUDFactory.Box(c, "Panel", 0, 0, W, H, Palette.Panel);
        toast.strip = HUDFactory.Box(c, "Strip", 0, 0, 10, H, Palette.Gray);

        toast.icon = HUDFactory.Icon(c, "Icon", 20, 6, 48, 64);
        var irt = toast.icon.rectTransform;
        irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition += new Vector2(24f, -32f);

        toast.text = HUDFactory.Label(c, "Text", 78, 0, W - 92, H, 21, Color.white,
                                      TextAlignmentOptions.MidlineLeft, wrap: true);
        toast.border = new[]
        {
            HUDFactory.Box(c, "BorderTop",    0, 0,     W, B, Color.clear),
            HUDFactory.Box(c, "BorderBottom", 0, H - B, W, B, Color.clear),
            HUDFactory.Box(c, "BorderLeft",   0, 0,     B, H, Color.clear),
            HUDFactory.Box(c, "BorderRight",  W - B, 0, B, H, Color.clear),
        };
        return toast;
    }

    void Apply(Toast toast)
    {
        var sp = session.BlockIcon(toast.msg.Visual);
        toast.icon.sprite = sp;
        toast.icon.enabled = sp != null;
        toast.text.color = toast.msg.Kind == BlockType.Bonus ? Palette.Yellow : Color.white;
    }

    // ================= 每帧 =================

    void LateUpdate()
    {
        TrySubscribe();
        bool running = session != null && session.Phase == ShiftPhase.Running;

        // 一班结束：所有提示直接淡出
        if (!running)
        {
            QuietAll(stackLeft); QuietAll(stackRight); QuietAll(stackYou);
        }

        // 检查完成 / 失效 / 到时
        CheckStack(stackLeft); CheckStack(stackRight); CheckStack(stackYou);

        // YOU 面板为提示堆预留空间
        if (session != null && session.YouPanel != null)
        {
            float h = YouToastWorldWidth() * H / W;
            int n = Mathf.Max(1, stackYou.Count);
            session.YouPanel.ReservedBelow = h * (2f * youGap + n + (n - 1) * stackSpacing);
        }

        LayoutStack(stackLeft, Slot.Left);
        LayoutStack(stackRight, Slot.Right);
        LayoutStack(stackYou, Slot.You);
        UpdateLeaving();
    }

    void QuietAll(List<Toast> stack)
    {
        for (int i = stack.Count - 1; i >= 0; i--) Leave(stack[i], Phase.OutQuiet);
    }

    void CheckStack(List<Toast> stack)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            var toast = stack[i];
            var m = toast.msg;
            toast.age += Time.deltaTime;

            if (m.IsResolved != null && m.IsResolved())
            {
                SendIcon(toast);                        // 照做了：图标飞走
                Leave(toast, Phase.OutUp);
            }
            else if (m.IsVoid != null && m.IsVoid())
            {
                Leave(toast, m.IsTask ? Phase.OutFall : Phase.OutQuiet);   // 错过了：变灰掉落
            }
            else if (m.Duration > 0f && toast.age >= m.Duration)
            {
                SendIcon(toast);                        // 奖励到时：图标飞走
                Leave(toast, Phase.OutUp);
            }
        }
    }

    void Leave(Toast toast, Phase how)
    {
        StackOf(toast.slot).Remove(toast);
        toast.leaveFrom = toast.root.position;
        toast.phase = how;
        toast.t = 0f;
        if (how == Phase.OutFall) MakeGray(toast);
        leaving.Add(toast);
    }

    // ================= 堆叠布局 =================

    void LayoutStack(List<Toast> stack, Slot slot)
    {
        if (stack.Count == 0) return;
        if (!Anchor(slot, stack[0].worldH, out Vector3 anchor)) return;

        float dt = Time.deltaTime;
        for (int i = 0; i < stack.Count; i++)
        {
            var toast = stack[i];
            float targetY = i * toast.worldH * (1f + stackSpacing);   // 越旧越高
            if (!toast.placed) { toast.stackY = targetY; toast.placed = true; }
            toast.stackY = Mathf.Lerp(toast.stackY, targetY, 1f - Mathf.Exp(-stackSmoothing * dt));

            toast.t += dt;
            float alpha = 1f, slideOff = 0f;
            if (toast.phase == Phase.In)
            {
                float k = Mathf.Clamp01(toast.t / fadeIn);
                alpha = k;
                slideOff = -slide * (1f - k) * toast.worldH;
                if (k >= 1f) { toast.phase = Phase.Hold; toast.t = 0f; }
            }

            toast.root.position = anchor + Vector3.up * (toast.stackY + slideOff);
            HUDFactory.FaceCamera(toast.root);
            toast.group.alpha = alpha;

            if (toast.msg.Text != null) toast.text.text = toast.msg.Text();
            Breathe(toast);
        }
    }

    void UpdateLeaving()
    {
        float dt = Time.deltaTime;
        for (int i = leaving.Count - 1; i >= 0; i--)
        {
            var toast = leaving[i];
            toast.t += dt;
            float k;
            Vector3 pos = toast.leaveFrom;

            switch (toast.phase)
            {
                case Phase.OutUp:
                    k = Mathf.Clamp01(toast.t / fadeOut);
                    pos += Vector3.up * toast.worldH * 0.5f * k;
                    toast.group.alpha = 1f - k;
                    break;

                case Phase.OutFall:
                    k = Mathf.Clamp01(toast.t / fallTime);
                    pos -= Vector3.up * toast.worldH * 1.5f * k * k;   // 加速下落
                    toast.group.alpha = 1f - k;
                    break;

                default: // OutQuiet
                    k = Mathf.Clamp01(toast.t / fadeOut);
                    toast.group.alpha = Mathf.Min(toast.group.alpha, 1f - k);
                    break;
            }

            toast.root.position = pos;
            HUDFactory.FaceCamera(toast.root);

            if (k >= 1f)
            {
                Destroy(toast.root.gameObject);
                leaving.RemoveAt(i);
            }
        }
    }

    // ================= 位置 =================

    /// <summary>每一堆最下面那条的位置</summary>
    bool Anchor(Slot slot, float toastH, out Vector3 anchor)
    {
        anchor = Vector3.zero;

        if (slot == Slot.You)
        {
            var y = session != null ? session.YouPanel : null;
            if (y == null) return false;
            int n = Mathf.Max(1, stackYou.Count);
            float stackHeight = toastH * (n + (n - 1) * stackSpacing);
            anchor = y.transform.position - Vector3.up * (toastH * youGap + stackHeight);
            return true;
        }

        if (hasCase && caseBox != null)
        {
            var cam = Camera.main;

            float frontZ = caseLocal.max.z;
            if (cam != null)
            {
                Vector3 camLocal = caseBox.InverseTransformPoint(cam.transform.position);
                frontZ = camLocal.z >= caseLocal.center.z ? caseLocal.max.z : caseLocal.min.z;
            }
            float towardViewer = Mathf.Sign(frontZ - caseLocal.center.z);
            float z = frontZ + towardViewer * caseLocal.size.z * forwardOffset;

            float xSign = cam != null ? Mathf.Sign(Vector3.Dot(caseBox.right, cam.transform.right)) : 1f;
            float side = slot == Slot.Left ? -1f : 1f;
            float scaleX = Mathf.Max(0.0001f, Mathf.Abs(caseBox.lossyScale.x));
            float halfToastLocal = ToastWorldWidth(slot) * 0.5f / scaleX;
            float dist = caseLocal.extents.x + caseLocal.size.x * sideMargin + halfToastLocal;
            float x = caseLocal.center.x + side * xSign * dist;

            float yLocal = caseLocal.min.y + caseLocal.size.y * sideHeight;
            anchor = caseBox.TransformPoint(new Vector3(x, yLocal, z));
            return true;
        }

        var hud = session == null ? null : (slot == Slot.Left ? session.LeftHUD : session.RightHUD);
        if (hud == null) return false;
        anchor = hud.transform.position + hud.transform.up * (hud.WorldPanelHeight + panelGap * hud.WorkerHeight);
        return true;
    }

    // ================= 外观 =================

    void Breathe(Toast toast)
    {
        float hz;
        switch (toast.msg.Kind)
        {
            case BlockType.Notify: hz = pulseNotify; break;
            case BlockType.Bonus:  hz = pulseBonus;  break;
            default:               hz = pulseAssist; break;
        }

        // 任务拖得越久，闪得越快
        if (toast.msg.IsTask && escalateSeconds > 0f)
            hz *= Mathf.Min(maxEscalation, 1f + toast.age / escalateSeconds);

        // 用累积相位，避免变速时跳帧
        float phase = toast.age * hz;
        float wave = 0.5f + 0.5f * Mathf.Sin(phase * 2f * Mathf.PI);
        Color c = Palette.ForBlock(toast.msg.Visual);   // 颜色 = 要你放的木块；节奏 = 系统的情绪

        toast.strip.color = Color.Lerp(Color.Lerp(c, Color.black, 0.45f), Color.Lerp(c, Color.white, 0.25f), wave);

        Color bc = c;
        bc.a = Mathf.Lerp(borderMinAlpha, borderMaxAlpha, wave);
        foreach (var b in toast.border) b.color = bc;

        if (toast.icon.enabled)
        {
            float v = Mathf.Lerp(iconMinBrightness, 1f, wave);
            toast.icon.color = new Color(v, v, v, 1f);
            toast.icon.rectTransform.localScale = Vector3.one * (1f + iconPulseScale * (wave - 0.5f) * 2f);
        }
    }

    void MakeGray(Toast toast)
    {
        toast.strip.color = Palette.Gray;
        foreach (var b in toast.border) b.color = new Color(Palette.Gray.r, Palette.Gray.g, Palette.Gray.b, 0.6f);
        toast.text.color = Palette.Dim;
        toast.icon.rectTransform.localScale = Vector3.one;

        Sprite gray = session != null ? session.InvalidIcon(toast.msg.Visual) : null;
        if (gray != null)
        {
            toast.icon.sprite = gray;
            toast.icon.color = Color.white;
            toast.icon.enabled = true;
        }
        else
        {
            toast.icon.color = new Color(0.45f, 0.45f, 0.45f, 1f);
        }
    }

    void SendIcon(Toast toast)
    {
        if (feedback == null || toast.msg == null) return;
        Vector3 center = toast.root.position + toast.root.up * (toast.worldH * 0.5f);
        feedback.SendToYou(toast.msg.Kind, center);
    }
}
