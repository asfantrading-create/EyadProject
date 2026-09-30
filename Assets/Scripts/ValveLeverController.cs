using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// فالف / ذراع (Lever) مبني على مكونات Meta Interaction SDK الجاهزة.
///
/// الدوران نفسه (المسك + الدوران حول الـ Pivot + الحد الأدنى والأقصى) بيعمله
/// OneGrabRotateTransformer. هالسكربت ما بيحرّك الذراع أبداً، هو بس:
///   1) بيضبط إعدادات الـ Transformer (المحور + Min = 0 + Max = maxAngle) من مكان واحد.
///   2) بيقرأ زاوية الذراع الحالية حول محور الـ Pivot.
///   3) لما توصل الزاوية للحد الأقصى بيشغّل أنيميشن "Rise" وبيطلق OnMaxReached.
///   4) لما ترجع تحت الحد الأقصى بيرجّع الأنيميشن وبيطلق OnReturnedFromMax.
///   5) بيسمع لأحداث الـ Grabbable (مسك / إفلات) وبيطلق OnGrabbed / OnReleased.
///
/// مكانه: على نفس الأوبجكت اللي عليه Grabbable و OneGrabRotateTransformer
/// (بمشروعك: "Bone.001 Grab").
/// </summary>
[DisallowMultipleComponent]
public class ValveLeverController : MonoBehaviour
{
    public enum LeverAxis { X, Y, Z }

    public enum RiseAnimationMode
    {
        /// <summary>Bool واحد: true عند الحد الأقصى، false لما يرجع.</summary>
        Bool,
        /// <summary>Trigger "Rise" عند الحد الأقصى، و Trigger "Lower" لما يرجع.</summary>
        Triggers,
        /// <summary>
        /// Float من 0 لـ 1 بيتحرك بسلاسة ومربوط بـ Motion Time للـ State.
        /// أحسن خيار للأنيميشن بالعكس: إذا رجعت الذراع بنص الطلعة، بيرجع من نفس النقطة.
        /// </summary>
        MotionTime
    }

    [Header("Meta Interaction SDK")]
    [Tooltip("الـ Grabbable تبع الذراع. إذا فاضي بيدوّر عليه على نفس الأوبجكت.")]
    [SerializeField] Grabbable grabbable;

    [Tooltip("الـ OneGrabRotateTransformer. إذا فاضي بيدوّر عليه على نفس الأوبجكت.")]
    [SerializeField] OneGrabRotateTransformer rotateTransformer;

    [Tooltip("نقطة الدوران عند القاعدة. لازم تكون ثابتة: مش ابن (child) للأوبجكت اللي بيدور.")]
    [SerializeField] Transform pivot;

    [Header("الدوران")]
    [Tooltip("محور الدوران بالنسبة لمحاور الـ Pivot (X = الأحمر، Y = الأخضر، Z = الأزرق)")]
    [SerializeField] LeverAxis rotationAxis = LeverAxis.X;

    [Tooltip("أقصى زاوية ميلان لقدام (درجات). الحد الأدنى دايماً 0.")]
    [SerializeField, Range(1f, 170f)] float maxAngle = 45f;

    [Tooltip("فعّلها إذا الذراع بتميل لورا بدل لقدام (اتجاه الزاوية سالب على هالمحور).")]
    [SerializeField] bool invertDirection = false;

    [Tooltip("إذا مفعّلة، السكربت بيكتب المحور والـ Min/Max والـ Pivot على الـ Transformer تلقائياً. " +
             "خليها مفعّلة عشان ما تضطر تعدّل القيم بمكانين.")]
    [SerializeField] bool driveTransformerSettings = true;

    [Header("تحديد الوصول للحد الأقصى")]
    [Tooltip("بيعتبر الذراع وصلت إذا صارت ضمن هالهامش من maxAngle (درجات).")]
    [SerializeField, Min(0f)] float reachTolerance = 2f;

    [Tooltip("لازم ترجع الذراع هالقد تحت maxAngle عشان يعتبرها رجعت (بيمنع الرجفة عند الحد).")]
    [SerializeField, Min(0f)] float releaseHysteresis = 5f;

    [Header("الأنيميشن")]
    [Tooltip("الـ Animator تبع الأوبجكت اللي بيطلع وينزل")]
    [SerializeField] Animator targetAnimator;

    [SerializeField] RiseAnimationMode animationMode = RiseAnimationMode.Bool;

