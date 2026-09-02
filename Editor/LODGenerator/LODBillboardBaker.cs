using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace you5248.LODGenerator
{
    /// <summary>
    /// 薄いメッシュを板ポリ（ビルボード・インポスター）に置き換えるための撮影・生成ユーティリティ。
    ///
    /// メッシュ頂点を読まず「レンダリング結果」を撮るので、Read/Write 無効や
    /// Static Batching 済みで簡略化が効かないメッシュでも動作する（削減不能ケースの代替手段）。
    ///
    /// 手順:
    ///   1. mesh.bounds から一番薄い軸を検出（手動上書きも可）
    ///   2. その軸に正対するオルソカメラで隔離レンダリング（背景は透明クリア）
    ///   3. 透過 PNG として保存（カットアウト用）
    ///   4. 薄い軸に直交する平面に 1 枚の板ポリ（既定は両面）を生成
    ///   5. BuiltIn Standard / Cutout マテリアルを生成しテクスチャを割当
    /// </summary>
    public static class LODBillboardBaker
    {
        /// <summary>板ポリ化の各種設定。</summary>
        public struct Options
        {
            public int resolution;     // 撮影テクスチャの長辺ピクセル数
            public float cutoff;       // Standard Cutout の _Cutoff
            public float padding;      // 余白率 (0.0 - 0.5)
            public bool twoSided;      // 裏面も生成するか
            public int axisOverride;   // -1=自動 / 0,1,2 = X,Y,Z を薄い軸として強制
        }

        public static Options DefaultOptions
        {
            get
            {
                return new Options
                {
                    resolution = 1024,
                    cutoff = 0.5f,
                    padding = 0.05f,
                    twoSided = true,
                    axisOverride = -1
                };
            }
        }

        /// <summary>Bake の結果。ok=false の場合は error を参照。</summary>
        public struct Result
        {
            public bool ok;
            public string error;
            public Mesh quad;
            public Material material;
            public Texture2D texture;
        }

        // 隔離レンダリング用に予約するレイヤー（撮影は一瞬で、終了時に必ず後始末する）
        private const int CaptureLayer = 31;
        // シーン内の他オブジェクトと衝突しないよう、撮影対象を遠方へ退避する
        private static readonly Vector3 FarOffset = new Vector3(100000f, 100000f, 100000f);

        // ===================================================================
        //  薄さ判定
        // ===================================================================

        /// <summary>bounds の3軸 extent のうち最小の軸 index (0=X,1=Y,2=Z) を返す。</summary>
        public static int DetectThinAxis(Bounds b)
        {
            Vector3 e = b.size;
            int axis = 0;
            if (e.y < e[axis]) axis = 1;
            if (e.z < e[axis]) axis = 2;
            return axis;
        }

        /// <summary>最小 extent ÷ 最大 extent。0 に近いほど薄い。板ポリ向き判定に使う。</summary>
        public static float Thinness(Bounds b)
        {
            Vector3 e = b.size;
            float min = Mathf.Min(e.x, Mathf.Min(e.y, e.z));
            float max = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
            return max > 1e-6f ? min / max : 1f;
        }

        // ===================================================================
        //  メイン
        // ===================================================================

        /// <summary>
        /// 1 つのメッシュを板ポリ + テクスチャ + マテリアルに変換する。
        /// 生成したアセットのパスは outAssetPaths に追記される（Undo クリーンアップ用）。
        /// </summary>
        public static Result Bake(Mesh sourceMesh, Material[] sourceMats, string baseName,
            string saveFolder, Options opts, List<string> outAssetPaths)
        {
            var result = new Result { ok = false };
            if (sourceMesh == null) { result.error = "source mesh is null"; return result; }
            if (string.IsNullOrEmpty(saveFolder)) { result.error = "save folder is empty"; return result; }

            Bounds b = sourceMesh.bounds;
            int axis = (opts.axisOverride >= 0 && opts.axisOverride <= 2)
                ? opts.axisOverride
                : DetectThinAxis(b);

            // 撮影。カメラの実 right/up と、それに沿った平面サイズ(planeW/planeH)を受け取り、
            // 板ポリ・UV・キャンバスを同じ基底に合わせる（向き・縦横ズレを防ぐ）。
            Vector3 usedRight, usedUp, usedNormal;
            float planeW, planeH;
            Texture2D captured = CaptureOrtho(sourceMesh, sourceMats, b, axis,
                opts.resolution, opts.padding,
                out usedRight, out usedUp, out usedNormal,
                out planeW, out planeH, out string capErr);
            if (captured == null) { result.error = capErr ?? "capture failed"; return result; }

            string safe = SanitizeFileName(baseName);

            // --- PNG 保存 ---
            string pngPath = AssetDatabase.GenerateUniqueAssetPath(
                (saveFolder + "/" + safe + "_BB.png").Replace('\\', '/'));
            try
            {
                File.WriteAllBytes(Path.GetFullPath(pngPath), captured.EncodeToPNG());
            }
            catch (System.Exception ex)
            {
                Object.DestroyImmediate(captured);
                result.error = "PNG write failed: " + ex.Message;
                return result;
            }
            Object.DestroyImmediate(captured);

            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImport(pngPath);
            var savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            outAssetPaths.Add(pngPath);

            // --- マテリアル (Standard Cutout) ---
            var mat = CreateCutoutMaterial(savedTex, opts.cutoff);
            mat.name = safe + "_BB";
            string matPath = AssetDatabase.GenerateUniqueAssetPath(
                (saveFolder + "/" + safe + "_BB.mat").Replace('\\', '/'));
            AssetDatabase.CreateAsset(mat, matPath);
            outAssetPaths.Add(matPath);

            // --- 板ポリ ---
            var quad = BuildQuad(b.center, usedRight, usedUp, usedNormal, planeW, planeH, opts.twoSided);
            quad.name = safe + "_BB";
            string quadPath = AssetDatabase.GenerateUniqueAssetPath(
                (saveFolder + "/" + safe + "_BB.asset").Replace('\\', '/'));
            AssetDatabase.CreateAsset(quad, quadPath);
            outAssetPaths.Add(quadPath);

            result.ok = true;
            result.quad = quad;
            result.material = mat;
            result.texture = savedTex;
            return result;
        }

        // ===================================================================
        //  撮影
        // ===================================================================

        private static Texture2D CaptureOrtho(Mesh mesh, Material[] mats, Bounds b, int axis,
            int resolution, float padding,
            out Vector3 usedRight, out Vector3 usedUp, out Vector3 usedNormal,
            out float planeW, out float planeH, out string err)
        {
            err = null;
            usedRight = Vector3.right;
            usedUp = Vector3.up;
            usedNormal = AxisVector(axis);
            planeW = 0f;
            planeH = 0f;

            GameObject root = null;
            RenderTexture rt = null;
            RenderTexture prevActive = RenderTexture.active;
            Material fallbackMat = null;

            try
            {
                Vector3 axisVec = AxisVector(axis);
                float thinExtent = b.size[axis];

                root = new GameObject("~LODBillboardBakeRoot") { hideFlags = HideFlags.HideAndDontSave };
                root.transform.position = FarOffset;

                // 撮影対象（bounds.center を root 原点に合わせる）
                var meshGO = new GameObject("~mesh") { hideFlags = HideFlags.HideAndDontSave };
                meshGO.transform.SetParent(root.transform, false);
                meshGO.transform.localPosition = -b.center;
                SetLayerRecursive(meshGO, CaptureLayer);
                var mf = meshGO.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = meshGO.AddComponent<MeshRenderer>();
                if (mats != null && mats.Length > 0)
                {
                    mr.sharedMaterials = mats;
                }
                else
                {
                    fallbackMat = new Material(Shader.Find("Standard"));
                    mr.sharedMaterial = fallbackMat;
                }

                // ライト（撮影面に正対させ、平板の表面を均一に照らす）
                var keyGO = new GameObject("~key") { hideFlags = HideFlags.HideAndDontSave };
                keyGO.transform.SetParent(root.transform, false);
                var key = keyGO.AddComponent<Light>();
                key.type = LightType.Directional;
                key.intensity = 1.1f;
                key.transform.rotation = Quaternion.LookRotation(-axisVec, Vector3.up);

                // カメラ
                var camGO = new GameObject("~cam") { hideFlags = HideFlags.HideAndDontSave };
                camGO.transform.SetParent(root.transform, false);
                var cam = camGO.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << CaptureLayer;
                cam.nearClipPlane = 0.001f;
                cam.allowMSAA = true;
                cam.allowHDR = false;

                // 視線方向を決め、カメラの実 right/up を先に確定する。
                // 薄い軸が Y（床に寝た板）のときだけ up を Z に、それ以外はワールドYを縦にする
                // ＝シーンで見たままの向きでキャプチャされる。
                Vector3 upHint = (axis == 1) ? Vector3.forward : Vector3.up;
                camGO.transform.rotation = Quaternion.LookRotation(-axisVec, upHint);
                usedRight = cam.transform.right;
                usedUp = cam.transform.up;
                usedNormal = -cam.transform.forward; // カメラに向く面の法線

                // 平面サイズは「カメラ right/up に沿った bounds extent」。
                // これでキャプチャの縦横と板ポリの縦横が必ず一致する。
                planeW = b.size[AxisIndexOf(usedRight)];
                planeH = b.size[AxisIndexOf(usedUp)];
                if (planeW < 1e-5f || planeH < 1e-5f) { err = "degenerate (zero-area) bounds"; return null; }

                // 解像度（長辺 = resolution、アスペクト維持）
                int res = Mathf.Clamp(resolution, 16, 4096);
                int resW, resH;
                if (planeW >= planeH)
                {
                    resW = res;
                    resH = Mathf.Clamp(Mathf.RoundToInt(res * planeH / planeW), 4, res);
                }
                else
                {
                    resH = res;
                    resW = Mathf.Clamp(Mathf.RoundToInt(res * planeW / planeH), 4, res);
                }

                float pad = Mathf.Clamp(padding, 0f, 0.5f);
                cam.aspect = planeW / planeH;
                cam.orthographicSize = 0.5f * planeH * (1f + pad * 2f);
                cam.farClipPlane = thinExtent * 8f + Mathf.Max(planeW, planeH) + 10f;

                float dist = thinExtent + Mathf.Max(planeW, planeH) + 1f;
                camGO.transform.position = FarOffset + axisVec * dist;

                int aa = QualitySettings.antiAliasing;
                aa = aa <= 1 ? 4 : Mathf.Min(8, aa);
                rt = RenderTexture.GetTemporary(resW, resH, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, aa);
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;

                RenderTexture.active = rt;
                var tex = new Texture2D(resW, resH, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, resW, resH), 0, 0);
                tex.Apply();
                return tex;
            }
            catch (System.Exception ex)
            {
                err = ex.Message;
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (fallbackMat != null) Object.DestroyImmediate(fallbackMat);
                if (root != null) Object.DestroyImmediate(root);
            }
        }

        // ===================================================================
        //  板ポリ生成
        // ===================================================================

        private static Mesh BuildQuad(Vector3 center, Vector3 right, Vector3 up, Vector3 normal,
            float w, float h, bool twoSided)
        {
            Vector3 r = right * (w * 0.5f);
            Vector3 u = up * (h * 0.5f);
            Vector3 bl = center - r - u;
            Vector3 br = center + r - u;
            Vector3 tr = center + r + u;
            Vector3 tl = center - r + u;

            var mesh = new Mesh();

            if (!twoSided)
            {
                mesh.vertices = new[] { bl, br, tr, tl };
                mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                mesh.normals = new[] { normal, normal, normal, normal };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            }
            else
            {
                // 表 4 頂点 + 裏 4 頂点（法線・巻き順を反転）
                mesh.vertices = new[] { bl, br, tr, tl, bl, br, tr, tl };
                mesh.uv = new[]
                {
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
                };
                mesh.normals = new[] { normal, normal, normal, normal, -normal, -normal, -normal, -normal };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 };
            }

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ===================================================================
        //  マテリアル / テクスチャ
        // ===================================================================

        private static Material CreateCutoutMaterial(Texture tex, float cutoff)
        {
            var m = new Material(Shader.Find("Standard"));
            // Standard を Cutout モードへ
            m.SetFloat("_Mode", 1f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 2450;
            m.SetFloat("_Cutoff", Mathf.Clamp01(cutoff));
            m.SetFloat("_Glossiness", 0f);
            m.SetFloat("_Metallic", 0f);
            m.mainTexture = tex;
            return m;
        }

        private static void ConfigureTextureImport(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            ti.textureType = TextureImporterType.Default;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }

        // ===================================================================
        //  ヘルパー
        // ===================================================================

        private static Vector3 AxisVector(int axis)
        {
            switch (axis)
            {
                case 0: return Vector3.right;
                case 1: return Vector3.up;
                default: return Vector3.forward;
            }
        }

        /// <summary>単位カーディナル(±X/±Y/±Z)ベクトルが指す軸 index を返す。</summary>
        private static int AxisIndexOf(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return 0;
            if (ay >= az) return 1;
            return 2;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Billboard";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
