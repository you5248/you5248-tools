using System.Collections.Generic;

namespace you5248.LODGenerator
{
    public enum LODGenLanguage { English, Japanese }

    /// <summary>
    /// EN/JP localization dictionary for LOD Generator.
    /// </summary>
    public static class LODGeneratorLocalization
    {
        private static LODGenLanguage _current = LODGenLanguage.Japanese;
        public static LODGenLanguage Current
        {
            get => _current;
            set => _current = value;
        }

        private static readonly Dictionary<string, string> EN = new Dictionary<string, string>
        {
            // Window
            ["WindowTitle"]             = "LOD Generator",
            ["Language"]                = "Language",

            // Target
            ["SectionTarget"]           = "Target",
            ["SelectedObjects"]         = "Selected: {0} object(s) ({1} mesh(es))",
            ["IncludeChildren"]         = "Include Children",
            ["SelectSameMesh"]          = "Select All with Same Mesh",
            ["SelectSameMeshResult"]    = "Selected {0} objects ({1} unique mesh(es), {2} added)",
            ["NoMeshSelected"]          = "Select GameObjects with MeshRenderer or SkinnedMeshRenderer.",

            // Preset
            ["SectionPreset"]           = "Preset",
            ["PresetLabel"]             = "Preset",
            ["PresetSave"]              = "Save",
            ["PresetSaveAs"]            = "Save As",
            ["PresetDelete"]            = "Delete",
            ["PresetUpdated"]           = "Preset \"{0}\" updated.",
            ["PresetSaveTitle"]         = "Save Preset",
            ["PresetSaveName"]          = "Preset name:",
            ["PresetDeleteConfirm"]     = "Delete preset \"{0}\"?",
            ["PresetDefault"]           = "Default",

            // LOD Settings
            ["SectionLOD"]              = "LOD Settings",
            ["LODCount"]                = "LOD Levels",
            ["LODOriginal"]             = "LOD0 (Original)",
            ["LODLevel"]                = "LOD{0}",
            ["Reduction"]               = "Reduction %",
            ["ScreenPercent"]           = "Screen %",
            ["EnableCulled"]            = "Culled (Hide at distance)",
            ["SkipDuplicateLODs"]       = "Skip levels with no reduction",
            ["LODMode"]                 = "Mode",
            ["BillboardLevelInfo"]      = "Billboard: replaces the mesh with a flat quad + captured cutout texture. Best for thin meshes at far LOD levels.",
            ["CubeLevelInfo"]           = "Cube: replaces the mesh with a bounds-fitted 12-tri cube reusing the first source material. Best for box-like meshes (crates, boxes) at far LOD levels. Each face corner inherits the UV of the nearest real vertex facing the same way, so box meshes keep their original face textures.",
            ["ReplaceCube"]             = "Replace with Cube (12 tris)",
            ["ReplaceCubeInfo"]         = "Swaps the mesh with a bounds-fitted cube (first material only; extra material slots are trimmed). Quality / structural settings are ignored. Preview shows the simplified mesh, not the cube.",
            ["BillboardSuggest"]        = "This mesh is thin (min/max extent ≈ {0}%). A billboard may look better here than mesh reduction.",
            ["BillboardSwitchButton"]   = "Use Billboard for this level",
            ["BillboardSettings"]       = "Billboard Settings",
            ["BillboardResolution"]     = "Texture Resolution",
            ["BillboardCutoff"]         = "Alpha Cutoff",
            ["BillboardPadding"]        = "Padding",
            ["BillboardTwoSided"]       = "Two-Sided (visible from behind)",
            ["StructuralSimplify"]      = "Structural Simplify (merge coplanar / bevels)",
            ["StructuralDesc"]          = "Merges coplanar faces and welds bevel edges while keeping original UVs / normal maps. Good for over-built boxy parts (e.g. tatami). Set Reduction to 0% to apply structural only.",
            ["StructuralCoplanarAngle"] = "Coplanar Angle (deg)",
            ["StructuralBevelLength"]   = "Bevel Length (0 = off)",
            ["StructuralFlatShade"]     = "Flat-Shade Regions",
            ["StructuralEnable"]        = "Enable structural simplify",
            ["PreserveHeader"]          = "Preserve Options (mesh simplify)",
            ["PreserveBorderEdges"]     = "Preserve Border Edges (prevent holes)",
            ["PreserveUVSeamEdges"]     = "Preserve UV Seam Edges",
            ["PreserveUVFoldoverEdges"] = "Preserve UV Foldover Edges",
            ["PreserveSurfaceCurvature"]= "Preserve Surface Curvature",

            // Crossfade
            ["SectionCrossfade"]        = "Cross Fade",
            ["CrossfadeEnable"]         = "Enable",
            ["CrossfadeWidth"]          = "Transition Width",
            ["CrossfadeMode"]           = "Mode",
            ["CrossfadeAnimate"]        = "Animate (dither fade)",
            ["CrossfadeWidthNote"]      = "Transition Width only applies in SpeedTree (non-animated) mode.",

            // Save
            ["SectionSave"]             = "Save Location",
            ["SaveSameFolder"]          = "Same folder as source mesh",
            ["SaveCustomFolder"]        = "Custom folder",
            ["SaveFolderLabel"]         = "Folder",
            ["SaveBrowse"]              = "...",
            ["SaveInvalidPath"]         = "Save folder must be inside this project's Assets folder.",
            ["SaveOutsideAssets"]       = "Please choose a folder inside this project's Assets folder.",

            // Generate
            ["GenerateButton"]          = "Generate LODs",
            ["GenerateButtonReady"]     = "▶  Generate LODs for {0} mesh(es)",
            ["GenerateProgress"]        = "Generating LODs... ({0}/{1})",
            ["GenerateComplete"]        = "LOD generation complete! Processed {0} object(s).",
            ["GenerateSkipped"]         = "Skipped (LODGroup exists): {0}",
            ["GenerateOverwrite"]       = "LODGroup already exists on \"{0}\". Overwrite?",
            ["GenerateOverwriteBatch"]  = "LODGroup already exists on \"{0}\" (and {1} other object(s) with existing LODGroups).\n\nHow do you want to handle them?",
            ["OverwriteAll"]            = "Overwrite All",
            ["SkipAll"]                 = "Skip All",
            ["OverwriteOne"]            = "This One Only",
            ["GenerateNoMesh"]          = "No meshes to process.",

            // Errors
            ["ErrorNoMesh"]             = "Could not get mesh from {0}",
            ["ErrorSimplify"]           = "Failed to simplify mesh: {0}",

            // Tabs
            ["TabGenerate"]             = "Generate",
            ["TabReplace"]              = "Replace",
            ["TabUtility"]              = "Utility",

            // Replace (low-poly, no LODGroup)
            ["SectionReplace"]          = "Low-Poly Replace",
            ["ReplaceDesc"]             = "Replaces the selected mesh(es) directly with a simplified low-poly version — no LODGroup and no child objects are created.\n\nThe original mesh asset is kept on disk; only the renderer's mesh reference is updated (Undo supported). Objects sharing the same source mesh reuse a single low-poly asset.",
            ["ReplaceButton"]           = "Replace with Low-Poly",
            ["ReplaceButtonReady"]      = "▶  Replace {0} mesh(es) with Low-Poly",
            ["ReplaceProgress"]         = "Replacing... ({0}/{1})",
            ["ReplaceComplete"]         = "Replacement complete! Processed {0} object(s).",
            ["ReplaceConfirm"]          = "Replace the mesh on {0} object(s) with a simplified low-poly version?\n\nThe original mesh asset stays on disk; the renderer will point to the new low-poly asset.\n\nThis operation supports Undo.",

            // Before / After preview
            ["PreviewHeader"]           = "Before / After",
            ["PreviewSelectFirst"]      = "Select a mesh to see a before/after preview.",
            ["PreviewBefore"]           = "Before",
            ["PreviewAfter"]            = "After",
            ["PreviewSourceInfo"]       = "Before: {0:N0} tris / {1:N0} verts",
            ["PreviewTargetInfo"]       = "Target: ~{0:N0} tris (reduction {1:P0})",
            ["PreviewResultInfo"]       = "After: {0:N0} tris / {1:N0} verts",
            ["PreviewActualReduction"]  = "Actual reduction: {0:P1}",
            ["PreviewComputeButton"]    = "Compute / Update Preview",
            ["PreviewMultiNote"]        = "{0} meshes selected — previewing the first one.",
            ["PreviewStale"]            = "Selection or reduction changed. Press 'Compute / Update Preview' to refresh.",
            ["PreviewLimitNote"]        = "Couldn't reduce much further — topology / preserve-option limit reached. Turn off Preserve Border / UV Seam edges for more reduction.",
            ["PreviewDragHint"]         = "Drag the view to rotate · drag the bar below to resize",
            ["PreviewNotReadable"]      = "Mesh is not readable — enable Read/Write in its import settings to preview.",

            // Utility
            ["UtilityDesc"]             = "When you generate LODs separately for multiple objects sharing the same source mesh (e.g. 100 identical chairs), each gets its own copy of the simplified mesh asset — increasing build size unnecessarily.\n\nThis utility scans the scene for duplicate LOD mesh assets and consolidates them, keeping one copy and updating all references.\n\nNote: Batch generation already shares assets automatically within one run. Use this for duplicates from separate runs.",
            ["UtilityScanButton"]       = "Scan Scene",
            ["UtilityNoResults"]        = "Click 'Scan Scene' to search for duplicate LOD mesh assets.",
            ["UtilityNoDuplicates"]     = "No duplicate LOD mesh assets found in the scene.",
            ["UtilityScanSummary"]      = "{0} group(s) found. {1} duplicate asset(s) can be removed.",
            ["UtilityGroupHeader"]      = "{0} ({1} verts) — {2} assets → 1",
            ["UtilityKeep"]             = "Keep: {0} ({1} renderers)",
            ["UtilityRemove"]           = "Remove: {0} ({1} renderers)",
            ["UtilityConsolidateButton"]= "Consolidate All",
            ["UtilityConsolidateConfirm"]= "Consolidate {0} duplicate group(s)?\n\n{1} renderer reference(s) will be updated and up to {2} asset file(s) will be deleted.\n\nReference changes support Undo.\nDeleted assets can be regenerated.",
            ["UtilityConsolidateComplete"]= "Done! {0} reference(s) updated, {1} asset(s) deleted.",

            // Utility — Unused Assets
            ["UtilitySectionOrphan"]     = "Unused LOD Assets",
            ["UtilityOrphanDesc"]        = "LOD mesh assets (.asset) that exist in the project but are not referenced by any renderer in the current scene. These are left over from previous LOD generations and can be safely deleted.",
            ["UtilityOrphanScanButton"]  = "Scan Project",
            ["UtilityOrphanNoResults"]   = "Click 'Scan Project' to search for unused LOD mesh assets across the project.",
            ["UtilityOrphanNoneFound"]   = "No unused LOD mesh assets found.",
            ["UtilityOrphanSummary"]     = "{0} unused LOD asset(s) found.",
            ["UtilityOrphanDeleteButton"]= "Delete All Unused Assets",
            ["UtilityOrphanDeleteConfirm"]= "Delete {0} unused LOD asset(s)?\n\nThese files are not referenced by any renderer in the current scene.\n\nThis operation cannot be undone.",
            ["UtilityOrphanDeleteComplete"]= "Deleted {0} asset(s).",
        };

