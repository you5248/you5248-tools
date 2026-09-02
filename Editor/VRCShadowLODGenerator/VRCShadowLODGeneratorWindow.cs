using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace you5248.VRCShadowLODGenerator
{

    public class VRCShadowLODGeneratorWindow : EditorWindow
    {
        enum LowQualityMode
        {
            NoShadows,      // LOW: 蠖ｱ縺ｪ縺・
            BoundsBox       // LOW: 蠖ｱ縺ｯ邂ｱ・・ED縺ｨ蜷後§縺縺檎畑騾疲ｬ｡隨ｬ・・
        }

        // Options
        bool includeChildren = true;
        bool keepOriginalReceiveShadows = true;
        bool disableOriginalCastShadows = true;

        bool createMedium = true;
        bool createLow = true;

        LowQualityMode lowMode = LowQualityMode.NoShadows;

        // Proxy naming
        const string MED_NAME = "ShadowProxy_MED_Box";
        const string LOW_NAME = "ShadowProxy_LOW";

        [MenuItem("you5248/Shadow LOD Generator (Built-in)")]
        static void Open()
        {
            GetWindow<VRCShadowLODGeneratorWindow>("Shadow LOD");
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "驕ｸ謚槭＠縺・GameObject・井ｻｻ諢上〒蟄宣嚴螻､繧ょ性繧・峨・ MeshRenderer 縺ｫ蟇ｾ縺励※縲―n" +
                "繝ｪ繧｢繝ｫ繧ｿ繧､繝蠖ｱ逕ｨ縺ｮ縲手ｻｽ驥上・繝ｭ繧ｭ繧ｷ縲上ｒ逕滓・縺励∪縺吶・n\n" +
                "繝ｻMED: Bounds Box・育ｮｱ・峨〒 Shadows Only\n" +
                "繝ｻLOW: 蠖ｱ縺ｪ縺・or 邂ｱ\n\n" +
                "Ideal for making a shadow-only low-poly renderer reliably.\n" +
                "Does not include automatic mesh simplification.",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            includeChildren = EditorGUILayout.Toggle("蟄宣嚴螻､繧ょ性繧", includeChildren);

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            disableOriginalCastShadows = EditorGUILayout.Toggle("Disable original Cast Shadows", disableOriginalCastShadows);
            keepOriginalReceiveShadows = EditorGUILayout.Toggle("Keep Original Receive Shadows", keepOriginalReceiveShadows);
            keepOriginalReceiveShadows = EditorGUILayout.Toggle("Keep Original Receive Shadows", keepOriginalReceiveShadows);
            createMedium = EditorGUILayout.Toggle("Create Medium", createMedium);
            createLow = EditorGUILayout.Toggle("Create Low", createLow);

            using (new EditorGUI.DisabledScope(!createLow))
            {
                lowMode = (LowQualityMode)EditorGUILayout.EnumPopup("Low Quality Mode", lowMode);
            }

            EditorGUILayout.Space();

            var selected = Selection.gameObjects;
            EditorGUILayout.LabelField($"Selection: {selected.Length} objects");

            using (new EditorGUI.DisabledScope(selected == null || selected.Length == 0))
            {
                if (GUILayout.Button("Generate / Update Shadow Proxies", GUILayout.Height(36)))
                {
                    Generate();
                }
            }
        }

        void Generate()
        {
            var roots = Selection.gameObjects;
            if (roots == null || roots.Length == 0) return;

            // 蜿朱寔
            var renderers = new List<MeshRenderer>();
            foreach (var root in roots)
            {
                if (root == null) continue;
                if (includeChildren)
                    renderers.AddRange(root.GetComponentsInChildren<MeshRenderer>(true));
                else
                {
                    var mr = root.GetComponent<MeshRenderer>();
                    if (mr != null) renderers.Add(mr);
                }
            }

            int processed = 0;
            foreach (var mr in renderers)
            {
                if (mr == null) continue;

                // MeshRenderer + MeshFilter縺後≠繧九ｂ縺ｮ縺縺托ｼ・kinned縺ｯ髯､螟厄ｼ壼ｽｱ縺縺代・繝ｭ繧ｭ繧ｷ縺ｯ蛻･險ｭ險医′蠢・ｦ・ｼ・
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                // 蜈・・險ｭ螳・
                Undo.RecordObject(mr, "Shadow LOD: Modify Original Renderer");
                if (disableOriginalCastShadows)
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                if (!keepOriginalReceiveShadows)
                    mr.receiveShadows = false;

                // MED
                if (createMedium)
                {
                    CreateOrUpdateBoundsBoxProxy(mr, MED_NAME);
                }
                else
                {
                    RemoveChildIfExists(mr.transform, MED_NAME);
                }

                // LOW
                if (createLow)
                {
                    if (lowMode == LowQualityMode.NoShadows)
                    {
                        // LOW縺ｯ蠖ｱ辟｡縺暦ｼ昴・繝ｭ繧ｭ繧ｷ菴懊ｉ縺壹∵里蟄倥′縺ゅｌ縺ｰ豸医☆
                        RemoveChildIfExists(mr.transform, LOW_NAME);
                    }
                    else
                    {
                        CreateOrUpdateBoundsBoxProxy(mr, LOW_NAME);
                    }
                }
                else
                {
                    RemoveChildIfExists(mr.transform, LOW_NAME);
                }

                processed++;
            }

            Debug.Log($"[ShadowLOD] processed renderers={processed}");
        }

        static void CreateOrUpdateBoundsBoxProxy(MeshRenderer sourceMr, string childName)
        {
            var t = sourceMr.transform;

            // 譌｢蟄俶爾縺・
            Transform child = t.Find(childName);
            GameObject go;
            if (child == null)
            {
                go = new GameObject(childName);
                Undo.RegisterCreatedObjectUndo(go, "Shadow LOD: Create Proxy");
                go.transform.SetParent(t, false);
            }
            else
            {
                go = child.gameObject;
                Undo.RegisterFullObjectHierarchyUndo(go, "Shadow LOD: Update Proxy");
            }

            // 繧ｽ繝ｼ繧ｹ縺ｮ繝ｭ繝ｼ繧ｫ繝ｫBounds・・eshFilter縺ｮmesh bounds・峨ｒ菴ｿ縺｣縺ｦ邂ｱ繧剃ｽ懊ｋ
            // 窶ｻ Mesh.bounds 縺ｯ繝ｭ繝ｼ繧ｫ繝ｫ遨ｺ髢薙５ransform繧ｹ繧ｱ繝ｼ繝ｫ縺ｯ蟄舌↓蠑輔″邯吶＄縺ｮ縺ｧOK縲・
            var mf = sourceMr.GetComponent<MeshFilter>();
            var mesh = mf.sharedMesh;
            var b = mesh.bounds;

            // Proxy Mesh (box) 繧堤函謌・譖ｴ譁ｰ
            var proxyMf = go.GetComponent<MeshFilter>();
            if (proxyMf == null) proxyMf = go.AddComponent<MeshFilter>();

            var proxyMr = go.GetComponent<MeshRenderer>();
            if (proxyMr == null) proxyMr = go.AddComponent<MeshRenderer>();

            proxyMf.sharedMesh = BuildBoxMesh(b.center, b.size);

            // Renderer險ｭ螳夲ｼ壼ｽｱ縺縺・
            proxyMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            proxyMr.receiveShadows = false;

            // LightProbe遲峨・蠖ｱ逶ｮ逧・↑繧牙渕譛ｬ縺・ｉ縺ｪ縺・ｼ・etPass繧・ｨ育ｮ励ｒ蠅励ｄ縺励◆縺上↑縺・ｼ・
            proxyMr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            proxyMr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // 繝槭ユ繝ｪ繧｢繝ｫ縺ｯ菴輔〒繧ゅ＞縺・′縲ゞnity縺系ull繧貞ｫ後≧縺薙→縺後≠繧九・縺ｧ驕ｩ蠖薙↑繝・ヵ繧ｩ繝ｫ繝・
            // ・・hadowsOnly縺ｪ繧芽ｦ九∴縺ｪ縺・ｼ・
            if (proxyMr.sharedMaterial == null)
            {
                proxyMr.sharedMaterial = sourceMr.sharedMaterial != null
                    ? sourceMr.sharedMaterial
                    : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            }

            // Transform縺ｯ繝ｭ繝ｼ繧ｫ繝ｫ蜴溽せ縺ｫ鄂ｮ縺擾ｼ・esh縺慶enter/size縺ｧ菴咲ｽｮ繧呈戟縺､縺ｮ縺ｧ・・
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // 繝ｬ繧､繝､繝ｼ/Static縺ｯ蜷医ｏ縺帙ｋ・亥ｿ・ｦ√↑繧会ｼ・
            go.layer = sourceMr.gameObject.layer;
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(sourceMr.gameObject));
        }

        static void RemoveChildIfExists(Transform parent, string name)
        {
            if (parent == null) return;
            var child = parent.Find(name);
            if (child != null)
            {
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        // center/size 縺ｮ邂ｱ繝｡繝・す繝･・医Ο繝ｼ繧ｫ繝ｫ・・
        static Mesh BuildBoxMesh(Vector3 center, Vector3 size)
        {
            var m = new Mesh();
            m.name = "ShadowProxy_Box";

            Vector3 ext = size * 0.5f;

            // 8 corners
            Vector3 p0 = center + new Vector3(-ext.x, -ext.y, -ext.z);
            Vector3 p1 = center + new Vector3( ext.x, -ext.y, -ext.z);
            Vector3 p2 = center + new Vector3( ext.x,  ext.y, -ext.z);
            Vector3 p3 = center + new Vector3(-ext.x,  ext.y, -ext.z);
            Vector3 p4 = center + new Vector3(-ext.x, -ext.y,  ext.z);
            Vector3 p5 = center + new Vector3( ext.x, -ext.y,  ext.z);
            Vector3 p6 = center + new Vector3( ext.x,  ext.y,  ext.z);
            Vector3 p7 = center + new Vector3(-ext.x,  ext.y,  ext.z);

            // 24 vertices (per face) for correct normals (not critical for shadow but fine)
            var v = new List<Vector3>(24)
            {
                // Front (z+): p4 p5 p6 p7
                p4, p5, p6, p7,
                // Back (z-): p1 p0 p3 p2
                p1, p0, p3, p2,
                // Left (x-): p0 p4 p7 p3
                p0, p4, p7, p3,
                // Right (x+): p5 p1 p2 p6
                p5, p1, p2, p6,
                // Top (y+): p3 p7 p6 p2
                p3, p7, p6, p2,
                // Bottom (y-): p0 p1 p5 p4
                p0, p1, p5, p4
            };

            var tris = new List<int>(36)
            {
                // Front
                0,1,2, 0,2,3,
                // Back
                4,5,6, 4,6,7,
                // Left
                8,9,10, 8,10,11,
                // Right
                12,13,14, 12,14,15,
                // Top
                16,17,18, 16,18,19,
                // Bottom
                20,21,22, 20,22,23
            };

            m.SetVertices(v);
            m.SetTriangles(tris, 0, true);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

}
