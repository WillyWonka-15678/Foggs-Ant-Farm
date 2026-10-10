using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 手柄两点标定：把虚拟箱子（以及整个 VirtualScene）对齐到真实的亚克力箱。
///
/// 流程：
///   1. 同时按住两个手柄的握把键 2 秒进入标定（头显里启动时自动进入）
///      只有等待阶段能进入；手里抓着木块时不计时
///   2. 右手柄前端的黄色小球碰住真实箱子"靠近观众那一面"的左上角，按右扳机
///   3. 再碰右上角，按右扳机
///   4. 微调：左摇杆前后左右平移，右摇杆上下移动 / 左右旋转
///      A = 完成，B = 重新点
///   点角的两步里按 B = 取消：退出标定，场景回到进入标定之前的位置
///
/// 正面自动判断：根据 Zone_Left / Zone_Right 的位置，保证标定后左区永远在观众左手边。
/// 标定期间自动停用 ControllerInput（避免扳机和 A 键误放木块），虚拟木块看得见但拿不起来。
/// 挂在 Session 物体上。
/// </summary>
public class BoxCalibrator : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("整个虚拟场景的根物体（包含 Cube、Area_Left、Area_Right）")]
    public Transform virtualScene;
    [Tooltip("代表亚克力箱的 Cube")]
    public Transform caseBox;
    public Transform zoneLeft;
    public Transform zoneRight;
    [Tooltip("标定期间停用的输入脚本（不填会自动找 ControllerInput）")]
    public Behaviour[] disableWhileCalibrating;

    [Header("测量点")]
    [Tooltip("测量点相对手柄的位置（米）。小球没对准手柄前端时调这里")]
    public Vector3 tipOffset = new Vector3(0f, -0.01f, 0.05f);
    public float markerSize = 0.012f;

    [Header("进入方式")]
    [Tooltip("头显里启动时自动进入标定（编辑器里不会）")]
    public bool autoStartOnDevice = true;
    [Tooltip("同时按住两个握把键多少秒进入标定")]
    public float holdToEnter = 2f;

    [Header("微调速度")]
    public float moveSpeed = 0.05f;      // 米 / 秒
    public float liftSpeed = 0.03f;      // 米 / 秒
    public float rotateSpeed = 10f;      // 度 / 秒
    [Range(0f, 0.5f)] public float stickDeadzone = 0.15f;

    [Header("提示面板（相对观众的头）")]
    public float uiDistance = 0.45f;
    public float uiOffsetUp = -0.08f;
    public float uiWidth = 0.28f;

    public bool IsCalibrating => step != Step.Off;
    public bool HasCalibrated { get; private set; }

    enum Step { Off, PickLeft, PickRight, Adjust }
    Step step = Step.Off;

    OVRCameraRig rig;
    SessionManager session;
    Vector3 savedPos;        // 进入标定前场景的位置和朝向，取消时恢复
    Quaternion savedRot;
    Transform marker;
    GameObject ui;
    TextMeshProUGUI uiText;
    LineRenderer[] outline;
    Vector3 pickLeft;
    float holdTimer;
    float widthError;
    Bounds caseLocal;

    // ================= 启动 =================

    void Start()
    {
        rig = FindFirstObjectByType<OVRCameraRig>();
        session = FindFirstObjectByType<SessionManager>();

        if (caseBox != null)
        {
            var mf = caseBox.GetComponent<MeshFilter>();
            caseLocal = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds
                                                              : new Bounds(Vector3.zero, Vector3.one);
        }

        if (disableWhileCalibrating == null || disableWhileCalibrating.Length == 0)
        {
            var ci = FindFirstObjectByType<ControllerInput>();
            if (ci != null) disableWhileCalibrating = new Behaviour[] { ci };
        }

        BuildMarker();
        BuildUI();
        BuildOutline();
        ShowCalibrationVisuals(false);

        if (autoStartOnDevice && !Application.isEditor) Begin();
    }

    // ================= 每帧 =================

    void Update()
    {
        // 隐藏入口：同时按住两个握把。
        // 只在等待阶段有效（观众开始操作后不再标定）；手里抓着木块时不算（握把键也是抓木块的键）
        bool both = OVRInput.Get(OVRInput.RawButton.LHandTrigger) && OVRInput.Get(OVRInput.RawButton.RHandTrigger);
        bool allowed = (session == null || session.Phase == ShiftPhase.Waiting) && !VirtualBlock.AnyHeld;
        if (both && allowed && step == Step.Off)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdToEnter) { holdTimer = 0f; Begin(); }
        }
        else holdTimer = 0f;

        if (step == Step.Off) return;

        Vector3 tip = TipWorld();
        marker.position = tip;

        switch (step)
        {
            case Step.PickLeft:
                if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
                {
                    pickLeft = tip;
                    step = Step.PickRight;
                    Buzz();
                }
                else if (OVRInput.GetDown(OVRInput.RawButton.B)) { Cancel(); return; }
                break;

            case Step.PickRight:
                if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
                {
                    Solve(pickLeft, tip);
                    step = Step.Adjust;
                    Buzz();
                }
                else if (OVRInput.GetDown(OVRInput.RawButton.B)) { Cancel(); return; }
                break;

            case Step.Adjust:
                Adjust();
                if (OVRInput.GetDown(OVRInput.RawButton.A)) Finish();
                else if (OVRInput.GetDown(OVRInput.RawButton.B)) { step = Step.PickLeft; Buzz(); }
                break;
        }

        UpdateUI();
        UpdateOutline();
    }

    // ================= 进入 / 退出 =================

    public void Begin()
    {
        if (virtualScene != null)
        {
            savedPos = virtualScene.position;
            savedRot = virtualScene.rotation;
        }
        step = Step.PickLeft;
        SetInputs(false);
        VirtualBlock.SetLocked(true);    // 标定完才能拿起木块
        ShowCalibrationVisuals(true);
        Debug.Log("[Calibration] Started");
    }

    /// <summary>取消标定：场景回到进入标定之前的位置</summary>
    void Cancel()
    {
        if (virtualScene != null) virtualScene.SetPositionAndRotation(savedPos, savedRot);
        step = Step.Off;
        SetInputs(true);
        VirtualBlock.SetLocked(false);
        ShowCalibrationVisuals(false);
        Buzz();
        Debug.Log("[Calibration] Cancelled. Scene restored.");
    }

    void Finish()
    {
        step = Step.Off;
        HasCalibrated = true;
        SetInputs(true);
        VirtualBlock.SetLocked(false);
        ShowCalibrationVisuals(false);
        Buzz();
        Debug.Log($"[Calibration] Done. Scene at {virtualScene.position:F3}, yaw {virtualScene.eulerAngles.y:0.0}°, width error {widthError * 100f:0.0} cm");
    }

    void SetInputs(bool on)
    {
        CancelInvoke(nameof(EnableInputs));
        // 恢复时稍等一下：退出标定按的 A / B 不能在同一瞬间又被当成"放木块"
        if (on) Invoke(nameof(EnableInputs), 0.2f);
        else ApplyInputs(false);
    }

    void EnableInputs() => ApplyInputs(true);

    void ApplyInputs(bool on)
    {
        if (disableWhileCalibrating == null) return;
        foreach (var b in disableWhileCalibrating) if (b != null) b.enabled = on;
    }

    // ================= 计算 =================

    Vector3 TipWorld()
    {
        Vector3 lp = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        Quaternion lr = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
        Vector3 local = lp + lr * tipOffset;

        Transform space = rig != null ? rig.trackingSpace : null;
        return space != null ? space.TransformPoint(local) : local;
    }

    /// <summary>虚拟箱子正面的左上角和右上角（VirtualScene 的局部坐标）</summary>
    void VirtualCorners(out Vector3 aLeft, out Vector3 aRight)
    {
        Vector3 min = caseLocal.min, max = caseLocal.max;

        // 判断正面：观众看过去时 Zone_Left 必须在左边
        bool frontIsMinZ = true;
        if (zoneLeft != null && zoneRight != null)
        {
            float xl = caseBox.InverseTransformPoint(Center(zoneLeft)).x;
            float xr = caseBox.InverseTransformPoint(Center(zoneRight)).x;
            frontIsMinZ = xl < xr;   // 从 -Z 往 +Z 看时，左 = -X
        }

        Vector3 cl, cr;
        if (frontIsMinZ) { cl = new Vector3(min.x, max.y, min.z); cr = new Vector3(max.x, max.y, min.z); }
        else             { cl = new Vector3(max.x, max.y, max.z); cr = new Vector3(min.x, max.y, max.z); }

        aLeft  = virtualScene.InverseTransformPoint(caseBox.TransformPoint(cl));
        aRight = virtualScene.InverseTransformPoint(caseBox.TransformPoint(cr));
    }

    static Vector3 Center(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        return r != null ? r.bounds.center : t.position;
    }

    void Solve(Vector3 pLeft, Vector3 pRight)
    {
        if (virtualScene == null || caseBox == null) return;

        VirtualCorners(out Vector3 aL, out Vector3 aR);
        Vector3 s = virtualScene.lossyScale;
        aL = Vector3.Scale(aL, s);
        aR = Vector3.Scale(aR, s);

        // 只算水平方向的旋转（桌面是水平的）
        Vector3 real = pRight - pLeft; real.y = 0f;
        Vector3 virt = aR - aL;       virt.y = 0f;
        float yaw = Mathf.Atan2(real.x, real.z) * Mathf.Rad2Deg - Mathf.Atan2(virt.x, virt.z) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        Vector3 realMid = (pLeft + pRight) * 0.5f;
        Vector3 virtMid = (aL + aR) * 0.5f;
        virtualScene.SetPositionAndRotation(realMid - rot * virtMid, rot);

        // 宽度误差：点出来的宽度和虚拟箱子宽度的差，用来检查测量点准不准
        widthError = real.magnitude - virt.magnitude;
        Debug.Log($"[Calibration] Solved. Width error {widthError * 100f:0.0} cm");
    }

    void Adjust()
    {
        float dt = Time.deltaTime;
        Vector2 l = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        Vector2 r = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        l = Dead(l); r = Dead(r);

        // 平移方向以观众的视角为准
        Transform head = Camera.main != null ? Camera.main.transform : null;
        Vector3 fwd = head != null ? head.forward : Vector3.forward;
        fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        virtualScene.position += (right * l.x + fwd * l.y) * moveSpeed * dt;
        virtualScene.position += Vector3.up * r.y * liftSpeed * dt;

        if (Mathf.Abs(r.x) > 0f && caseBox != null)
        {
            Vector3 pivot = caseBox.TransformPoint(caseLocal.center);
            virtualScene.RotateAround(pivot, Vector3.up, r.x * rotateSpeed * dt);
        }
    }

    Vector2 Dead(Vector2 v) => v.magnitude < stickDeadzone ? Vector2.zero : v;

    void Buzz()
    {
        OVRInput.SetControllerVibration(1f, 0.5f, OVRInput.Controller.RTouch);
        CancelInvoke(nameof(StopBuzz));
        Invoke(nameof(StopBuzz), 0.08f);
    }

    void StopBuzz() => OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);

    // ================= 可视化 =================

    void BuildMarker()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "CalibrationTip";
        Destroy(go.GetComponent<Collider>());
        var mat = new Material(Shader.Find("Sprites/Default")) { color = Palette.Yellow };
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.transform.localScale = Vector3.one * markerSize;
        marker = go.transform;
    }

    void BuildUI()
    {
        const float W = 560f, H = 150f;
        ui = new GameObject("CalibrationUI");
        var c = HUDFactory.CreateCanvas(ui.transform, "Canvas", W, H);
        c.localScale = Vector3.one * (uiWidth / W);
        c.GetComponent<Canvas>().sortingOrder = 900;

        HUDFactory.Box(c, "Panel", 0, 0, W, H, new Color(0f, 0f, 0f, 0.9f));
        HUDFactory.Box(c, "Strip", 0, 0, 10, H, Palette.Yellow);
        uiText = HUDFactory.Label(c, "Text", 26, 8, W - 40, H - 16, 22, Color.white,
                                  TextAlignmentOptions.MidlineLeft, wrap: true);
    }

    void UpdateUI()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            Transform h = cam.transform;
            Vector3 target = h.position + h.forward * uiDistance + h.up * uiOffsetUp;
            ui.transform.position = Vector3.Lerp(ui.transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
            HUDFactory.FaceCamera(ui.transform);
        }

        switch (step)
        {
            case Step.PickLeft:
                uiText.text = "<b>CALIBRATION 1 / 2</b>\nTouch the <color=#FAC775>FRONT-LEFT</color> top corner of the box with the yellow ball, then pull the right trigger.   <b>B</b>: cancel";
                break;
            case Step.PickRight:
                uiText.text = "<b>CALIBRATION 2 / 2</b>\nTouch the <color=#FAC775>FRONT-RIGHT</color> top corner, then pull the right trigger.   <b>B</b>: cancel";
                break;
            case Step.Adjust:
                string err = Mathf.Abs(widthError) <= 0.015f
                    ? $"<color=#5DCAA5>width error {widthError * 100f:0.0} cm</color>"
                    : $"<color=#F09595>width error {widthError * 100f:0.0} cm — check the ball position</color>";
                uiText.text = $"<b>FINE TUNE</b>   {err}\nLeft stick: move    Right stick: up / down, rotate\n<b>A</b>: done    <b>B</b>: redo (then B again to cancel)";
                break;
        }
    }

    void BuildOutline()
    {
        var mat = new Material(Shader.Find("Sprites/Default"));
        outline = new LineRenderer[6];   // 底面一圈、顶面一圈、四条竖边
        for (int i = 0; i < 6; i++)
        {
            var go = new GameObject($"CalibOutline{i}");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.loop = i < 2;
            lr.positionCount = i < 2 ? 4 : 2;
            lr.startWidth = lr.endWidth = 0.002f;
            lr.startColor = lr.endColor = Palette.Yellow;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outline[i] = lr;
        }
    }

    void UpdateOutline()
    {
        if (caseBox == null) return;
        Vector3 a = caseLocal.min, b = caseLocal.max;
        Vector3[] p =
        {
            caseBox.TransformPoint(new Vector3(a.x, a.y, a.z)), caseBox.TransformPoint(new Vector3(b.x, a.y, a.z)),
            caseBox.TransformPoint(new Vector3(b.x, a.y, b.z)), caseBox.TransformPoint(new Vector3(a.x, a.y, b.z)),
            caseBox.TransformPoint(new Vector3(a.x, b.y, a.z)), caseBox.TransformPoint(new Vector3(b.x, b.y, a.z)),
            caseBox.TransformPoint(new Vector3(b.x, b.y, b.z)), caseBox.TransformPoint(new Vector3(a.x, b.y, b.z)),
        };
        for (int i = 0; i < 4; i++) outline[0].SetPosition(i, p[i]);
        for (int i = 0; i < 4; i++) outline[1].SetPosition(i, p[i + 4]);
        for (int i = 0; i < 4; i++) { outline[2 + i].SetPosition(0, p[i]); outline[2 + i].SetPosition(1, p[i + 4]); }
    }

    void ShowCalibrationVisuals(bool on)
    {
        if (marker != null) marker.gameObject.SetActive(on);
        if (ui != null) ui.SetActive(on);
        if (outline != null) foreach (var lr in outline) if (lr != null) lr.enabled = on;
        if (on && ui != null && Camera.main != null)
        {
            Transform h = Camera.main.transform;
            ui.transform.position = h.position + h.forward * uiDistance + h.up * uiOffsetUp;
        }
    }
}
