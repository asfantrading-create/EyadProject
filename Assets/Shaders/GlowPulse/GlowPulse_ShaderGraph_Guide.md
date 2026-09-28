# GlowPulse — URP Shader Graph Guide

A Fresnel/rim glow with a light whole-surface emission, driven by time:

1. **Idle**: no glow, the object looks normal.
2. **Ramp-up**: glow goes 0 → *Max Ramp Intensity* over *Ramp Duration* seconds (smoothstep).
3. **Pulse**: glow pulses forever between *Pulse Min* and *Pulse Max* (sine/cosine wave).

Everything is computed from `t = Time - _StartTime`, so writing `_StartTime` restarts the effect.
`GlowPulseController.cs` (in `Assets/Scripts`) does that for you.

> **Defaults used here** (the request template left these blank): glow color **cyan**,
> Unity **2022.3 LTS / Unity 6** (URP 14–17), object type **3D mesh**. Differences between
> versions are called out where they matter.

---

## 0. The math

```
t         = Time - _StartTime                         // negative => idle (glow = 0)
D         = max(_RampDuration, 0.0001)

ramp      = _MaxRampIntensity * smoothstep(0, D, t)   // 0 -> MaxRamp, flat at the end

tp        = max(t - D, 0)                             // seconds since ramp finished
wave      = 0.5 + 0.5 * cos(2π * _PulseSpeed * tp)    // 1 -> 0 -> 1 ... (starts at 1, flat)
pulse     = lerp(_PulseMin, _PulseMax, wave)          // starts at PulseMax

handoff   = smoothstep(0, 0.5 / _PulseSpeed, tp)      // 0 -> 1 during the first half pulse

Intensity = lerp(ramp, pulse, handoff)

Emission  = _GlowColor * Intensity * (Fresnel(_FresnelPower) + SurfaceGlow)
```

**Why there is no jump at the ramp → pulse boundary**

- `smoothstep` ends at `MaxRamp` with slope 0.
- The pulse uses **cos** (not sin), so it starts at its peak with slope 0.
- `handoff` is 0 at `t = D` and also has slope 0, then cross-fades from the ramp value into the
  pulse over half a pulse period.

So at `t = D` the value is exactly `MaxRamp` and nothing is moving — the curve is continuous
*and* smooth, even if `MaxRamp ≠ PulseMax`. (If you set `Max Ramp Intensity = Pulse Max`, the
hand-off is invisible; the ramp simply rolls straight into the first down-swing.)

Why the idle phase works: while idle, the script sets `_StartTime = 1e9`, so `t` is hugely
negative → `smoothstep` returns 0, `tp = 0`, `handoff = 0` → `Intensity = 0` → emission is black.

`Pulse Speed` is in **pulses per second** (1 = one full bright→dim→bright cycle per second).

---

## 1. Create the graph

1. Project window → right-click → **Create → Shader Graph → URP → Lit Shader Graph**.
   Name it `GlowPulse`.
2. Double-click to open. In **Graph Inspector → Graph Settings**:
   - Material: **Lit**, Workflow: Metallic, Surface Type: **Opaque**, Render Face: Front.
   - Precision (graph): **Single** (time values need full float precision).

## 2. Blackboard properties

Click **+** on the Blackboard for each. Set the **Reference** name exactly (the C# script relies
on `_StartTime`).

| Name | Type | Reference | Default | Notes |
|---|---|---|---|---|
| Base Map | Texture2D | `_BaseMap` | white | right-click → *Set as Main Texture* |
| Base Color | Color | `_BaseColor` | white (1,1,1,1) | right-click → *Set as Main Color* |
| Glow Color | Color | `_GlowColor` | R0 G1 B1, Intensity **2** | **Mode: HDR** |
| Max Ramp Intensity | Float | `_MaxRampIntensity` | 2 | |
| Fresnel Power | Float | `_FresnelPower` | 3 | Mode: Slider 0.5 – 10 |
| Ramp Duration | Float | `_RampDuration` | 1.5 | seconds |
| Pulse Speed | Float | `_PulseSpeed` | 1 | pulses/second |
| Pulse Min | Float | `_PulseMin` | 0.8 | |
| Pulse Max | Float | `_PulseMax` | 2 | same as Max Ramp = seamless |
| _StartTime | Float | `_StartTime` | **1000000000** | hidden, see below |

**Hiding `_StartTime`:** in its Node Settings set **Scope = Per Material** and **untick
"Show In Inspector"**. On older Shader Graph versions that only have an **"Exposed"**
checkbox, leave it **exposed** — unticking "Exposed" there turns it into a *global* variable
and per-object values stop working. It will just be visible in the inspector; don't edit it.

The default `1e9` means a freshly created material is idle.

## 3. Base surface (normal look)

