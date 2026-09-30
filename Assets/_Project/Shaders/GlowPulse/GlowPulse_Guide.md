# GlowPulse — Outline Glow (URP)

The object's body looks **completely normal**. Only a thin **inverted-hull outline** around the
silhouette glows, with this timing:

1. **Idle**: no outline at all.
2. **Ramp-up**: outline brightness 0 → *Max Ramp Intensity* over *Ramp Duration* seconds (smoothstep).
3. **Pulse**: brightness pulses forever between *Pulse Min* and *Pulse Max* (cosine).

Everything is computed from `t = Time - _StartTime`; `GlowPulseController.cs`
(in `Assets/Scripts`) writes `_StartTime` to start/restart/reset it.

## Files in this folder

| File | Role |
|---|---|
| `GlowPulse.shader` (**Custom/URP/GlowPulse**) | Body. Plain Lit-style surface, **no emission**. (Standard *URP/Lit* works just as well.) |
| `GlowPulseOutline.shader` (**Custom/URP/GlowPulseOutline**) | The glowing outline. Goes in a **second material slot** on the same renderer. |
| `GlowPulseIntensity.hlsl` | Shared idle → ramp → pulse math (used by the outline shader, or a Shader Graph Custom Function). |

> **Defaults** (never specified): glow color **cyan**, Unity **2022.3 LTS / Unity 6** (URP 14–17),
> **3D mesh**.

---

## 0. How it works

**Inverted hull.** The outline material redraws the mesh with every vertex pushed out along its
normal by `_OutlineWidth` and **front faces culled** (`Cull Front`). Where the body is in front,
the body's depth hides the hull; only the thin band that sticks out past the silhouette is left.

**Brightness** (unchanged formula):

```
t         = Time - _StartTime                         // negative => idle
D         = max(_RampDuration, 0.0001)
ramp      = _MaxRampIntensity * smoothstep(0, D, t)   // 0 -> MaxRamp, flat at the end
tp        = max(t - D, 0)                             // seconds since ramp finished
wave      = 0.5 + 0.5 * cos(2π * _PulseSpeed * tp)    // starts at 1 with zero slope
pulse     = lerp(_PulseMin, _PulseMax, wave)
handoff   = smoothstep(0, 0.5 / _PulseSpeed, tp)      // cross-fade over the first half pulse
Intensity = lerp(ramp, pulse, handoff)

OutlineColor = _GlowColor (HDR) * Intensity           // blended ADDITIVELY
```

At `t = D` every term has zero slope, so the ramp rolls into the pulse with no jump.

**Why additive, in the Transparent queue, as a separate material:**
- Additive means a dim outline is *faint*, not a dark line, and bright HDR values bloom.
- Idle: the shader collapses the hull to a single point, so nothing is drawn and no depth is written.
- An extra pass inside the body shader would sit in the opaque queue, where URP's optional
  **Depth Priming** forces `ZTest Equal` (the hull would vanish) and the skybox would paint over
  additive pixels. A transparent-queue material avoids both.

---

## 1. Quick path: the ready-made `.shader` files

Skip to §3. Nothing to build.

## 2. Shader Graph path (if you prefer graphs)

### 2a. Body
Use a normal material: *Universal Render Pipeline/Lit*, `Custom/URP/GlowPulse`, or your own Lit
graph with **Emission left unconnected (black)**. If you built the old GlowPulse Lit graph,
**delete the link into Fragment › Emission** (nodes 26–30 of the old guide) and save.

### 2b. Outline graph
1. **Create → Shader Graph → URP → Unlit Shader Graph**, name it `GlowPulseOutline`.
2. **Graph Inspector → Graph Settings**:
   - Surface Type: **Transparent**
   - Blending Mode: **Additive**
   - Render Face: **Back** (= Cull Front)
   - Depth Write: **Force Enabled** · Depth Test: **LEqual**
   - Precision: **Single**
3. **Blackboard** (Reference names matter; `_StartTime` is what the script writes):

