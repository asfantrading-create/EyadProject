using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// أنيميشن عرض الدبابة مبني على "خط زمني" (Timeline) واحد.
/// كل وضعية للدبابة (دوران + مكان + حجم) محسوبة من الوقت فقط،
/// لهيك الـ Slider و +5 / -5 بيقدروا يروحوا لأي لحظة بالزبط.
///
/// المراحل:
///   1) لف على Y: 0 → 90 (وقفة) → 180 (وقفة) → 270 (وقفة) → 360
///   2) تقرّب من وجه اللاعب مع تصغير الحجم من 0.9 إلى 0.1
///   3) عرض من كل الجهات: جنب، خلف، الجنب الثاني، قدام، فوق، تحت
/// </summary>
public class TankShowcaseController : MonoBehaviour
{
    [Serializable]
    public class InspectionStep
    {
        public string name = "Step";
        [Tooltip("الدوران بالنسبة لوضعية الدبابة الأصلية (درجات)")]
        public Vector3 eulerAngles;
        [Tooltip("وقت الانتقال لهاي الوضعية (ثواني)")]
        public float moveDuration = 1.5f;
        [Tooltip("وقت الوقفة بعد ما توصل (ثواني)")]
        public float holdDuration = 1f;
    }

    // وضعية واحدة على الخط الزمني
    struct Key
    {
        public float time;        // وقت الوصول لهاي الوضعية
        public Vector3 euler;     // الدوران بالنسبة للدوران الأصلي
        public float posBlend;    // 0 = مكان البداية، 1 = نقطة اللاعب
        public float scale;
    }

    [Header("الدبابة")]
    [Tooltip("إذا فاضي بيستخدم نفس الأوبجكت اللي عليه السكربت")]
    [SerializeField] Transform tank;

    [Header("المرحلة 1: اللف على Y")]
    [Tooltip("وقت كل ربع لفة (90 درجة)")]
    [SerializeField] float quarterTurnDuration = 1.5f;
    [SerializeField] float holdAt90 = 1f;
    [SerializeField] float holdAt180 = 1f;
    [SerializeField] float holdAt270 = 1f;
    [Tooltip("وقفة بعد ما يرجع وجهها زي ما كان (360)")]
    [SerializeField] float holdAt360 = 0.5f;

    [Header("المرحلة 2: التقرّب والتصغير")]
    [SerializeField] float startScale = 0.9f;
    [SerializeField] float endScale = 0.1f;
    [SerializeField] float approachDuration = 3f;
    [SerializeField] float holdAfterApproach = 0.5f;

    [Header("نقطة الوصول عند اللاعب")]
    [Tooltip("إذا حطيت Transform هون الدبابة بتوصل عليه بالزبط (أولوية)")]
    [SerializeField] Transform approachTarget;
    [Tooltip("كاميرا اللاعب (إذا فاضي بيستخدم Camera.main)")]
    [SerializeField] Transform playerCamera;
    [Tooltip("مكان الوصول بالنسبة للكاميرا: X يمين، Y فوق، Z قدام (متر)")]
    [SerializeField] Vector3 offsetFromPlayer = new Vector3(0f, -0.1f, 0.8f);
    [Tooltip("إذا مفعّل الدبابة بتلحق اللاعب لو تحرك، إذا لا بتثبت النقطة لحظة Play/Replay")]
    [SerializeField] bool followPlayerLive = false;

    [Header("المرحلة 3: العرض من كل الجهات")]
    [SerializeField] List<InspectionStep> inspectionSteps = new List<InspectionStep>
    {
        new InspectionStep { name = "Right Side", eulerAngles = new Vector3(0, 90, 0) },
        new InspectionStep { name = "Back",       eulerAngles = new Vector3(0, 180, 0) },
        new InspectionStep { name = "Left Side",  eulerAngles = new Vector3(0, 270, 0) },
        new InspectionStep { name = "Front",      eulerAngles = new Vector3(0, 360, 0) },
        new InspectionStep { name = "Top",        eulerAngles = new Vector3(80, 360, 0) },
        new InspectionStep { name = "Bottom",     eulerAngles = new Vector3(-80, 360, 0) },
        new InspectionStep { name = "Front",      eulerAngles = new Vector3(0, 360, 0) },
    };

    [Header("الحركة")]
    [Tooltip("حركة ناعمة (تبطّئ بالبداية والنهاية)")]
    [SerializeField] bool smoothEasing = true;
    [SerializeField] bool playOnStart = true;