| # | Node | Settings / connections |
|---|---|---|
| 1 | **Base Map** (drag from Blackboard) | → Sample Texture 2D `Texture` |
| 2 | **Sample Texture 2D** | UV default (UV0) |
| 3 | **Base Color** (drag) | |
| 4 | **Multiply** | A = Sample Texture 2D `RGBA`, B = Base Color → **Fragment › Base Color** |

Leave Metallic 0, Smoothness 0.5 (or add your own properties).

## 4. Time → Intensity (the ramp/pulse chain)

Tip: right-click an output → **Add Redirect Node** / use **Group** (select nodes → Ctrl+G)
to keep this tidy. Better: build this whole section as a **Sub Graph** (see §7) so the outline
shader can reuse it.

### 4a. Elapsed time `t`
| # | Node | Connections |
|---|---|---|
| 5 | **Time** | |
| 6 | **_StartTime** (drag) | |
| 7 | **Subtract** | A = Time `Time`, B = _StartTime → **`t`** |

### 4b. Safe duration `D`
| # | Node | Connections |
|---|---|---|
| 8 | **Ramp Duration** (drag) | |
| 9 | **Maximum** | A = Ramp Duration, B = `0.0001` → **`D`** |

### 4c. Ramp
| # | Node | Connections |
|---|---|---|
| 10 | **Smoothstep** | Edge1 = `0`, Edge2 = `D` (node 9), In = `t` (node 7) |
| 11 | **Max Ramp Intensity** (drag) | |
| 12 | **Multiply** | A = Smoothstep out, B = Max Ramp Intensity → **`ramp`** |

### 4d. Time since ramp ended `tp`
| # | Node | Connections |
|---|---|---|
| 13 | **Subtract** | A = `t` (7), B = `D` (9) |
| 14 | **Maximum** | A = node 13, B = `0` → **`tp`** |

### 4e. Pulse wave
| # | Node | Connections |
|---|---|---|
| 15 | **Pulse Speed** (drag) | |
| 16 | **Multiply** | A = `tp` (14), B = Pulse Speed |
| 17 | **Multiply** | A = node 16, B = `6.2831853` (2π) |
| 18 | **Cosine** | In = node 17 |
| 19 | **Remap** | In = Cosine out, In Min Max = (−1, 1), Out Min Max = (0, 1) → **`wave`** |
| 20 | **Pulse Min**, **Pulse Max** (drag both) | |
| 21 | **Lerp** | A = Pulse Min, B = Pulse Max, T = `wave` → **`pulse`** |

### 4f. Hand-off ramp → pulse
| # | Node | Connections |
|---|---|---|
| 22 | **Maximum** | A = Pulse Speed (15), B = `0.001` |
| 23 | **Divide** | A = `0.5`, B = node 22 → half a pulse period |
| 24 | **Smoothstep** | Edge1 = `0`, Edge2 = node 23, In = `tp` (14) → **`handoff`** |
| 25 | **Lerp** | A = `ramp` (12), B = `pulse` (21), T = `handoff` (24) → **`Intensity`** |

**Shortcut instead of 5–25:** add a **Custom Function** node, Type = **File**,
Source = `Assets/Shaders/GlowPulse/GlowPulseIntensity.hlsl`, Name = `GlowPulseIntensity`.
Inputs (all Float): `Time, StartTime, RampDuration, MaxRampIntensity, PulseSpeed, PulseMin,
PulseMax`; Output: `Intensity` (Float). Wire the Time node and the properties in. Same math.

## 5. Fresnel + surface glow → Emission

| # | Node | Connections |
|---|---|---|
| 26 | **Fresnel Effect** | Normal & View Dir = defaults, Power = **Fresnel Power** (drag) |
| 27 | **Add** | A = Fresnel out, B = `0.25` (whole-surface glow; raise/lower to taste) |
| 28 | **Multiply** | A = node 27, B = `Intensity` (25) |
| 29 | **Glow Color** (drag) | |
| 30 | **Multiply** | A = Glow Color, B = node 28 → **Fragment › Emission** |

(If you want the surface amount tweakable, make `0.25` a Float property `_SurfaceGlow`.)

Final layout:

```
Time ─┐
      Subtract ─ t ─┬─ Smoothstep(0,D) ─ ×MaxRamp ───────────── ramp ──┐
_StartTime ┘        │                                                  │
RampDuration ─ max ─ D                                                  Lerp ─ Intensity
                    └─ (t−D) ─ max0 ─ tp ─┬─ ×Speed ×2π ─ cos ─ remap ─ Lerp(Min,Max) ─ pulse ┘   │
                                          └─ Smoothstep(0, 0.5/Speed) ─ handoff ──────(T)─────┘  │
                                                                                                 │
Fresnel(Power) ─ +0.25 ─ × Intensity ─ × GlowColor(HDR) ──────────────────────────► Emission
Sample(BaseMap) × BaseColor ─────────────────────────────────────────────────────► Base Color
```

Click **Save Asset**.

