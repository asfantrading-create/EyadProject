using UnityEngine;

/// <summary>
/// بيشغّل شيدر GlowPulse (Shader Graph):
///   1) Idle: بدون توهج
///   2) Ramp-up: التوهج بيطلع من 0 لـ Max Ramp Intensity بـ smoothstep
///   3) Pulse: نبض بين Pulse Min و Pulse Max للأبد
///
/// الشيدر بيحسب كل شي من (Time - _StartTime)، هاد السكربت بس بيكتب _StartTime.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class GlowPulseController : MonoBehaviour
{
    public enum ApplyMode
    {
        // نسخة خاصة من المتريال لكل Renderer (renderer.materials) — بتضل متوافقة مع SRP Batcher
        InstancedMaterial,
        // MaterialPropertyBlock — بدون نسخ متريال، بس الـ Renderer بيطلع من SRP Batcher
        PropertyBlock
    }

    // أي وقت بداية بعيد بالمستقبل بيخلي (Time - _StartTime) سالب => التوهج = 0
    public const float IdleStartTime = 1e9f;

    static readonly int StartTimeId = Shader.PropertyToID("_StartTime");

    [Tooltip("يبلّش التوهج تلقائياً أول ما يشتغل الأوبجكت")]
    public bool playOnStart = false;

    [Tooltip("InstancedMaterial: نسخة متريال لكل أوبجكت. PropertyBlock: بدون نسخ.")]
    public ApplyMode applyMode = ApplyMode.InstancedMaterial;

    Renderer _renderer;
    MaterialPropertyBlock _block;
    Material[] _instances;
    float _startTime = IdleStartTime;

    public bool IsGlowing => _startTime < IdleStartTime;

    // الوقت من بداية التوهج (ثواني)، 0 إذا مش شغّال
    public float Elapsed => IsGlowing ? Time.timeSinceLevelLoad - _startTime : 0f;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();

        if (applyMode == ApplyMode.InstancedMaterial)
            _instances = _renderer.materials; // بتعمل نسخ، بنمسحها بـ OnDestroy
        else
            _block = new MaterialPropertyBlock();

        // نبلّش دايماً من Idle مهما كانت القيمة المحفوظة بالمتريال
        Apply(IdleStartTime);
    }

    void Start()
    {
        if (playOnStart)
            StartGlow();
    }

    /// <summary>يبلّش (أو يعيد) التأثير من أول الـ Ramp-up.</summary>
    [ContextMenu("Start Glow")]
    public void StartGlow()
    {
        // _Time.y بالشيدر = Time.timeSinceLevelLoad، لازم نستعمل نفس الساعة
        Apply(Time.timeSinceLevelLoad);
    }

    /// <summary>يرجّع الأوبجكت لشكله الطبيعي بدون توهج.</summary>
    [ContextMenu("Reset Glow")]
    public void ResetGlow()
    {
        Apply(IdleStartTime);
    }

    void Apply(float startTime)
    {
        _startTime = startTime;
        if (_renderer == null)
            return; // ContextMenu بوضع الـ Edit قبل Awake

        if (applyMode == ApplyMode.InstancedMaterial)
        {
            // كل المتريالات اللي عندها _StartTime (مثلاً متريال الـ Outline كمان)
            foreach (var mat in _instances)
            {
                if (mat != null && mat.HasProperty(StartTimeId))
                    mat.SetFloat(StartTimeId, startTime);
            }
        }
        else
        {
            // الـ Block بيتطبق على كل الـ submeshes/المتريالات تبع الـ Renderer
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(StartTimeId, startTime);
            _renderer.SetPropertyBlock(_block);
        }
    }

    void OnDestroy()
    {
        if (_instances == null)
            return;

        foreach (var mat in _instances)
        {
            if (mat != null)
                Destroy(mat);
        }
    }
}