    [Header("الكبسات (UI Toggle)")]
    [SerializeField] Toggle playToggle;
    [SerializeField] Toggle pauseToggle;
    [SerializeField] Toggle forward5Toggle;
    [SerializeField] Toggle back5Toggle;
    [SerializeField] Toggle replayToggle;
    [SerializeField] float skipSeconds = 5f;

    [Header("السلايدر ووقت العرض (اختياري)")]
    [SerializeField] Slider timelineSlider;
    [SerializeField] TMP_Text timeLabel;

    readonly List<Key> keys = new List<Key>();
    Vector3 startPosition;
    Quaternion baseRotation;
    Vector3 lockedTargetPosition;
    float currentTime;
    bool isPlaying;

    public float Duration { get; private set; }
    public float CurrentTime => currentTime;
    public bool IsPlaying => isPlaying;

    void Awake()
    {
        if (tank == null) tank = transform;
        if (playerCamera == null && Camera.main != null) playerCamera = Camera.main.transform;

        startPosition = tank.position;
        baseRotation = tank.rotation;

        BuildTimeline();
        HookUI();
    }

    void Start()
    {
        LockTarget();
        SetTime(0f);
        if (playOnStart) Play(); else Pause();
    }

    void OnDestroy()
    {
        if (playToggle) playToggle.onValueChanged.RemoveListener(OnPlayToggle);
        if (pauseToggle) pauseToggle.onValueChanged.RemoveListener(OnPauseToggle);
        if (forward5Toggle) forward5Toggle.onValueChanged.RemoveListener(OnForward5Toggle);
        if (back5Toggle) back5Toggle.onValueChanged.RemoveListener(OnBack5Toggle);
        if (replayToggle) replayToggle.onValueChanged.RemoveListener(OnReplayToggle);
        if (timelineSlider) timelineSlider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    void OnValidate()
    {
        // لو غيرت الأرقام من الـ Inspector وقت التشغيل، الخط الزمني بيتحدّث فوراً
        if (Application.isPlaying && keys.Count > 0)
        {
            BuildTimeline();
            SetTime(currentTime);
        }
    }

    void Update()
    {
        if (!isPlaying) return;

        float next = currentTime + Time.deltaTime;
        if (next >= Duration)
        {
            next = Duration;
            isPlaying = false;
            RefreshButtons();
        }
        SetTime(next);
    }

    // ───────────────────────── الكبسات ─────────────────────────

    public void Play()
    {
        // إذا خلص الأنيميشن و كبس Play بيعيد من الأول
        if (currentTime >= Duration) { LockTarget(); SetTime(0f); }
        isPlaying = true;
        RefreshButtons();
    }

    public void Pause()
    {
        isPlaying = false;
        RefreshButtons();
    }

    public void TogglePlayPause()
    {
        if (isPlaying) Pause(); else Play();
    }

    public void Forward5() => SetTime(currentTime + skipSeconds);

    public void Back5() => SetTime(currentTime - skipSeconds);

    public void Replay()
    {
        LockTarget();
        SetTime(0f);
        isPlaying = true;
        RefreshButtons();
    }

    void OnSliderChanged(float value) => SetTime(value);

    // ───────────────────────── الخط الزمني ─────────────────────────

    /// <summary>روح لأي ثانية بالأنيميشن وطبّق وضعية الدبابة عليها.</summary>
    public void SetTime(float t)
    {
        currentTime = Mathf.Clamp(t, 0f, Duration);
        ApplyPose(currentTime);

        if (timelineSlider) timelineSlider.SetValueWithoutNotify(currentTime);
        if (timeLabel) timeLabel.text = $"{FormatTime(currentTime)} / {FormatTime(Duration)}";
    }

    void BuildTimeline()
    {
        keys.Clear();
        float t = 0f;
        var euler = Vector3.zero;
        float blend = 0f;
        float scale = startScale;

        void Add(float moveTime, float holdTime)
        {
            t += Mathf.Max(0f, moveTime);
            keys.Add(new Key { time = t, euler = euler, posBlend = blend, scale = scale });
            if (holdTime > 0f)
            {
                t += holdTime;
                keys.Add(new Key { time = t, euler = euler, posBlend = blend, scale = scale });
            }
        }

        // البداية
        Add(0f, 0f);

        // المرحلة 1: لف على Y بأربع أرباع
        euler.y = 90f;  Add(quarterTurnDuration, holdAt90);
        euler.y = 180f; Add(quarterTurnDuration, holdAt180);
        euler.y = 270f; Add(quarterTurnDuration, holdAt270);
        euler.y = 360f; Add(quarterTurnDuration, holdAt360);

        // المرحلة 2: تقرّب من اللاعب وتصغير
        blend = 1f;
        scale = endScale;
        Add(approachDuration, holdAfterApproach);

        // المرحلة 3: العرض من كل الجهات
        // (نضيف 360 على Y عشان تكمل من نفس الزاوية بدون ما ترجع لفة لورا)
        foreach (var step in inspectionSteps)
        {
            euler = new Vector3(step.eulerAngles.x, step.eulerAngles.y + 360f, step.eulerAngles.z);
            Add(step.moveDuration, step.holdDuration);
        }

        Duration = t;

        if (timelineSlider)
        {
            timelineSlider.minValue = 0f;
            timelineSlider.maxValue = Mathf.Max(Duration, 0.0001f);
            timelineSlider.wholeNumbers = false;
        }
    }

    void ApplyPose(float t)
    {
        if (keys.Count == 0) return;

        // لاقي المقطع اللي فيه الوقت الحالي
        Key a = keys[0], b = keys[0];
        for (int i = 1; i < keys.Count; i++)
        {
            if (t <= keys[i].time) { a = keys[i - 1]; b = keys[i]; break; }
            a = b = keys[i];
        }

        float span = b.time - a.time;
        float u = span > 0f ? Mathf.Clamp01((t - a.time) / span) : 1f;
        if (smoothEasing) u = Mathf.SmoothStep(0f, 1f, u);

        Vector3 euler = Vector3.Lerp(a.euler, b.euler, u);
        float blend = Mathf.Lerp(a.posBlend, b.posBlend, u);
        float scale = Mathf.Lerp(a.scale, b.scale, u);

        Vector3 target = followPlayerLive ? GetTargetPosition() : lockedTargetPosition;

        tank.SetPositionAndRotation(
            Vector3.Lerp(startPosition, target, blend),
            baseRotation * Quaternion.Euler(euler));
        tank.localScale = Vector3.one * scale;
    }

    void LockTarget() => lockedTargetPosition = GetTargetPosition();

    Vector3 GetTargetPosition()
    {
        if (approachTarget) return approachTarget.position;
        if (playerCamera) return playerCamera.TransformPoint(offsetFromPlayer);
        return startPosition;
    }

    // ───────────────────────── UI ─────────────────────────

    void HookUI()
    {
        if (playToggle) playToggle.onValueChanged.AddListener(OnPlayToggle);
        if (pauseToggle) pauseToggle.onValueChanged.AddListener(OnPauseToggle);
        if (forward5Toggle) forward5Toggle.onValueChanged.AddListener(OnForward5Toggle);
        if (back5Toggle) back5Toggle.onValueChanged.AddListener(OnBack5Toggle);
        if (replayToggle) replayToggle.onValueChanged.AddListener(OnReplayToggle);
        if (timelineSlider) timelineSlider.onValueChanged.AddListener(OnSliderChanged);
    }

    // الـ Toggle بيقلب قيمته مع كل كبسة، فأي تغيير بالقيمة = كبسة.
    // بعدين إحنا بنرجّع قيمة isOn بدون ما نطلق الحدث، عشان تعكس الحالة الحقيقية.
    void OnPlayToggle(bool _) => Play();
    void OnPauseToggle(bool _) => Pause();

    void OnForward5Toggle(bool _)
    {
        Forward5();
        forward5Toggle.SetIsOnWithoutNotify(false);
    }

    void OnBack5Toggle(bool _)
    {
        Back5();
        back5Toggle.SetIsOnWithoutNotify(false);
    }

    void OnReplayToggle(bool _)
    {
        Replay();
        replayToggle.SetIsOnWithoutNotify(false);
    }

    void RefreshButtons()
    {
        // Play و Pause: وحدة بتكون On والثانية Off حسب حالة التشغيل
        if (playToggle) playToggle.SetIsOnWithoutNotify(isPlaying);
        if (pauseToggle) pauseToggle.SetIsOnWithoutNotify(!isPlaying);
    }

    static string FormatTime(float seconds)
    {
        int s = Mathf.FloorToInt(seconds);
        return $"{s / 60:00}:{s % 60:00}";
    }
}