| Name | Type | Reference | Default |
|---|---|---|---|
| Glow Color | Color, **Mode HDR** | `_GlowColor` | R0 G1 B1, Intensity 2 |
| Outline Width | Float, Slider 0 – 0.1 | `_OutlineWidth` | 0.02 (world units) |
| Max Ramp Intensity | Float | `_MaxRampIntensity` | 2 |
| Ramp Duration | Float | `_RampDuration` | 1.5 |
| Pulse Speed | Float | `_PulseSpeed` | 1 |
| Pulse Min | Float | `_PulseMin` | 0.8 |
| Pulse Max | Float | `_PulseMax` | 2 |
| _StartTime | Float | `_StartTime` | **1000000000**, Scope *Per Material*, *Show In Inspector* off |

(On old Shader Graph versions with only an **Exposed** checkbox, leave `_StartTime` exposed;
unticking it makes it global.)

### 2c. Time → Intensity

Build this chain once (nodes 5–25). Tip: select it → right-click → **Convert To → Sub Graph** to
keep the graph tidy.

#### Elapsed time `t`
| # | Node | Connections |
|---|---|---|
| 5 | **Time** | |
| 6 | **_StartTime** (drag) | |
| 7 | **Subtract** | A = Time `Time`, B = _StartTime → **`t`** |

#### Safe duration `D`
| # | Node | Connections |
|---|---|---|
| 8 | **Ramp Duration** (drag) | |
| 9 | **Maximum** | A = Ramp Duration, B = `0.0001` → **`D`** |

#### Ramp
| # | Node | Connections |
|---|---|---|
| 10 | **Smoothstep** | Edge1 = `0`, Edge2 = `D` (node 9), In = `t` (node 7) |
| 11 | **Max Ramp Intensity** (drag) | |
| 12 | **Multiply** | A = Smoothstep out, B = Max Ramp Intensity → **`ramp`** |

#### Time since ramp ended `tp`
| # | Node | Connections |
|---|---|---|
| 13 | **Subtract** | A = `t` (7), B = `D` (9) |
| 14 | **Maximum** | A = node 13, B = `0` → **`tp`** |

#### Pulse wave
| # | Node | Connections |
|---|---|---|
| 15 | **Pulse Speed** (drag) | |
| 16 | **Multiply** | A = `tp` (14), B = Pulse Speed |
| 17 | **Multiply** | A = node 16, B = `6.2831853` (2π) |
| 18 | **Cosine** | In = node 17 |
| 19 | **Remap** | In = Cosine out, In Min Max = (−1, 1), Out Min Max = (0, 1) → **`wave`** |
| 20 | **Pulse Min**, **Pulse Max** (drag both) | |
| 21 | **Lerp** | A = Pulse Min, B = Pulse Max, T = `wave` → **`pulse`** |

#### Hand-off ramp → pulse
| # | Node | Connections |
|---|---|---|
| 22 | **Maximum** | A = Pulse Speed (15), B = `0.001` |
| 23 | **Divide** | A = `0.5`, B = node 22 → half a pulse period |
| 24 | **Smoothstep** | Edge1 = `0`, Edge2 = node 23, In = `tp` (14) → **`handoff`** |
| 25 | **Lerp** | A = `ramp` (12), B = `pulse` (21), T = `handoff` (24) → **`Intensity`** |

**Shortcut instead of 5–25:** add a **Custom Function** node, Type = **File**,
Source = `Assets/_Project/Shaders/GlowPulse/GlowPulseIntensity.hlsl`, Name = `GlowPulseIntensity`.
Inputs (all Float): `Time, StartTime, RampDuration, MaxRampIntensity, PulseSpeed, PulseMin,
PulseMax`; Output: `Intensity` (Float). Wire the Time node and the properties in. Same math.

### 2d. Vertex: inflate along the normal, collapse when idle