        private static readonly Dictionary<string, string> JP = new Dictionary<string, string>
        {
            // Window
            ["WindowTitle"]             = "LOD Generator",
            ["Language"]                = "言語",

            // Target
            ["SectionTarget"]           = "対象",
            ["SelectedObjects"]         = "選択中: {0} オブジェクト ({1} メッシュ)",
            ["IncludeChildren"]         = "子オブジェクトを含める",
            ["SelectSameMesh"]          = "同じメッシュを持つオブジェクトをすべて選択",
            ["SelectSameMeshResult"]    = "{0} オブジェクトを選択しました({1} 種類のメッシュ、{2} 個追加)",
            ["NoMeshSelected"]          = "MeshRenderer または SkinnedMeshRenderer を持つ GameObject を選択してください。",

            // Preset
            ["SectionPreset"]           = "プリセット",
            ["PresetLabel"]             = "プリセット",
            ["PresetSave"]              = "保存",
            ["PresetSaveAs"]            = "別名",
            ["PresetDelete"]            = "削除",
            ["PresetUpdated"]           = "プリセット「{0}」を上書き保存しました。",
            ["PresetSaveTitle"]         = "プリセットを保存",
            ["PresetSaveName"]          = "プリセット名:",
            ["PresetDeleteConfirm"]     = "プリセット「{0}」を削除しますか?",
            ["PresetDefault"]           = "デフォルト",

            // LOD Settings
            ["SectionLOD"]              = "LOD 設定",
            ["LODCount"]                = "LOD 段階数",
            ["LODOriginal"]             = "LOD0 (元メッシュ)",
            ["LODLevel"]                = "LOD{0}",
            ["Reduction"]               = "削減率 %",
            ["ScreenPercent"]           = "画面比率 %",
            ["EnableCulled"]            = "Culled (遠距離で非表示)",
            ["SkipDuplicateLODs"]       = "ポリゴンが減らない段階を省略",
            ["LODMode"]                 = "生成方式",
            ["BillboardLevelInfo"]      = "板ポリ: メッシュを1枚の板＋カットアウトテクスチャに置換します。遠距離LODの薄いメッシュ向け。",
            ["CubeLevelInfo"]           = "キューブ: メッシュをバウンズにフィットした12triの箱に置換します（元マテリアルの先頭を流用）。遠距離LODの箱物（クレート・段ボール等）向け。各面の4隅は「同方向を向く実頂点の中で最も近い物のUV」を継承するため、箱メッシュなら元の面テクスチャがほぼそのまま保たれます。",
            ["ReplaceCube"]             = "キューブに置換 (12tri)",
            ["ReplaceCubeInfo"]         = "メッシュをバウンズフィットのキューブへ丸ごと差し替えます（マテリアルは先頭1本に整理）。削減率・構造簡略化の設定は無視されます。プレビューは簡略化メッシュ表示のままです。",
            ["BillboardSuggest"]        = "このメッシュは薄いです (min/max比 ≈ {0}%)。簡略化より板ポリの方がきれいな場合があります。",
            ["BillboardSwitchButton"]   = "この段を板ポリにする",
            ["BillboardSettings"]       = "板ポリ設定",
            ["BillboardResolution"]     = "テクスチャ解像度",
            ["BillboardCutoff"]         = "アルファカットオフ",
            ["BillboardPadding"]        = "余白",
            ["BillboardTwoSided"]       = "両面 (裏からも見える)",
            ["StructuralSimplify"]      = "構造簡略化 (平面結合・ベベル)",
            ["StructuralDesc"]          = "同一平面の面を結合し、ベベルのエッジを溶接します。元のUV・法線マップは維持。畳のような作り込まれた箱状パーツに有効。削減率0%にすれば構造簡略化のみ適用。",
            ["StructuralCoplanarAngle"] = "平面結合の角度 (度)",
            ["StructuralBevelLength"]   = "ベベル長さ (0=無効)",
            ["StructuralFlatShade"]     = "領域をフラットシェード",
            ["StructuralEnable"]        = "構造簡略化を有効にする",
            ["PreserveHeader"]          = "保持オプション (メッシュ簡略化)",
            ["PreserveBorderEdges"]     = "境界辺を保持 (穴の防止)",
            ["PreserveUVSeamEdges"]     = "UV縫い目を保持",
            ["PreserveUVFoldoverEdges"] = "UV折り返しを保持",
            ["PreserveSurfaceCurvature"]= "表面の曲率を保持",

            // Crossfade
            ["SectionCrossfade"]        = "クロスフェード",
            ["CrossfadeEnable"]         = "有効",
            ["CrossfadeWidth"]          = "遷移幅",
            ["CrossfadeMode"]           = "モード",
            ["CrossfadeAnimate"]        = "アニメーション(ディザ)",
            ["CrossfadeWidthNote"]      = "遷移幅は SpeedTree モード（アニメーションOFF時）のみ有効です。",

            // Save
            ["SectionSave"]             = "保存先",
            ["SaveSameFolder"]          = "元メッシュと同じフォルダ",
            ["SaveCustomFolder"]        = "指定フォルダ",
            ["SaveFolderLabel"]         = "フォルダ",
            ["SaveBrowse"]              = "...",
            ["SaveInvalidPath"]         = "保存先は Assets フォルダ配下を指定してください。",
            ["SaveOutsideAssets"]       = "プロジェクトの Assets フォルダ内のフォルダを選択してください。",

            // Generate
            ["GenerateButton"]          = "LOD を生成",
            ["GenerateButtonReady"]     = "▶  {0} 個のメッシュを LOD 化",
            ["GenerateProgress"]        = "LOD 生成中... ({0}/{1})",
            ["GenerateComplete"]        = "LOD 生成完了! {0} オブジェクトを処理しました。",
            ["GenerateSkipped"]         = "スキップ (LODGroup 設定済み): {0}",
            ["GenerateOverwrite"]       = "「{0}」には既に LODGroup があります。上書きしますか?",
            ["GenerateOverwriteBatch"]  = "「{0}」には既に LODGroup があります (他にも {1} 個の既存LODGroupがあります)。\n\nどのように処理しますか?",
            ["OverwriteAll"]            = "すべて上書き",
            ["SkipAll"]                 = "すべてスキップ",
            ["OverwriteOne"]            = "この1件だけ上書き",
            ["GenerateNoMesh"]          = "処理対象のメッシュがありません。",

            // Errors
            ["ErrorNoMesh"]             = "{0} からメッシュを取得できませんでした",
            ["ErrorSimplify"]           = "メッシュの簡略化に失敗しました: {0}",

            // Tabs
            ["TabGenerate"]             = "生成",
            ["TabReplace"]              = "ローポリ置換",
            ["TabUtility"]              = "ユーティリティ",

            // Replace (low-poly, no LODGroup)
            ["SectionReplace"]          = "ローポリ置換",
            ["ReplaceDesc"]             = "選択したメッシュを、LODGroupや子オブジェクトを作らずに、簡略化したローポリメッシュへ直接置き換えます。\n\n元のメッシュアセットはディスク上に残り、レンダラーのメッシュ参照のみが更新されます(Undo対応)。同じ元メッシュを持つオブジェクトは1つのローポリアセットを共有します。",
            ["ReplaceButton"]           = "ローポリに置き換え",
            ["ReplaceButtonReady"]      = "▶  {0} 個のメッシュをローポリに置換",
            ["ReplaceProgress"]         = "置換中... ({0}/{1})",
            ["ReplaceComplete"]         = "置換完了! {0} オブジェクトを処理しました。",
            ["ReplaceConfirm"]          = "{0} 個のオブジェクトのメッシュを、簡略化したローポリメッシュに置き換えますか?\n\n元のメッシュアセットはディスク上に残り、レンダラーは新しいローポリアセットを参照します。\n\nこの操作はUndoに対応しています。",

            // Before / After preview
            ["PreviewHeader"]           = "ビフォーアフター",
            ["PreviewSelectFirst"]      = "メッシュを選択するとビフォーアフターを確認できます。",
            ["PreviewBefore"]           = "変更前",
            ["PreviewAfter"]            = "変更後",
            ["PreviewSourceInfo"]       = "変更前: {0:N0} 三角形 / {1:N0} 頂点",
            ["PreviewTargetInfo"]       = "目標: 約 {0:N0} 三角形 (削減率 {1:P0})",
            ["PreviewResultInfo"]       = "変更後: {0:N0} 三角形 / {1:N0} 頂点",
            ["PreviewActualReduction"]  = "実際の削減率: {0:P1}",
            ["PreviewComputeButton"]    = "ビフォーアフターを計算 / 更新",
            ["PreviewMultiNote"]        = "{0} メッシュ選択中 — 最初の1つをプレビュー表示します。",
            ["PreviewStale"]            = "選択または削減率が変わりました。「ビフォーアフターを計算 / 更新」で更新してください。",
            ["PreviewLimitNote"]        = "これ以上はほとんど削減できませんでした — トポロジーや保持オプションの限界です。境界辺/UV縫い目の保持をオフにすると更に削減できる場合があります。",
            ["PreviewDragHint"]         = "ビューをドラッグで回転 ・ 下のバーをドラッグで高さ変更",
            ["PreviewNotReadable"]      = "メッシュが読み取り不可です — プレビューにはインポート設定で Read/Write を有効にしてください。",

            // Utility
            ["UtilityDesc"]             = "同じ元メッシュを持つ複数オブジェクト (例: 同じ椅子100個) にLODを個別生成すると、同一内容のLODメッシュアセットがオブジェクト数分作られ、ビルド容量が増大します。\n\nこのユーティリティはシーン内の重複LODメッシュアセットを検出し、1つに統合して不要アセットを削除します。\n\n※ 一括生成時は同一メッシュを自動共有します。別々に実行した場合の重複処理にお使いください。",
            ["UtilityScanButton"]       = "シーンをスキャン",
            ["UtilityNoResults"]        = "「シーンをスキャン」で重複LODメッシュアセットを検索します。",
            ["UtilityNoDuplicates"]     = "重複するLODメッシュアセットは見つかりませんでした。",
            ["UtilityScanSummary"]      = "{0} グループ検出、{1} 個の重複アセットを削除できます。",
            ["UtilityGroupHeader"]      = "{0} ({1} 頂点) — {2} アセット → 1",
            ["UtilityKeep"]             = "保持: {0} ({1} レンダラー)",
            ["UtilityRemove"]           = "削除予定: {0} ({1} レンダラー)",
            ["UtilityConsolidateButton"]= "すべて統合",
            ["UtilityConsolidateConfirm"]= "{0} 個の重複グループを統合しますか?\n\n{1} 個のレンダラー参照が更新され、最大 {2} 個のアセットファイルが削除されます。\n\n参照変更はUndo対応です。\n削除されたアセットは再生成できます。",
            ["UtilityConsolidateComplete"]= "統合完了! {0} 個の参照を更新、{1} 個のアセットを削除しました。",

            // Utility — Unused Assets
            ["UtilitySectionOrphan"]     = "未使用LODアセット",
            ["UtilityOrphanDesc"]        = "プロジェクト内に存在するが、現在のシーンのどのレンダラーも参照していないLODメッシュアセット (.asset) です。過去のLOD生成で残ったファイルで、削除しても安全です。",
            ["UtilityOrphanScanButton"]  = "プロジェクトをスキャン",
            ["UtilityOrphanNoResults"]   = "「プロジェクトをスキャン」でプロジェクト全体の未使用LODアセットを検索します。",
            ["UtilityOrphanNoneFound"]   = "未使用のLODメッシュアセットは見つかりませんでした。",
            ["UtilityOrphanSummary"]     = "{0} 個の未使用LODアセットが見つかりました。",
            ["UtilityOrphanDeleteButton"]= "未使用アセットをすべて削除",
            ["UtilityOrphanDeleteConfirm"]= "{0} 個の未使用LODアセットを削除しますか?\n\nこれらのファイルは現在のシーンのどのレンダラーも参照していません。\n\nこの操作は元に戻せません。",
            ["UtilityOrphanDeleteComplete"]= "{0} 個のアセットを削除しました。",
        };

        /// <summary>Get a localized string by key.</summary>
        public static string Get(string key)
        {
            var dict = _current == LODGenLanguage.Japanese ? JP : EN;
            return dict.TryGetValue(key, out string val) ? val : key;
        }

        /// <summary>Get a formatted localized string.</summary>
        public static string Get(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }
}
