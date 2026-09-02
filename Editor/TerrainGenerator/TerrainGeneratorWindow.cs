using System;
using UnityEditor;
using UnityEngine;

namespace you5248.TerrainGenerator
{
    public class TerrainGeneratorWindow : EditorWindow
    {
        enum Language { Japanese, English }

        enum Preset
        {
            FlatPlains,   // 平地
            RollingHills, // なだらかな丘
            Forest,       // 森林
            Mountains,    // 山岳
            Island,       // 島（海側が低い）
        }

        [SerializeField] Language _lang = Language.Japanese;
        [SerializeField] string _terrainName = "GeneratedTerrain";
        [SerializeField] Preset _preset = Preset.Forest;
        [SerializeField] int _seed = 12345;
        [SerializeField] float _width = 500f;
        [SerializeField] float _maxHeight = 80f;
        [SerializeField] float _length = 500f;
        [SerializeField] int _resIndex = 2;
        [SerializeField] Vector3 _position = Vector3.zero;
        [SerializeField] bool _showAdvanced = false;
        [SerializeField] float _frequency = 0.004f;
        [SerializeField] int _octaves = 6;
        [SerializeField] float _lacunarity = 2f;
        [SerializeField] float _persistence = 0.5f;
        [SerializeField] bool _dryRun = false;

        static readonly int[] Resolutions = { 129, 257, 513, 1025 };
        static readonly string[] ResLabels = { "129", "257", "513 (推奨)", "1025" };

        Vector2 _scroll;
        Texture2D _previewTex;
        bool _previewDirty = true;

        string JP(string ja, string en) => _lang == Language.Japanese ? ja : en;

        [MenuItem("you5248/Terrain Generator")]
        static void Open()
        {
            var w = GetWindow<TerrainGeneratorWindow>();
            w.titleContent = new GUIContent("Terrain Generator");
            w.minSize = new Vector2(360, 540);
        }

        void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            EditorGUILayout.Space(6);
            DrawBasicSettings();
            EditorGUILayout.Space(4);
            DrawAdvancedFoldout();
            EditorGUILayout.Space(10);
            DrawHeightmapPreview();
            EditorGUILayout.Space(10);
            DrawFooter();

            EditorGUILayout.EndScrollView();
            if (EditorGUI.EndChangeCheck())
                _previewDirty = true;
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(JP("テレイン生成ツール", "Terrain Generator"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            var newLang = (Language)EditorGUILayout.EnumPopup(_lang, GUILayout.Width(80));
            if (newLang != _lang) _lang = newLang;
            EditorGUILayout.EndHorizontal();
        }

        void DrawBasicSettings()
        {
            EditorGUILayout.LabelField(JP("基本設定", "Basic Settings"), EditorStyles.boldLabel);
            _terrainName = EditorGUILayout.TextField(JP("テレイン名", "Name"), _terrainName);
            _position = EditorGUILayout.Vector3Field(JP("配置位置", "Position"), _position);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(JP("地形プリセット", "Preset"), EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _preset = (Preset)EditorGUILayout.EnumPopup(JP("タイプ", "Type"), _preset);
            if (EditorGUI.EndChangeCheck()) ApplyPreset();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(JP("サイズ", "Size"), EditorStyles.boldLabel);
            _width     = Mathf.Max(10f, EditorGUILayout.FloatField(JP("幅 X", "Width X"), _width));
            _length    = Mathf.Max(10f, EditorGUILayout.FloatField(JP("奥行き Z", "Length Z"), _length));
            _maxHeight = Mathf.Max(1f,  EditorGUILayout.FloatField(JP("最大高さ Y", "Max Height Y"), _maxHeight));
            _resIndex  = EditorGUILayout.Popup(JP("解像度", "Resolution"), _resIndex, ResLabels);
            _seed      = EditorGUILayout.IntField(JP("シード値", "Seed"), _seed);
        }

        void DrawAdvancedFoldout()
        {
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, JP("詳細設定（ノイズ）", "Advanced (Noise)"), true);
            if (!_showAdvanced) return;

            EditorGUI.indentLevel++;
            _frequency   = EditorGUILayout.Slider(JP("周波数", "Frequency"),       _frequency,   0.0005f, 0.02f);
            _octaves     = EditorGUILayout.IntSlider(JP("オクターブ", "Octaves"), _octaves,     1, 10);
            _lacunarity  = EditorGUILayout.Slider(JP("ラクナリティ", "Lacunarity"), _lacunarity, 1f, 4f);
            _persistence = EditorGUILayout.Slider(JP("パーシスタンス", "Persistence"), _persistence, 0.1f, 0.9f);
            EditorGUI.indentLevel--;
        }

        void DrawHeightmapPreview()
        {
            EditorGUILayout.LabelField(JP("ハイトマップ プレビュー", "Heightmap Preview"), EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect previewRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (_previewDirty || _previewTex == null)
            {
                RegeneratePreview(64);
                _previewDirty = false;
            }
            if (_previewTex != null)
                EditorGUI.DrawPreviewTexture(previewRect, _previewTex);
        }

        void DrawFooter()
        {
            _dryRun = EditorGUILayout.Toggle(JP("ドライラン(ログのみ)", "Dry Run (log only)"), _dryRun);

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = _dryRun ? new Color(1f, 0.85f, 0f) : new Color(0.45f, 0.9f, 0.45f);
            string label = _dryRun ? JP("ドライラン実行", "Dry Run") : JP("テレイン生成", "Generate Terrain");
            if (GUILayout.Button(label, GUILayout.Height(36)))
            {
                if (_dryRun) DryRun();
                else Generate();
            }
            GUI.backgroundColor = prevBg;
        }

        void ApplyPreset()
        {
            switch (_preset)
            {
                case Preset.FlatPlains:
                    _frequency = 0.002f; _octaves = 3; _persistence = 0.45f; _lacunarity = 2f;   _maxHeight = 15f;  break;
                case Preset.RollingHills:
                    _frequency = 0.003f; _octaves = 5; _persistence = 0.50f; _lacunarity = 2f;   _maxHeight = 45f;  break;
                case Preset.Forest:
                    _frequency = 0.004f; _octaves = 6; _persistence = 0.50f; _lacunarity = 2f;   _maxHeight = 70f;  break;
                case Preset.Mountains:
                    _frequency = 0.005f; _octaves = 8; _persistence = 0.45f; _lacunarity = 2.2f; _maxHeight = 180f; break;
                case Preset.Island:
                    _frequency = 0.004f; _octaves = 6; _persistence = 0.50f; _lacunarity = 2f;   _maxHeight = 60f;  break;
            }
        }

        // ----------------------------------------------------------------------
        //  Noise
        // ----------------------------------------------------------------------

        float[,] SampleHeights(int res)
        {
            var rng = new System.Random(_seed);
            float ox = (float)(rng.NextDouble() * 99999);
            float oy = (float)(rng.NextDouble() * 99999);

            bool island    = _preset == Preset.Island;
            bool mountains = _preset == Preset.Mountains;
            float center   = res * 0.5f;

            var h = new float[res, res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx = (x + ox) * _frequency;
                    float ny = (y + oy) * _frequency;

                    float v = mountains ? RidgedFBM(nx, ny) : FBM(nx, ny);

                    if (island)
                    {
                        float dx = (x - center) / center;
                        float dy = (y - center) / center;
                        float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 1.15f);
                        falloff *= falloff;
                        v *= falloff;
                    }

                    h[y, x] = Mathf.Clamp01(v);
                }
            }
            return h;
        }