| # | Node | Connections |
|---|---|---|
| 26 | **Position** | Space = **World** |
| 27 | **Normal Vector** | Space = **World** |
| 28 | **Outline Width** (drag) | |
| 29 | **Multiply** | A = Normal Vector, B = Outline Width |
| 30 | **Add** | A = Position (26), B = node 29 → inflated world position |
| 31 | **Transform** | From **World** To **Object**, Type **Position**, In = node 30 |
| 32 | **Step** | Edge = `0.0001`, In = `Intensity` (25) → 1 while glowing, 0 when idle |
| 33 | **Multiply** | A = node 31, B = node 32 → **Vertex › Position** |

Multiplying by 0 when idle puts every vertex at the object origin: zero-area triangles, nothing drawn.

### 2e. Fragment: color

| # | Node | Connections |
|---|---|---|
| 34 | **Glow Color** (drag) | |
| 35 | **Multiply** | A = Glow Color, B = `Intensity` (25) → **Fragment › Base Color** |

Alpha stays 1. **Save Asset**, then right-click the graph → **Create → Material** (`M_GlowOutline`).

---

## 3. Material & Inspector setup

### Create the outline material
1. Right-click `GlowPulseOutline.shader` (or the graph) → **Create → Material** → name it
   `M_GlowOutline`.
2. Set **Glow Color** (HDR) — cyan, Intensity 2 — and **Outline Width** (start at `0.02`; it is
   in world units, so scale it to your object's size). Copy your old timing values
   (Max Ramp, Ramp Duration, Pulse Speed/Min/Max) from `M_GlowPulse` if you had tuned them.

### Fix the existing body material
3. Select your existing `M_GlowPulse`. It still uses `Custom/URP/GlowPulse`, which now has **no
   glow**; the old Glow/Timing fields disappear from the Inspector (stale values stay serialized
   but are unused). Nothing else to do. (Or switch it to *Universal Render Pipeline/Lit*.)

### Renderer: two material slots
4. Select the object → **Mesh Renderer › Materials** → click **+** so the list has 2 entries:
   - Element 0: `M_GlowPulse` (body)
   - Element 1: `M_GlowOutline` (outline)

   Unity draws the mesh a second time with the extra material. (Unity 6 shows an info box about
   more materials than sub-meshes; that's expected here.)

### Controller
5. `GlowPulseController` on the same GameObject — **no changes needed**. It finds every material
   with a `_StartTime` property (now the outline) and drives it; the body material is not touched.
   - **Play On Start**, `StartGlow()`, `ResetGlow()` work as before.
   - **Apply Mode → InstancedMaterial** now copies *only* the outline material, so the body stays
     shared/batched. **PropertyBlock** also works.
   - New: **Include Children** — also drive renderers on child objects (see §5).

## 4. HDR + Post Processing + Bloom (unchanged)

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

## 5. Limitations and fixes

- **Gaps at hard edges / corners.** The hull follows vertex normals; meshes with split normals
  (cubes, low-poly) crack at corners. Fix: duplicate the model file, set the copy's
  **Import Settings → Normals: Calculate, Smoothing Angle 180**, and use that copy only for an
  outline child renderer (setup as in the next bullet), so the body keeps its hard shading.
- **Meshes with several sub-meshes** (several materials on one renderer): an extra material slot
  only wraps the **last** sub-mesh. Fix: add a child GameObject with the same mesh
  (MeshFilter + MeshRenderer, *Cast Shadows Off*), put `M_GlowOutline` in **every** slot there,
  and tick **Include Children** on the parent's `GlowPulseController`.
- **Models made of several child renderers** (e.g. hull + turret): add the second slot to each
  renderer, put the controller on the root, tick **Include Children**.
- **Width is in world units**, so it looks thinner far away. Increase `_OutlineWidth` for distant objects.
- **Skinned meshes** work the same way (second material slot on the SkinnedMeshRenderer).
- **Shared timing for many objects** via a *Render Objects* Renderer Feature is possible, but an
  override material means one `_StartTime` for all of them; the per-object slot setup above is
  what keeps each object independently restartable.
