# Valve Lever: خطوات الإعداد (Meta Interaction SDK)

السكربت: `Assets/Scripts/ValveLeverController.cs`

الفكرة:
- الدوران والحدود (0° → Max) بيعملها `OneGrabRotateTransformer` الجاهز.
- `ValveLeverController` بس بيقرأ الزاوية، وبيشغّل الـ Animator، وبيطلق الـ Events.
  كمان بيكتب المحور والـ Min/Max على الـ Transformer، فبتعدّل القيم من مكان واحد.

> تم التحقق من الـ API على `com.meta.xr.sdk.interaction` نسخة 207
> (`OneGrabRotateTransformer.Constraints`، `InjectOptionalRotationAxis`، `InjectOptionalPivotTransform`، `FloatConstraint`).

---

## 1) الـ Hierarchy المقترح

```
Joystick
├── Armature
│   └── Bone
│       ├── LeverPivot            ← جديد (Empty): ثابت، نقطة الدوران عند القاعدة
│       └── Bone.001 Grab         ← Rigidbody + Grabbable + OneGrabRotateTransformer + ValveLeverController
│           ├── Bone.001_end
│           ├── HandleCollider    ← جديد: CapsuleCollider حوالين المقبض بس
│           ├── GrabInteractable
│           ├── HandGrabInteractable
│           └── Mesh              ← عطّل الـ Mesh Collider
└── Cube

RisingObjectRoot                  ← Empty ثابت (أبو الأوبجكت اللي بيطلع)
└── RisingObject                  ← Animator (الأنيميشن بيحرك localPosition)
```

أهم قاعدة: `LeverPivot` لازم يكون **أخ (sibling)** لـ `Bone.001 Grab`، مش ابن إله.
إذا كان ابن، بيلف مع الذراع والـ Transformer بيضاعف الدوران.

---

## 2) LeverPivot

1. كليك يمين على `Bone` → Create Empty → سمّيه `LeverPivot`.
2. على `Bone.001 Grab`: ⋮ جنب Transform → **Copy Component**.
3. على `LeverPivot`: ⋮ → **Paste Component Values**. هيك صار بنفس مكان ودوران راس العظمة (القاعدة).
4. إذا بدك نقطة الدوران أوطى أو أعلى، حرّك `LeverPivot` بس.
5. من الـ Toolbar خلي الـ Gizmo على **Local** وشوف أسهم `LeverPivot`:
   - محور الدوران هو المحور **العمودي على اتجاه الميلان**.
     مثلاً إذا الذراع بتميل لقدام (باتجاه الأزرق Z)، المحور هو الأحمر (X).
   - إذا ولا محور مناسب (بيصير كتير مع موديلات Blender)، لف `LeverPivot` لحتى يصير واحد من أسهمه على محور المفصل.
     لف الـ Pivot ما بيحرّك الذراع، هو بس مرجع للمحاور.

---

## 3) `Bone.001 Grab`

### Rigidbody
| الحقل | القيمة |
|---|---|
| Is Kinematic | ✓ |
| Use Gravity | ✗ |
| Interpolate | None |

### Grabbable
| الحقل | القيمة |
|---|---|
| Transfer On Second Selection | ✓ |
| Max Grab Points | **1** (إذا خليتها -1 ومسكت بإيدين، ما في Two Grab Transformer والذراع بتعلّق) |
| Kinematic While Selected | ✓ |
| Throw When Unselected | **✗** (وإلا بيعطيه سرعة لما تفلته) |
| Optionals → One Grab Transformer | اسحب `OneGrabRotateTransformer` |
| Optionals → Two Grab Transformer | فاضي |

### احذف
- **Grab Free Transformer**: وإلا الذراع بتتحرك بحرية.
- **Respawn On Drop**: بيرجّع الذراع لمكانها إذا نزلت تحت Y = 0.3، وما إلها لزوم هون.

### ضيف: One Grab Rotate Transformer
- Pivot Transform: `LeverPivot`
- Rotation Axis و Constraints: خليهم، `ValveLeverController` بيكتبهم تلقائياً.
  (إذا لغيت `Drive Transformer Settings`، اعمل Min Angle: Constrain ✓ Value 0، Max Angle: Constrain ✓ Value 45.)

### ضيف: Valve Lever Controller
| الحقل | القيمة |
|---|---|
| Grabbable | `Bone.001 Grab` (بيتعبّى تلقائياً) |
| Rotate Transformer | `Bone.001 Grab` (بيتعبّى تلقائياً) |
| Pivot | `LeverPivot` |
| Rotation Axis | X / Y / Z (محور الـ Pivot) |
| Max Angle | 45 |
| Invert Direction | فعّله إذا الذراع بتميل لورا بدل لقدام |
| Drive Transformer Settings | ✓ |
| Target Animator | Animator تبع `RisingObject` |
| Animation Mode | Bool / Triggers / MotionTime (شوف تحت) |
| Events | اربط اللي بدك ياه على OnMaxReached / OnReturnedFromMax / OnGrabbed / OnReleased |

لما تحدد الأوبجكت بالـ Scene بيطلع قوس برتقالي بيبيّن المحور ومدى الدوران (0 → Max).

---

## 4) الـ Colliders

