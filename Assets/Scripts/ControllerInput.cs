using System.Collections;
using UnityEngine;

/// <summary>
/// 头显里用手柄模拟放置木块，相当于头显版的 KeyboardInput。
/// 和键盘一样只负责"哪一边放了哪块"，其他脚本不用改。
///
///                    NOTIFY   BONUS   ASSIST
/// 左手柄 → 左边小人      X        Y      扳机
/// 右手柄 → 右边小人      A        B      扳机
/// 按下右摇杆 = 开始新的一班
///
/// 挂在 Input 物体上（和 KeyboardInput 并存，互不影响）。
/// 需要场景里有 Camera Rig（OVRManager 负责更新手柄状态）。
/// </summary>
public class ControllerInput : MonoBehaviour
{
    [Tooltip("左边小人（与 SessionManager 的 Worker Left 相同）")]
    public WorkerController workerLeft;
    [Tooltip("右边小人（与 SessionManager 的 Worker Right 相同）")]
    public WorkerController workerRight;
    [Tooltip("可选：拖入后右摇杆按下会开始新的一班")]
    public SessionManager session;

    [Header("震动反馈")]
    public bool haptics = true;
    [Range(0f, 1f)] public float validStrength = 0.6f;
    [Range(0f, 1f)] public float invalidStrength = 0.2f;
    public float hapticDuration = 0.08f;

    [Tooltip("在 Console / 调试面板打印每次操作")]
    public bool log = true;

    int actionCount;

    void Update()
    {
        // 左手柄 → 左边小人
        if (OVRInput.GetDown(OVRInput.RawButton.X))             Place(workerLeft,  BlockType.Notify, OVRInput.Controller.LTouch);
        if (OVRInput.GetDown(OVRInput.RawButton.Y))             Place(workerLeft,  BlockType.Bonus,  OVRInput.Controller.LTouch);
        if (OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger)) Place(workerLeft,  BlockType.Assist, OVRInput.Controller.LTouch);

        // 右手柄 → 右边小人
        if (OVRInput.GetDown(OVRInput.RawButton.A))             Place(workerRight, BlockType.Notify, OVRInput.Controller.RTouch);
        if (OVRInput.GetDown(OVRInput.RawButton.B))             Place(workerRight, BlockType.Bonus,  OVRInput.Controller.RTouch);
        if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger)) Place(workerRight, BlockType.Assist, OVRInput.Controller.RTouch);

        // 新的一班
        if (OVRInput.GetDown(OVRInput.RawButton.RThumbstick))
        {
            if (session != null) session.StartNewShift();
            else
            {
                if (workerLeft != null)  workerLeft.ResetWorker();
                if (workerRight != null) workerRight.ResetWorker();
            }
            actionCount = 0;
            if (log) Debug.Log("[Controller] New shift");
        }
    }

    void Place(WorkerController worker, BlockType block, OVRInput.Controller hand)
    {
        if (worker == null) return;

        WorkerState before = worker.State;
        bool valid = worker.ReceiveBlock(block);
        actionCount++;

        if (log)
        {
            string result = valid ? "<color=#5DCAA5>VALID</color>" : "<color=#888780>No effect</color>";
            Debug.Log($"[Controller #{actionCount}] {worker.workerName} ← {block} ({before}) {result}");
        }

        if (haptics) StartCoroutine(Buzz(hand, valid ? validStrength : invalidStrength));
    }

    IEnumerator Buzz(OVRInput.Controller hand, float strength)
    {
        OVRInput.SetControllerVibration(1f, strength, hand);
        yield return new WaitForSeconds(hapticDuration);
        OVRInput.SetControllerVibration(0f, 0f, hand);
    }
}
