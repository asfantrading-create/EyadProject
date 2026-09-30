using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// بيشغّل الـ Outline المتوهج (GlowPulseOutline):
///   1) Idle: بدون توهج
///   2) Ramp-up: التوهج بيطلع من 0 لـ Max Ramp Intensity بـ smoothstep
///   3) Pulse: نبض بين Pulse Min و Pulse Max للأبد
///
/// الشيدر بيحسب كل شي من (Time - _StartTime)، هاد السكربت بس بيكتب _StartTime
/// على كل متريال عنده _StartTime (متريال الـ Outline)، ومتريال الجسم ما بيتأثر.
/// </summary>
[DisallowMultipleComponent]
public class GlowPulseController : MonoBehaviour
{
    public enum ApplyMode
    {
        // نسخة خاصة من متريال الـ Outline لكل Renderer — بتضل متوافقة مع SRP Batcher
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

    [Tooltip("كمان الـ Renderers اللي بالأولاد (موديل من كذا قطعة، أو نسخة Outline كـ child)")]
    public bool includeChildren = false;

    Renderer[] _renderers;
    MaterialPropertyBlock _block;
    readonly List<Material> _instances = new List<Material>();
    float _startTime = IdleStartTime;

    public bool IsGlowing => _startTime < IdleStartTime;

    // الوقت من بداية التوهج (ثواني)، 0 إذا مش شغّال
    public float Elapsed => IsGlowing ? Time.timeSinceLevelLoad - _startTime : 0f;

    void Awake()
    {
        _renderers = includeChildren
            ? GetComponentsInChildren<Renderer>(true)
            : GetComponents<Renderer>();

        if (_renderers.Length == 0)
            Debug.LogWarning($"{name}: GlowPulseController ما لقى أي Renderer", this);

        if (applyMode == ApplyMode.InstancedMaterial)
        {
            // بننسخ بس المتريالات اللي عندها _StartTime، ومتريال الجسم بيضل مشترك زي ما هو
            foreach (var r in _renderers)
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty(StartTimeId))
                        continue;
                    mats[i] = new Material(mats[i]); // بنمسحها بـ OnDestroy
                    _instances.Add(mats[i]);
                    changed = true;
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
        }
        else
        {
            _block = new MaterialPropertyBlock();
        }

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
        if (_renderers == null)
            return; // ContextMenu بوضع الـ Edit قبل Awake

        if (applyMode == ApplyMode.InstancedMaterial)
        {
            foreach (var mat in _instances)
            {
                if (mat != null)
                    mat.SetFloat(StartTimeId, startTime);
            }
        }
        else
        {
            // الـ Block بيتطبق على كل الـ submeshes/المتريالات تبع الـ Renderer
            foreach (var r in _renderers)
            {
                if (r == null)
                    continue;
                r.GetPropertyBlock(_block);
                _block.SetFloat(StartTimeId, startTime);
                r.SetPropertyBlock(_block);
            }
        }
    }

    void OnDestroy()
    {
        foreach (var mat in _instances)
        {
            if (mat != null)
                Destroy(mat);
        }
    }
}
