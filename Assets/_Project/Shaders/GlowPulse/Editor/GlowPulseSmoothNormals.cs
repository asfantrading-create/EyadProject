using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// بيحسب Normals ناعمة (Smoothed) ويخزنها بـ UV3 (TEXCOORD3) عشان GlowPulseOutline
/// ينفخ الـ Outline بدون تشققات عند الحواف الحادة. الـ Normals الأصلية ما بتتغير،
/// فشكل الجسم (الإضاءة) بيضل زي ما هو.
///
/// طريقتين:
///   1) موديل FBX/OBJ...: كليك يمين على الموديل بالـ Project →
///      GlowPulse → Enable Smoothed Outline Normals  (بيعمل Reimport وبيخبز تلقائياً كل مرة)
///   2) Mesh مش من موديل (ProBuilder، Mesh معمول بكود...): اختار الأوبجكت بالـ Hierarchy →
///      Tools → GlowPulse → Bake Smoothed Normals Into Mesh Copy
/// </summary>
public static class GlowPulseSmoothNormals
{
    // لازم يطابق TEXCOORD3 بـ GlowPulseOutline.shader
    public const int UVChannel = 3;

    // علامة بتنحط بـ userData تبع الـ ModelImporter
    public const string ImporterMarker = "GlowPulseSmoothNormals";

