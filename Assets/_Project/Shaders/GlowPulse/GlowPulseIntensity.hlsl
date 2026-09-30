#ifndef GLOW_PULSE_INTENSITY_INCLUDED
#define GLOW_PULSE_INTENSITY_INCLUDED

// Optional shortcut for the Shader Graph "Custom Function" node (Type = File, Name = GlowPulseIntensity).
// Same math as the node-by-node chain in GlowPulse_Guide.md (§2c).
//
//   t        = Time - StartTime                       (negative => idle)
//   ramp     = MaxRamp * smoothstep(0, D, t)          (0 -> MaxRamp, zero slope at t = D)
//   tp       = max(t - D, 0)                          (time since the ramp finished)
//   pulse    = lerp(PulseMin, PulseMax, 0.5 + 0.5*cos(2*pi*PulseSpeed*tp))
//              (starts at PulseMax with zero slope)
//   handoff  = smoothstep(0, 0.5 / PulseSpeed, tp)    (0 -> 1 over the first half pulse)
//   Intensity = lerp(ramp, pulse, handoff)
//
// At t = D: value = MaxRamp and every term has zero slope, so ramp -> pulse has no jump,
// even when MaxRamp != PulseMax.
void GlowPulseIntensity_float(float Time, float StartTime, float RampDuration, float MaxRampIntensity,
                              float PulseSpeed, float PulseMin, float PulseMax, out float Intensity)
{
    float t = Time - StartTime;
    float d = max(RampDuration, 0.0001);

    float ramp = MaxRampIntensity * smoothstep(0.0, d, t);

    float tp = max(t - d, 0.0);
    float wave = 0.5 + 0.5 * cos(6.28318530718 * PulseSpeed * tp);
    float pulse = lerp(PulseMin, PulseMax, wave);

    float handoff = smoothstep(0.0, 0.5 / max(PulseSpeed, 0.001), tp);

    Intensity = lerp(ramp, pulse, handoff);
}

void GlowPulseIntensity_half(half Time, half StartTime, half RampDuration, half MaxRampIntensity,
                             half PulseSpeed, half PulseMin, half PulseMax, out half Intensity)
{
    float result;
    GlowPulseIntensity_float(Time, StartTime, RampDuration, MaxRampIntensity,
                             PulseSpeed, PulseMin, PulseMax, result);
    Intensity = result;
}

#endif
