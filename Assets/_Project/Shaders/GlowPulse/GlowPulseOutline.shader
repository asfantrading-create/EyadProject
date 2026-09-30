// GlowPulseOutline — inverted-hull glow outline with the GlowPulse timing.
//
// Use it as a SECOND material on the same renderer as the body material. It redraws the mesh
// pushed out along its normals by _OutlineWidth with front faces culled, so only a thin band
// around the silhouette survives the depth test against the body.
//
//   Idle     : _StartTime far in the future (default 1e9) => hull collapsed, nothing drawn.
//   Ramp-up  : intensity 0 -> _MaxRampIntensity over _RampDuration seconds (smoothstep).
//   Pulse    : endless cosine pulse between _PulseMin and _PulseMax, C1-smooth hand-off from the ramp.
//
// Color = _GlowColor (HDR) * Intensity, blended additively so it fades in from nothing and blooms.
//
// Normals: the hull is pushed along SMOOTHED normals baked into UV channel 3 (TEXCOORD3) by the
// editor tool in Editor/GlowPulseSmoothNormals.cs. Hard-edged meshes (split normals) crack and
// spike without them. Meshes that were not baked fall back to the regular vertex normal.
//
// Silhouette Only: GlowPulse.shader marks body pixels in the stencil buffer; with
// _OutlineStencilComp = NotEqual the outline is never drawn on top of the body, only around it.
//
// Timing is (Time - _StartTime); GlowPulseController.StartGlow() / ResetGlow() write _StartTime.
//
// Why a separate material in the Transparent queue instead of an extra pass in GlowPulse.shader:
// URP's optional depth priming forces ZTest Equal on every opaque-queue pass, which would hide an
// inflated hull, and additive blending in the opaque queue gets overwritten by the skybox.
Shader "Custom/URP/GlowPulseOutline"
{
    Properties
    {
        [HDR] _GlowColor("Glow Color", Color) = (0, 4, 4, 1)
        _OutlineWidth("Outline Width (world units)", Range(0, 0.1)) = 0.02
        [ToggleUI] _UseSmoothedNormals("Use Baked Smoothed Normals (UV3)", Float) = 1
        // NotEqual = Silhouette Only (needs the GlowPulse body shader). Always = off.
        [Enum(UnityEngine.Rendering.CompareFunction)] _OutlineStencilComp("Silhouette Only (Stencil)", Float) = 6

        [Header(Timing)]
        _MaxRampIntensity("Max Ramp Intensity", Float) = 2
        _RampDuration("Ramp Duration (s)", Float) = 1.5
        _PulseSpeed("Pulse Speed (pulses per s)", Float) = 1
        _PulseMin("Pulse Min", Float) = 0.8
        _PulseMax("Pulse Max", Float) = 2

        [HideInInspector] _StartTime("Start Time", Float) = 1000000000
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GlowOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front          // keep only the back faces of the inflated hull
            ZTest LEqual        // the body's depth hides the hull everywhere except the silhouette band
            ZWrite On           // stops overlapping hull layers (concave meshes) from adding up twice
            Blend One One       // additive: dim = faint, bright HDR = bloom

            Stencil
            {
                Ref 4
                ReadMask 4
                Comp [_OutlineStencilComp]
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GlowPulseIntensity.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                float _OutlineWidth;
                float _UseSmoothedNormals;
                float _MaxRampIntensity;
                float _RampDuration;
                float _PulseSpeed;
                float _PulseMin;
                float _PulseMax;
                float _StartTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float3 smoothNormalOS : TEXCOORD3; // baked by GlowPulseSmoothNormals, (0,0,0) if not baked
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float intensity   : TEXCOORD0;
                float fogFactor   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Same value for every vertex of the object: idle -> ramp -> pulse
                float intensity;
                GlowPulseIntensity_float(_TimeParameters.x, _StartTime, _RampDuration, _MaxRampIntensity,
                                         _PulseSpeed, _PulseMin, _PulseMax, intensity);
                intensity = max(intensity, 0.0);

                // Smoothed normal if the mesh was baked, otherwise the real vertex normal
                bool hasSmoothed = _UseSmoothedNormals > 0.5 && dot(input.smoothNormalOS, input.smoothNormalOS) > 1e-6;
                float3 normalOS = hasSmoothed ? input.smoothNormalOS : input.normalOS;

                // Inflate along the world-space normal so the width is in world units even on scaled objects
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
                positionWS += normalWS * _OutlineWidth;

                float4 positionCS = TransformWorldToHClip(positionWS);

                // Idle: collapse every vertex to one point => zero-area triangles, nothing rasterized,
                // no depth written. Costs nothing and keeps the object exactly as it was.
                output.positionCS = intensity > 0.0 ? positionCS : float4(0.0, 0.0, 0.0, 1.0);
                output.intensity = intensity;
                output.fogFactor = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 color = _GlowColor.rgb * input.intensity;
                // Additive: fog fades the glow towards black (= adds nothing), not towards the fog color
                color = MixFogColor(color, half3(0, 0, 0), input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