- على `Mesh`: **عطّل الـ Mesh Collider**. هو Convex لموديل كامل (ممكن فيه القاعدة المطاطية)،
  وما بيتشوّه مع الـ Skinned Mesh، فبتقدر تمسك الذراع من القاعدة.
- اعمل `HandleCollider` كابن لـ `Bone.001 Grab` مع **Capsule Collider** بيغطي المقبض والعمود بس.
- كل الـ Colliders اللي بدك ياها تنمسك لازم تكون تحت نفس الـ Rigidbody (`Bone.001 Grab`).
- `Cube` (القاعدة) ما لازم يكون تحت الـ Rigidbody.

---

## 5) GrabInteractable و HandGrabInteractable

الإعداد الموجود عندك صح (Pointable Element = Grabbable، Rigidbody = نفس الـ Rigidbody). ملاحظات:
- **Hand Alignment**: `Align On Grab` منيح. للشكل الأحلى سجّل Hand Grab Pose على المقبض.
- **Should Hide Hand On Grab**: بيخفي الإيد وقت المسك. شيله إذا بدك الإيد تبين.
- **Use Closest Point As Grab Source** بالـ GrabInteractable: خليه ✗.
  الـ Transformer بيحسب الزاوية من نقطة المسك حول الـ Pivot. إذا نقطة المسك قريبة كتير من الـ Pivot، الدوران بيصير حساس ومهزوز.

---

## 6) الـ Animator تبع الأوبجكت اللي بيطلع

حط الـ Animator على `RisingObject` تحت أب ثابت، وخلي الـ Clip يحرّك **localPosition** عشان الأنيميشن يكون نسبي لمكانه.

### A) MotionTime (موصى فيه: عكس دقيق من أي نقطة)
1. Parameters: Float `RiseProgress`.
2. State واحدة `Rise` فيها Clip الطلوع (Loop Time ✗).
3. بالـ State: **Motion Time ✓** → Parameter `RiseProgress`.
4. بالسكربت: Animation Mode = MotionTime، Rise Duration = مدة الطلوع.

السكربت بيحرّك `RiseProgress` من 0 لـ 1 بسلاسة. إذا رجّعت الذراع بنص الطلوع، الأوبجكت بينزل من نفس النقطة.

### B) Bool `Rise`
1. Parameters: Bool `Rise`.
2. Clips: `Rise` (من تحت لفوق) و `Lower` (من فوق لتحت، نفس الـ Keys بالعكس). الاتنين Loop Time ✗.
3. States: `Down` (Default، Clip `Lower`)، `Up` (Clip `Rise`).
4. Transitions:
   - Down → Up: Condition `Rise == true`، Has Exit Time ✗.
   - Up → Down: Condition `Rise == false`، Has Exit Time ✗.

### C) Triggers
نفس B بس Parameters: Trigger `Rise` و Trigger `Lower`، والـ Conditions عليهم.

---

## 7) مشاكل شائعة

1. **Interaction Layers**: هاد مفهوم من XR Interaction Toolkit، مش موجود بالـ Interaction SDK.
   الفلترة بالـ ISDK بتكون عن طريق **Interactor Filters** (مثلاً TagSet).
   الـ Layer تبع `GrabInteractable` و `HandGrabInteractable` (عندك Water) ما بيأثر على المسك.
   الكشف بيعتمد على الـ Colliders اللي تحت الـ Rigidbody.
2. **الذراع بتلف ضعف أو بتطير**: الـ Pivot ابن للذراع. خليه أخ الها (شوف قسم 2).
3. **الذراع بتتحرك بحرية**: لسا في `Grab Free Transformer`، أو الـ One Grab Transformer مش مربوط بالـ Grabbable.
   (إذا ما في Transformer مربوط، الـ Grabbable بيضيف `GrabFreeTransformer` لحاله.)
4. **الذراع بتلف بالاتجاه الغلط أو ما بتتحرك**:
   - المحور غلط: جرّب X / Y / Z وراقب القوس البرتقالي.
   - الاتجاه معكوس: فعّل Invert Direction.
   - موديلات Blender بتيجي بدوران -90 على X، فمحاور العظمة مش زي ما بتتوقع. لهيك منستخدم Pivot منفصل.
5. **الزاوية مش صفر بالبداية**: الـ Min/Max بالـ Transformer نسبية لوضعية الذراع وقت بداية المشهد.
   حط الذراع بوضعية 0° بالمحرر.
6. **الذراع بترجع لوضعيتها لحالها أو ما بتتحرك أبداً**: في Animator على `Joystick` (بيجي مع الـ FBX) عم يكتب على العظام كل فريم.
   احذفه، أو شيل الـ Controller منه.
7. **بتنرمى لما تفلتها**: Throw When Unselected لازم يكون ✗، و Is Kinematic ✓.
8. **Scale**: الـ Transformer بيستخدم `parent.lossyScale.x`، فخلي الـ Scale تبع الآباء متساوي (Uniform).
9. **بإيدين**: Max Grab Points = 1 مع Transfer On Second Selection ✓، فالإيد التانية بتاخدها.
10. **الأنيميشن ما بيشتغل**: شوف الـ Console. السكربت بيحذّرك إذا اسم الـ Parameter أو نوعه غلط.