    [Tooltip("Bool mode: اسم الـ Bool. Triggers mode: اسم Trigger الطلوع.")]
    [SerializeField] string riseParameter = "Rise";

    [Tooltip("Triggers mode فقط: اسم Trigger النزول.")]
    [SerializeField] string lowerParameter = "Lower";

    [Tooltip("MotionTime mode فقط: اسم الـ Float المربوط بـ Motion Time.")]
    [SerializeField] string progressParameter = "RiseProgress";

    [Tooltip("MotionTime mode فقط: مدة الطلوع/النزول الكاملة (ثواني).")]
    [SerializeField, Min(0.01f)] float riseDuration = 1f;

    [Header("Events")]
    public UnityEvent OnMaxReached = new UnityEvent();
    public UnityEvent OnReturnedFromMax = new UnityEvent();
    public UnityEvent OnGrabbed = new UnityEvent();
    public UnityEvent OnReleased = new UnityEvent();

    [Tooltip("بيتبعت كل ما تتغير الزاوية: 0 = الوضع الأصلي، 1 = الحد الأقصى")]
    public UnityEvent<float> OnNormalizedAngleChanged = new UnityEvent<float>();

    /// <summary>الزاوية الحالية بالدرجات (0 → maxAngle).</summary>
    public float CurrentAngle { get; private set; }

    /// <summary>الزاوية الحالية من 0 لـ 1.</summary>
    public float NormalizedAngle => maxAngle > 0f ? Mathf.Clamp01(CurrentAngle / maxAngle) : 0f;

    public bool IsAtMax { get; private set; }
    public bool IsGrabbed { get; private set; }

    Transform _target;              // الأوبجكت اللي بيدور فعلياً (Grabbable.Transform)
    Vector3 _referenceInTarget;     // اتجاه مرجعي عمودي على المحور، مخزّن بإحداثيات الـ target
    Quaternion _fallbackLocalRotation;
    bool _pivotIsValid;
    float _lastNormalized = -1f;
    float _riseProgress;
    bool _riseParamOk, _lowerParamOk, _progressParamOk;

    void Reset()
    {
        grabbable = GetComponent<Grabbable>();
        rotateTransformer = GetComponent<OneGrabRotateTransformer>();
    }

    void Awake()
    {
        if (grabbable == null) grabbable = GetComponent<Grabbable>();
        if (rotateTransformer == null) rotateTransformer = GetComponent<OneGrabRotateTransformer>();

        if (grabbable == null)
        {
            Debug.LogError($"[{nameof(ValveLeverController)}] ما في Grabbable على {name}.", this);
            enabled = false;
            return;
        }

        if (driveTransformerSettings) ApplyTransformerSettings();

        CacheAnimatorParameters();
    }

    void Start()
    {
        // الأوبجكت اللي بيدور: Target Transform تبع الـ Grabbable، أو نفس أوبجكت الـ Grabbable
        _target = grabbable.Transform != null ? grabbable.Transform : grabbable.transform;

        // إذا ما في Pivot مربوط، منستخدم نفس الـ Pivot اللي بيستخدمه الـ Transformer
        if (pivot == null && rotateTransformer != null) pivot = rotateTransformer.Pivot;

        // الـ Pivot لازم يكون ثابت. إذا كان هو نفس الـ target أو ابن إله، بيلف معه.
        _pivotIsValid = pivot != null && !pivot.IsChildOf(_target);
        if (pivot != null && pivot != _target && !_pivotIsValid)
        {
            Debug.LogWarning($"[{nameof(ValveLeverController)}] الـ Pivot ({pivot.name}) ابن للأوبجكت اللي بيدور. " +
                             "اعمله أخ (sibling) مش ابن، وإلا الدوران رح يتضاعف. منستخدم الوضعية الأصلية بدلاً منه.", this);
        }

        // وضعية احتياطية للمحاور: دوران الـ target الأصلي
        _fallbackLocalRotation = _target.localRotation;

        // اتجاه مرجعي عمودي على المحور: منخزّنه بإحداثيات الـ target عشان يلف معه
        Quaternion pivotRot = PivotRotation;
        _referenceInTarget = Quaternion.Inverse(_target.rotation) * (pivotRot * PerpendicularLocal());

        grabbable.WhenPointerEventRaised += HandlePointerEvent;

        // حالة البداية
        UpdateAngle();
        ApplyAnimatorImmediate();
    }

    void OnDestroy()
    {
        if (grabbable != null) grabbable.WhenPointerEventRaised -= HandlePointerEvent;
    }