## 6. Material, script, HDR, Bloom

### Material
1. Right-click `GlowPulse` graph → **Create → Material** (name `M_GlowPulse`).
2. Assign your texture to Base Map, pick Base Color.
3. Glow Color: open the HDR picker → cyan (R0 G1 B1), **Intensity 2** (values must go above 1
   to pass the Bloom threshold).
4. Drag the material onto your mesh.

### Script
1. Add **GlowPulseController** to the same GameObject as the MeshRenderer.
2. **Play On Start** — start the ramp automatically.
3. **Apply Mode**
   - *InstancedMaterial* (default): each object gets its own material copy. Keeps the SRP Batcher.
   - *PropertyBlock*: no material copies; that renderer just drops out of SRP batching.
4. Call from code / UnityEvents / buttons:
   ```csharp
   glow.StartGlow();  // start or restart from the ramp-up
   glow.ResetGlow();  // back to normal (idle)
   ```
   Both are also in the component's ⋮ context menu for testing in Play mode.

The script uses `Time.timeSinceLevelLoad` because that is what the shader's `Time` node
(`_Time.y`) reports. If you pause with `Time.timeScale = 0`, the glow freezes too.

### Enable HDR + Post Processing + Bloom
1. **Project Settings → Player → Other Settings → Color Space = Linear**.
2. Select your **URP Asset** (Project Settings → Graphics/Quality shows which one) →
   **Quality › HDR = on**. Do this on every quality level's URP asset you use.
3. Select the **Universal Renderer Data** used by that asset → make sure
   **Post-processing** is enabled (checkbox on the renderer in URP 14+).
4. Select your **Main Camera** → **Rendering › Post Processing = on**.
5. Hierarchy → **+ → Volume → Global Volume**. In the Volume component click **New** (profile),
   then **Add Override → Post-processing → Bloom**:
   - Threshold **1** (only HDR values > 1 bloom)
   - Intensity **1 – 2**
   - Scatter **0.7**
   - (optional) Tonemapping override → ACES or Neutral, so bright colors don't clip.

Press Play and call `StartGlow()`.

---

## 7. Real outline?

**Not in the same single Shader Graph pass.** A classic "inverted hull" outline needs a
*second pass* that draws the mesh inflated along its normals with **front faces culled**.
Shader Graph produces one forward pass per target, and you can't add a custom extra pass or
separate cull mode to a Lit graph. Screen-space outlines (like the URP Glow Outliner asset
uses) need a separate render pass entirely. So pick one of these:

### Option A (recommended, simple): second material on the same renderer
1. First make the timing reusable: select nodes 5–25 → right-click → **Convert To → Sub Graph**
   (`SG_GlowPulseIntensity`, output `Intensity`). Use it in both graphs.
2. Create **Shader Graph → URP → Unlit Shader Graph** `GlowOutline`:
   - Graph Settings: Surface **Opaque**, **Render Face = Back**, Depth Write enabled.
   - Properties: same timing ones (`_GlowColor`, `_MaxRampIntensity`, `_RampDuration`,
     `_PulseSpeed`, `_PulseMin`, `_PulseMax`, `_StartTime` default 1e9) + `_OutlineWidth`
     (Float, 0.02).
   - **Vertex**:
     `Position (Object)` + `Normal Vector (Object)` × `_OutlineWidth` ×
     `Saturate(Intensity / _MaxRampIntensity)` → **Vertex › Position**.
     The width factor makes the outline 0 when idle, grow during the ramp and breathe with the pulse.
   - **Fragment**: `_GlowColor × Intensity` → **Base Color** (HDR, so it blooms).
3. On the MeshRenderer, set **Materials › Size = 2**, slot 0 = `M_GlowPulse`,
   slot 1 = `M_GlowOutline`. The extra slot draws the mesh a second time with the outline shader.
4. `GlowPulseController` already writes `_StartTime` to **every** material on the renderer
   (both modes), so outline and glow stay in sync.

Caveats: meshes with hard edges/split normals get gaps at corners (use smooth normals or bake
smoothed normals into a UV channel); works best on single-submesh meshes (with several submeshes
the extra material only wraps the last one — use Option B).

### Option B: Renderer Feature
Universal Renderer Data → **Add Renderer Feature → Render Objects**: Event *AfterRenderingOpaques*,
Layer Mask = an `Outline` layer, Override Material = `M_GlowOutline`. Put glowing objects on that
layer. Works for any number of submeshes, but with a *Material* override all objects share one
material's values (one `_StartTime`), so per-object timing needs Option A or a custom feature.

### Option C: screen-space outline (what the asset does)
A custom `ScriptableRendererFeature` that renders selected objects into a mask, then blurs /
dilates (or jump-floods) it and composites it over the image. Gives uniform pixel-width,
silhouette-accurate outlines regardless of mesh topology, but it is C#/HLSL render-pass work,
not something you can build in Shader Graph alone.