        float FBM(float x, float y)
        {
            float val = 0f, amp = 1f, freq = 1f, max = 0f;
            for (int i = 0; i < _octaves; i++)
            {
                val += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                max += amp;
                amp  *= _persistence;
                freq *= _lacunarity;
            }
            return val / max;
        }

        // 尾根型ノイズ(山岳向け)
        float RidgedFBM(float x, float y)
        {
            float val = 0f, amp = 1f, freq = 1f, max = 0f;
            for (int i = 0; i < _octaves; i++)
            {
                float n = Mathf.PerlinNoise(x * freq, y * freq);
                n = 1f - Mathf.Abs(n * 2f - 1f);
                n = n * n;
                val += n * amp;
                max += amp;
                amp  *= _persistence;
                freq *= _lacunarity;
            }
            return val / max;
        }

        // ----------------------------------------------------------------------
        //  Preview Texture
        // ----------------------------------------------------------------------

        void RegeneratePreview(int size)
        {
            if (_previewTex == null || _previewTex.width != size)
            {
                if (_previewTex != null) DestroyImmediate(_previewTex);
                _previewTex = new Texture2D(size, size, TextureFormat.RGB24, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode   = TextureWrapMode.Clamp,
                };
            }

            float[,] h = SampleHeights(size);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float v = h[y, x];
                    Color col = v < 0.3f
                        ? Color.Lerp(new Color(0.15f, 0.35f, 0.1f), new Color(0.3f, 0.55f, 0.2f), v / 0.3f)
                        : v < 0.7f
                            ? Color.Lerp(new Color(0.3f, 0.55f, 0.2f), new Color(0.7f, 0.6f, 0.4f), (v - 0.3f) / 0.4f)
                            : Color.Lerp(new Color(0.7f, 0.6f, 0.4f), Color.white, (v - 0.7f) / 0.3f);
                    pixels[y * size + x] = col;
                }

            _previewTex.SetPixels(pixels);
            _previewTex.Apply();
        }

        // ----------------------------------------------------------------------
        //  Generate
        // ----------------------------------------------------------------------

        void DryRun()
        {
            Debug.Log($"[TerrainGenerator] DRY RUN - Name: {_terrainName}, Preset: {_preset}, " +
                      $"Size: {_width}x{_maxHeight}x{_length}, Res: {Resolutions[_resIndex]}, Seed: {_seed}");
        }

        void Generate()
        {
            int res = Resolutions[_resIndex];

            var data = new TerrainData
            {
                heightmapResolution = res,
                size = new Vector3(_width, _maxHeight, _length),
            };
            data.SetHeights(0, 0, SampleHeights(res));

            EnsureFolder("Assets/you5248/TerrainGenerator/Generated");
            string path = $"Assets/you5248/TerrainGenerator/Generated/{_terrainName}_Data.asset";
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var terrainObj = Terrain.CreateTerrainGameObject(data);
            terrainObj.name = _terrainName;
            terrainObj.transform.position = _position;
            terrainObj.GetComponent<Terrain>().drawInstanced = true;

            Undo.RegisterCreatedObjectUndo(terrainObj, "Generate Terrain");
            Selection.activeGameObject = terrainObj;
            EditorGUIUtility.PingObject(terrainObj);

            Debug.Log($"[TerrainGenerator] Created '{_terrainName}' at {_position} -> {path}");
        }

        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        void OnDestroy()
        {
            if (_previewTex != null) DestroyImmediate(_previewTex);
        }
    }
}
