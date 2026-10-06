using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 键盘模拟放置木块。版本二时禁用它，换成 QR / RFID 输入脚本，其他脚本不用改。
///
///            NOTIFY  BONUS  ASSIST
/// 左区（骑手）   Q       W      E
/// 右区（工人）   I       O      P
/// R = 立即开始新的一班（跳过等待）
/// 等待阶段按任意放置键 = 开始这一班（这一下不作用于小人）
/// </summary>
public class KeyboardInput : MonoBehaviour
{
    [Tooltip("左区小人（骑手）")]
    public WorkerController workerLeft;
    [Tooltip("右区小人（工人）")]
    public WorkerController workerRight;

    [Tooltip("可选：拖入 SessionManager，R 键会开始新的一班")]
    public SessionManager session;

    [Tooltip("在 Console 打印每次操作")]
    public bool logToConsole = true;

    int actionCount;

    void Start()
    {
        // 没拖 SessionManager 时自动找（等待阶段靠它开始一班）
        if (session == null) session = FindFirstObjectByType<SessionManager>();
    }

    void Update()
    {
        // 左区
        if (Pressed(KeyLeftNotify()))  Place(workerLeft,  BlockType.Notify);
        if (Pressed(KeyLeftBonus()))   Place(workerLeft,  BlockType.Bonus);
        if (Pressed(KeyLeftAssist()))  Place(workerLeft,  BlockType.Assist);

        // 右区
        if (Pressed(KeyRightNotify())) Place(workerRight, BlockType.Notify);
        if (Pressed(KeyRightBonus()))  Place(workerRight, BlockType.Bonus);
        if (Pressed(KeyRightAssist())) Place(workerRight, BlockType.Assist);

        // 重置
        if (Pressed(KeyReset())) ResetAll();
    }

    void Place(WorkerController worker, BlockType block)
    {
        if (worker == null) return;

        // 等待阶段：这一块只用来开始这一班
        if (session != null && session.ConsumeStartPlacement(worker))
        {
            actionCount = 0;
            if (logToConsole) Debug.Log($"[Start] {block} → {worker.workerName} side: shift begins");
            return;
        }

        WorkerState before = worker.State;
        bool valid = worker.ReceiveBlock(block);
        actionCount++;

        if (logToConsole)
        {
            string result = valid ? "<color=#5DCAA5>VALID</color>" : "<color=#888780>No effect</color>";
            Debug.Log($"[#{actionCount}] {worker.workerName} ← {block}  ({before})  {result}   " +
                      $"fatigue {worker.Fatigue:0}  output {worker.Output:0.0}");
        }
    }

    void ResetAll()
    {
        if (session != null) session.StartNewShift();
        else
        {
            if (workerLeft != null)  workerLeft.ResetWorker();
            if (workerRight != null) workerRight.ResetWorker();
        }
        actionCount = 0;
        if (logToConsole) Debug.Log("[Reset] Next Team Lead");
    }

    // ---------- 按键：兼容新旧两种输入系统，按下只算一次 ----------
#if ENABLE_INPUT_SYSTEM
    static bool Pressed(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
    static Key KeyLeftNotify()  => Key.Q;
    static Key KeyLeftBonus()   => Key.W;
    static Key KeyLeftAssist()  => Key.E;
    static Key KeyRightNotify() => Key.I;
    static Key KeyRightBonus()  => Key.O;
    static Key KeyRightAssist() => Key.P;
    static Key KeyReset()       => Key.R;
#else
    static bool Pressed(KeyCode k) => Input.GetKeyDown(k);
    static KeyCode KeyLeftNotify()  => KeyCode.Q;
    static KeyCode KeyLeftBonus()   => KeyCode.W;
    static KeyCode KeyLeftAssist()  => KeyCode.E;
    static KeyCode KeyRightNotify() => KeyCode.I;
    static KeyCode KeyRightBonus()  => KeyCode.O;
    static KeyCode KeyRightAssist() => KeyCode.P;
    static KeyCode KeyReset()       => KeyCode.R;
#endif
}