    void Update()
    {
        if (_target == null) return;

        UpdateAngle();
        UpdateMaxState();
        UpdateMotionTime();
    }

    // ───────────────────────── الزاوية ─────────────────────────

    // محاور الـ Pivot بالعالم. بدون Pivot صالح: دوران الـ target الأصلي بالنسبة لأبوه
    // (نفس اللي بيعمله OneGrabRotateTransformer لما ما يكون في Pivot)
    Quaternion PivotRotation =>
        _pivotIsValid ? pivot.rotation
        : (_target.parent != null ? _target.parent.rotation * _fallbackLocalRotation : _fallbackLocalRotation);

    Vector3 AxisLocal()
    {
        Vector3 v = Vector3.zero;
        v[(int)rotationAxis] = 1f;
        return v;
    }

    // المحور اللي بعده (نفس فكرة الـ Transformer): X→Y, Y→Z, Z→X
    Vector3 PerpendicularLocal()
    {
        Vector3 v = Vector3.zero;
        v[((int)rotationAxis + 1) % 3] = 1f;
        return v;
    }

    void UpdateAngle()
    {
        Quaternion pivotRot = PivotRotation;
        Vector3 axis = AxisLocal();
        Vector3 reference = PerpendicularLocal();

        // الاتجاه المرجعي الحالي بإحداثيات الـ Pivot، مسقط على مستوى الدوران
        Vector3 current = Quaternion.Inverse(pivotRot) * (_target.rotation * _referenceInTarget);
        current = Vector3.ProjectOnPlane(current, axis);

        // نفس اتجاه الإشارة اللي بيستخدمه OneGrabRotateTransformer (Quaternion.AngleAxis)
        float signed = Vector3.SignedAngle(reference, current, axis);
        if (invertDirection) signed = -signed;

        CurrentAngle = Mathf.Clamp(signed, 0f, maxAngle);

        float normalized = NormalizedAngle;
        if (!Mathf.Approximately(normalized, _lastNormalized))
        {
            _lastNormalized = normalized;
            OnNormalizedAngleChanged.Invoke(normalized);
        }
    }

    void UpdateMaxState()
    {
        if (!IsAtMax && CurrentAngle >= maxAngle - reachTolerance)
        {
            IsAtMax = true;
            SetAnimatorRaised(true);
            OnMaxReached.Invoke();
        }
        else if (IsAtMax && CurrentAngle < maxAngle - Mathf.Max(releaseHysteresis, reachTolerance))
        {
            IsAtMax = false;
            SetAnimatorRaised(false);
            OnReturnedFromMax.Invoke();
        }
    }

    // ───────────────────────── Grabbable events ─────────────────────────

    void HandlePointerEvent(PointerEvent evt)
    {
        if (evt.Type != PointerEventType.Select &&
            evt.Type != PointerEventType.Unselect &&
            evt.Type != PointerEventType.Cancel)
        {
            return;
        }

        // SelectingPointsCount بيكون متحدّث قبل ما ينبعت الحدث
        bool grabbedNow = grabbable.SelectingPointsCount > 0;
        if (grabbedNow == IsGrabbed) return;

        IsGrabbed = grabbedNow;
        if (IsGrabbed) OnGrabbed.Invoke();
        else OnReleased.Invoke();
    }

    // ───────────────────────── Animator ─────────────────────────

    void CacheAnimatorParameters()
    {
        _riseParamOk = _lowerParamOk = _progressParamOk = false;
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null) return;

        foreach (AnimatorControllerParameter p in targetAnimator.parameters)
        {
            if (p.name == riseParameter &&
                (p.type == AnimatorControllerParameterType.Bool || p.type == AnimatorControllerParameterType.Trigger))
                _riseParamOk = true;
            if (p.name == lowerParameter && p.type == AnimatorControllerParameterType.Trigger)
                _lowerParamOk = true;
            if (p.name == progressParameter && p.type == AnimatorControllerParameterType.Float)
                _progressParamOk = true;
        }

        string missing =
            animationMode == RiseAnimationMode.Bool && !_riseParamOk ? $"Bool \"{riseParameter}\"" :
            animationMode == RiseAnimationMode.Triggers && !_riseParamOk ? $"Trigger \"{riseParameter}\"" :
            animationMode == RiseAnimationMode.Triggers && !_lowerParamOk ? $"Trigger \"{lowerParameter}\"" :
            animationMode == RiseAnimationMode.MotionTime && !_progressParamOk ? $"Float \"{progressParameter}\"" :
            null;