    /// <summary>بيخبز الـ Normals الناعمة بـ UV3 تبع الـ Mesh (ما بيلمس mesh.normals).</summary>
    public static void Bake(Mesh mesh)
    {
        if (mesh == null)
            return;

        var vertices = mesh.vertices;
        var normals = mesh.normals;
        bool hasNormals = normals != null && normals.Length == vertices.Length;
        int count = vertices.Length;
        if (count == 0)
            return;

        // الـ Vertices اللي بنفس المكان (Split normals / UV seams) بنجمعها بمجموعة وحدة
        float cell = Mathf.Max(mesh.bounds.size.magnitude * 1e-5f, 1e-6f);
        var groupOf = new int[count];
        var lookup = new Dictionary<Vector3Int, int>(count);
        var faceSums = new List<Vector3>(count);
        var normalSums = new List<Vector3>(count);

        for (int i = 0; i < count; i++)
        {
            var p = vertices[i];
            var key = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
            if (!lookup.TryGetValue(key, out int g))
            {
                g = faceSums.Count;
                lookup.Add(key, g);
                faceSums.Add(Vector3.zero);
                normalSums.Add(Vector3.zero);
            }
            groupOf[i] = g;
            if (hasNormals)
                normalSums[g] += normals[i];
        }

        // Normal كل وجه، موزون بالزاوية عند كل رأس (Angle-weighted) — أنعم نتيجة للزوايا الحادة
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                continue;

            var tris = mesh.GetTriangles(sub);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                Vector3 pa = vertices[a], pb = vertices[b], pc = vertices[c];

                var face = Vector3.Cross(pb - pa, pc - pa);
                if (face.sqrMagnitude < 1e-20f)
                    continue; // مثلث ممسوح (Degenerate)
                face.Normalize();

                faceSums[groupOf[a]] += face * AngleRad(pb - pa, pc - pa);
                faceSums[groupOf[b]] += face * AngleRad(pc - pb, pa - pb);
                faceSums[groupOf[c]] += face * AngleRad(pa - pc, pb - pc);
            }
        }

        var smoothed = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            int g = groupOf[i];
            var n = faceSums[g];

            // إذا ترتيب المثلثات مقلوب (Flipped winding) بنرجّع الاتجاه حسب الـ Normals الأصلية
            if (hasNormals && Vector3.Dot(n, normalSums[g]) < 0f)
                n = -n;

            if (n.sqrMagnitude > 1e-12f)
                smoothed.Add(n.normalized);
            else
                // وجهين متعاكسين بنفس المكان (Double-sided) بيلغوا بعض: منرجع للـ Normal الأصلي
                smoothed.Add(hasNormals ? normals[i] : Vector3.zero);
        }

        mesh.SetUVs(UVChannel, smoothed);
    }

    static float AngleRad(Vector3 u, Vector3 v)
    {
        return Vector3.Angle(u, v) * Mathf.Deg2Rad;
    }

    // ------------------------------------------------------------------
    // Model importer: تفعيل / إلغاء على موديلات مختارة بالـ Project
    // ------------------------------------------------------------------

    public static bool IsEnabled(AssetImporter importer)
    {
        return importer != null && !string.IsNullOrEmpty(importer.userData) && importer.userData.Contains(ImporterMarker);
    }

    [MenuItem("Assets/GlowPulse/Enable Smoothed Outline Normals")]
    static void EnableOnSelection()
    {
        foreach (var importer in SelectedModelImporters())
        {
            if (IsEnabled(importer))
                continue;
            importer.userData = string.IsNullOrEmpty(importer.userData)
                ? ImporterMarker
                : importer.userData + ";" + ImporterMarker;
            importer.SaveAndReimport();
            Debug.Log($"GlowPulse: smoothed outline normals enabled for {importer.assetPath}");
        }
    }

    [MenuItem("Assets/GlowPulse/Disable Smoothed Outline Normals")]
    static void DisableOnSelection()
    {
        foreach (var importer in SelectedModelImporters())
        {
            if (!IsEnabled(importer))
                continue;
            importer.userData = importer.userData.Replace(";" + ImporterMarker, "").Replace(ImporterMarker, "");
            importer.SaveAndReimport();
            Debug.Log($"GlowPulse: smoothed outline normals disabled for {importer.assetPath}");
        }
    }

    [MenuItem("Assets/GlowPulse/Enable Smoothed Outline Normals", true)]
    [MenuItem("Assets/GlowPulse/Disable Smoothed Outline Normals", true)]
    static bool ValidateSelection()
    {
        foreach (var _ in SelectedModelImporters())
            return true;
        return false;
    }

    static IEnumerable<ModelImporter> SelectedModelImporters()
    {
        foreach (var guid in Selection.assetGUIDs)
        {
            if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is ModelImporter importer)
                yield return importer;
        }
    }

    // ------------------------------------------------------------------
    // Mesh مش من ملف موديل: بنعمل نسخة .asset مخبوزة ونركّبها على الـ MeshFilter
    // ------------------------------------------------------------------

    [MenuItem("Tools/GlowPulse/Bake Smoothed Normals Into Mesh Copy")]
    static void BakeSelectedMeshCopies()
    {
        foreach (var go in Selection.gameObjects)
        {
            var filter = go.GetComponent<MeshFilter>();
            var skinned = go.GetComponent<SkinnedMeshRenderer>();
            var source = filter != null ? filter.sharedMesh : skinned != null ? skinned.sharedMesh : null;
            if (source == null)
            {
                Debug.LogWarning($"GlowPulse: {go.name} has no MeshFilter / SkinnedMeshRenderer mesh", go);
                continue;
            }

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(sourcePath) && AssetImporter.GetAtPath(sourcePath) is ModelImporter)
            {
                Debug.LogWarning($"GlowPulse: {source.name} comes from a model file. Use Assets → GlowPulse → " +
                                 "Enable Smoothed Outline Normals on that model instead (it survives re-imports).", go);
                continue;
            }

            string path = EditorUtility.SaveFilePanelInProject("Save smoothed-normal mesh copy",
                source.name + "_SmoothNormals", "asset", "Where to save the baked mesh copy");
            if (string.IsNullOrEmpty(path))
                continue;

            var copy = Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(path);
            Bake(copy);
            AssetDatabase.CreateAsset(copy, path);

            if (filter != null)
            {
                Undo.RecordObject(filter, "Assign smoothed-normal mesh");
                filter.sharedMesh = copy;
            }
            else
            {
                Undo.RecordObject(skinned, "Assign smoothed-normal mesh");
                skinned.sharedMesh = copy;
            }
            Debug.Log($"GlowPulse: baked smoothed normals into {path}", go);
        }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/GlowPulse/Bake Smoothed Normals Into Mesh Copy", true)]
    static bool ValidateBakeSelectedMeshCopies()
    {
        return Selection.gameObjects.Length > 0;
    }
}

/// <summary>
/// بيخبز الـ Normals الناعمة تلقائياً وقت الـ Import لكل موديل مفعّل عليه
/// (Assets → GlowPulse → Enable Smoothed Outline Normals).
/// </summary>
public class GlowPulseSmoothNormalsPostprocessor : AssetPostprocessor
{
    // غيّر الرقم إذا عدّلت طريقة الخبز، عشان Unity يعمل Reimport للموديلات
    public override uint GetVersion() => 1;

    void OnPostprocessModel(GameObject root)
    {
        if (!GlowPulseSmoothNormals.IsEnabled(assetImporter))
            return;

        var baked = new HashSet<Mesh>();
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh != null && baked.Add(filter.sharedMesh))
                GlowPulseSmoothNormals.Bake(filter.sharedMesh);
        }
        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned.sharedMesh != null && baked.Add(skinned.sharedMesh))
                GlowPulseSmoothNormals.Bake(skinned.sharedMesh);
        }
    }
}
