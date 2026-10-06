using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全部音效（第一批：木块交互 + 系统提示）。
/// 只"听"其他脚本的事件，自己不改任何逻辑；没拖音效的槽位直接跳过，不会报错。
///
/// 两个声音世界：
///   系统的声音（提示、木块确认）= 精致、清楚的 UI 音效
///   小人的声音（工作、倒下）   = 第二批再做，很轻的真实声音
///
/// 声音从发生的位置传来（3D）：木块声在小人那边，提示声在提示条那里。
/// 没处理的任务会跟着闪烁重复提醒，拖得越久越频繁；同一时间只有最急的那条出声。
///
/// 挂在 Session 上。
/// </summary>
public class SoundFX : MonoBehaviour
{
    [System.Serializable]
    public class Sound
    {
        [Tooltip("放多个会随机挑一个")]
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("随机音高幅度，避免机械重复")]
        [Range(0f, 0.3f)] public float pitchJitter = 0.04f;

        public bool HasClip => clips != null && clips.Length > 0;
    }

    [Header("引用（不填会自动从场景里找）")]
    public SessionManager session;
    public FloatingPrompts prompts;
    public FeedbackFX feedback;

    [Header("总体")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Tooltip("0 = 普通立体声，1 = 从发声的位置传来（3D）")]
    [Range(0f, 1f)] public float spatialBlend = 1f;
    [Tooltip("这个距离以内音量不衰减（米）。观众离箱子只有半米，所以默认整个装置内音量一致，只保留左右方向感")]
    public float minDistance = 1f;
    public float maxDistance = 10f;
    [Tooltip("同时最多播放几个声音，超出时打断最早的")]
    public int maxVoices = 12;
    [Tooltip("同一种声音的最短间隔（秒），防止刷屏")]
    public float sameSoundCooldown = 0.08f;

    [Header("木块交互")]
    public Sound grab;
    public Sound placeNotify;
    public Sound placeBonus;
    public Sound placeAssist;
    [Tooltip("放错木块：不惩罚只记录，声音要闷、不刺耳")]
    public Sound placeInvalid;
    [Tooltip("松手后残影消失（可留空）")]
    public Sound vanish;
    [Tooltip("原位长出新木块（可留空）")]
    public Sound respawn;
    [Tooltip("等待阶段放入木块、开始这一班的那一刻")]
    public Sound shiftStart;

    [Header("系统提示：出现（按 Kind = 系统的情绪）")]
    [Tooltip("警报：短促的提醒音")]
    public Sound promptNotify;
    [Tooltip("夸奖 / 配额达标：最甜的奖励音")]
    public Sound promptBonus;
    [Tooltip("教学：柔和的提示音")]
    public Sound promptAssist;

    [Header("系统提示：结束")]
    [Tooltip("照做了 / 奖励到时")]
    public Sound promptDone;
    [Tooltip("没照做、变灰掉落")]
    public Sound promptMissed;
    [Tooltip("反转后图标飞进 YOU 面板：很轻的记录 / 盖章声")]
    public Sound recordedToYou;

    [Header("没处理的任务：重复提醒")]
    public bool remind = true;
    [Tooltip("任务刚出现时两次提醒的间隔（秒）。拖得越久，间隔按闪烁加速的倍数缩短（最多快 3 倍）。0 = 每次闪烁都响")]
    public float remindInterval = 3f;
    [Tooltip("提醒音相对出现音的音量")]
    [Range(0f, 1f)] public float remindVolume = 0.7f;
    [Tooltip("每条任务最多提醒几次，0 = 不限")]
    public int maxReminders = 0;

    readonly List<AudioSource> voices = new List<AudioSource>();
    readonly Dictionary<Sound, float> lastPlayed = new Dictionary<Sound, float>();
    readonly Dictionary<PromptMessage, int> reminderCount = new Dictionary<PromptMessage, int>();
    float lastReminder = -999f;
    WorkerController[] workers;

    // ================= 启动 / 订阅 =================

    void Start()
    {
        if (session == null)  session  = FindFirstObjectByType<SessionManager>();
        if (prompts == null)  prompts  = FindFirstObjectByType<FloatingPrompts>();
        if (feedback == null) feedback = FindFirstObjectByType<FeedbackFX>();

        for (int i = 0; i < Mathf.Max(1, maxVoices); i++)
        {
            var go = new GameObject($"Voice{i}");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            voices.Add(src);
        }

        if (session != null)
        {
            workers = new[] { session.workerLeft, session.workerRight };
            foreach (var w in workers) if (w != null) w.BlockReceived += OnBlock;
            session.ShiftStartPlaced += OnShiftStart;
        }
        if (prompts != null)
        {
            prompts.ToastShown += OnShown;
            prompts.ToastResolved += OnResolved;
            prompts.ToastVoided += OnVoided;
            prompts.TaskPulse += OnPulse;
        }
        if (feedback != null) feedback.ArrivedAtYou += OnArrivedAtYou;

        VirtualBlock.Grabbed += OnGrabbed;
        VirtualBlock.Dropped += OnDropped;
        VirtualBlock.Respawned += OnRespawned;
    }

    void OnDestroy()
    {
        if (workers != null) foreach (var w in workers) if (w != null) w.BlockReceived -= OnBlock;
        if (session != null) session.ShiftStartPlaced -= OnShiftStart;
        if (prompts != null)
        {
            prompts.ToastShown -= OnShown;
            prompts.ToastResolved -= OnResolved;
            prompts.ToastVoided -= OnVoided;
            prompts.TaskPulse -= OnPulse;
        }
        if (feedback != null) feedback.ArrivedAtYou -= OnArrivedAtYou;

        VirtualBlock.Grabbed -= OnGrabbed;
        VirtualBlock.Dropped -= OnDropped;
        VirtualBlock.Respawned -= OnRespawned;
    }

    void Update()
    {
        // 一班结束后清掉提醒计数
        if (session != null && session.Phase != ShiftPhase.Running && reminderCount.Count > 0)
            reminderCount.Clear();
    }

    // ================= 事件 =================

    void OnBlock(WorkerController w, BlockType block, bool valid)
    {
        Play(valid ? PlaceSound(block) : placeInvalid, w.transform.position);
    }

    void OnShiftStart(WorkerController side) => Play(shiftStart, side != null ? side.transform.position : transform.position);

    void OnGrabbed(VirtualBlock b)   => Play(grab, b.transform.position);
    void OnDropped(VirtualBlock b)   => Play(vanish, b.transform.position);
    void OnRespawned(VirtualBlock b) => Play(respawn, b.transform.position);

    void OnShown(PromptMessage m, Vector3 pos)
    {
        Play(PromptSound(m.Kind), pos);
        if (m.IsTask) lastReminder = Time.time;   // 刚响过出现音，提醒从这里开始计时
    }

    void OnResolved(PromptMessage m, Vector3 pos)
    {
        reminderCount.Remove(m);
        Play(promptDone, pos);
    }

    void OnVoided(PromptMessage m, Vector3 pos)
    {
        reminderCount.Remove(m);
        Play(promptMissed, pos);
    }

    void OnArrivedAtYou(BlockType kind, Vector3 pos) => Play(recordedToYou, pos);

    /// <summary>最急的任务每次闪到最亮时调用；间隔到了就响一次</summary>
    void OnPulse(PromptMessage m, Vector3 pos, float escalation)
    {
        if (!remind) return;
        if (session != null && session.Phase != ShiftPhase.Running) return;

        float interval = remindInterval / Mathf.Max(1f, escalation);
        if (Time.time - lastReminder < interval) return;

        reminderCount.TryGetValue(m, out int n);
        if (maxReminders > 0 && n >= maxReminders) return;
        reminderCount[m] = n + 1;

        lastReminder = Time.time;
        Play(PromptSound(m.Kind), pos, remindVolume, ignoreCooldown: true);
    }

    Sound PlaceSound(BlockType b) => b == BlockType.Notify ? placeNotify : b == BlockType.Bonus ? placeBonus : placeAssist;
    Sound PromptSound(BlockType b) => b == BlockType.Notify ? promptNotify : b == BlockType.Bonus ? promptBonus : promptAssist;

    // ================= 播放 =================

    void Play(Sound s, Vector3 pos, float volumeMul = 1f, bool ignoreCooldown = false)
    {
        if (s == null || !s.HasClip) return;

        if (!ignoreCooldown && lastPlayed.TryGetValue(s, out float last) && Time.time - last < sameSoundCooldown) return;
        lastPlayed[s] = Time.time;

        var clip = s.clips[Random.Range(0, s.clips.Length)];
        if (clip == null) return;

        var src = FreeVoice();
        src.transform.position = pos;
        src.clip = clip;
        src.volume = s.volume * volumeMul * masterVolume;
        src.pitch = 1f + Random.Range(-s.pitchJitter, s.pitchJitter);
        src.spatialBlend = spatialBlend;
        src.minDistance = minDistance;
        src.maxDistance = Mathf.Max(minDistance + 0.01f, maxDistance);
        src.Play();
    }

    /// <summary>空闲的声道；都在用时打断播放最久的那个</summary>
    AudioSource FreeVoice()
    {
        AudioSource oldest = voices[0];
        foreach (var v in voices)
        {
            if (!v.isPlaying) return v;
            if (v.time > oldest.time) oldest = v;
        }
        return oldest;
    }
}