        if (missing != null)
        {
            Debug.LogWarning($"[{nameof(ValveLeverController)}] الـ Animator على {targetAnimator.name} ما فيه باراميتر {missing}.", this);
        }
    }

    void SetAnimatorRaised(bool raised)
    {
        if (targetAnimator == null) return;

        switch (animationMode)
        {
            case RiseAnimationMode.Bool:
                if (_riseParamOk) targetAnimator.SetBool(riseParameter, raised);
                break;

            case RiseAnimationMode.Triggers:
                if (raised)
                {
                    if (_lowerParamOk) targetAnimator.ResetTrigger(lowerParameter);
                    if (_riseParamOk) targetAnimator.SetTrigger(riseParameter);
                }
                else
                {
                    if (_riseParamOk) targetAnimator.ResetTrigger(riseParameter);
                    if (_lowerParamOk) targetAnimator.SetTrigger(lowerParameter);
                }
                break;

            // MotionTime بيتحدّث كل فريم بـ UpdateMotionTime
        }
    }

    void UpdateMotionTime()
    {
        if (animationMode != RiseAnimationMode.MotionTime || targetAnimator == null || !_progressParamOk) return;

        float goal = IsAtMax ? 1f : 0f;
        _riseProgress = Mathf.MoveTowards(_riseProgress, goal, Time.deltaTime / riseDuration);
        targetAnimator.SetFloat(progressParameter, _riseProgress);
    }

    void ApplyAnimatorImmediate()
    {
        if (targetAnimator == null) return;

        if (animationMode == RiseAnimationMode.Bool && _riseParamOk)
            targetAnimator.SetBool(riseParameter, IsAtMax);

        if (animationMode == RiseAnimationMode.MotionTime && _progressParamOk)
        {
            _riseProgress = IsAtMax ? 1f : 0f;
            targetAnimator.SetFloat(progressParameter, _riseProgress);
        }
    }

    // ───────────────────────── Transformer settings ─────────────────────────

    OneGrabRotateTransformer.Axis ToIsdkAxis(LeverAxis a) =>
        a == LeverAxis.X ? OneGrabRotateTransformer.Axis.Right :
        a == LeverAxis.Y ? OneGrabRotateTransformer.Axis.Up :
                           OneGrabRotateTransformer.Axis.Forward;

    /// <summary>
    /// بيكتب المحور والحدود والـ Pivot على الـ OneGrabRotateTransformer.
    /// الزوايا بالـ Transformer نسبية للوضعية الأصلية وقت بداية المشهد.
    /// </summary>
    void ApplyTransformerSettings()
    {
        if (rotateTransformer == null) return;

        rotateTransformer.InjectOptionalRotationAxis(ToIsdkAxis(rotationAxis));
        if (pivot != null) rotateTransformer.InjectOptionalPivotTransform(pivot);

        rotateTransformer.Constraints = new OneGrabRotateTransformer.OneGrabRotateConstraints
        {
            MinAngle = new FloatConstraint { Constrain = true, Value = invertDirection ? -maxAngle : 0f },
            MaxAngle = new FloatConstraint { Constrain = true, Value = invertDirection ? 0f : maxAngle }
        };
    }

#if UNITY_EDITOR
    // بيخلي قيم الـ Transformer بالـ Inspector متطابقة مع هالسكربت وقت التعديل
    void OnValidate()
    {
        if (rotateTransformer == null) rotateTransformer = GetComponent<OneGrabRotateTransformer>();
        if (!driveTransformerSettings || rotateTransformer == null) return;

        ApplyTransformerSettings();
        UnityEditor.EditorUtility.SetDirty(rotateTransformer);
    }

    // بيرسم المحور والقوس (0 → maxAngle) بالـ Scene view
    void OnDrawGizmosSelected()
    {
        Transform p = pivot != null ? pivot : (rotateTransformer != null ? rotateTransformer.Pivot : null);
        if (p == null) return;

        Vector3 axis = p.rotation * AxisLocal();
        Vector3 reference = p.rotation * PerpendicularLocal();
        float radius = 0.1f;
        float sign = invertDirection ? -1f : 1f;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(p.position - axis * radius, p.position + axis * radius);

        UnityEditor.Handles.color = new Color(1f, 0.6f, 0f, 0.25f);
        UnityEditor.Handles.DrawSolidArc(p.position, axis * sign, reference, maxAngle, radius);
    }
#endif
}
