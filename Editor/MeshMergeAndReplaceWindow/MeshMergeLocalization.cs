using System.Collections.Generic;

namespace you5248.MeshMergeAndReplace
{

    public enum MeshMergeLang { English, Japanese }

    /// <summary>
    /// EN/JP localization dictionary for the Mesh Merge tool (self-contained).
    /// </summary>
    public static class MeshMergeLocalization
    {
        public static MeshMergeLang Current = MeshMergeLang.Japanese;

        private static readonly Dictionary<string, string> EN = new Dictionary<string, string>
        {
            ["WindowTitle"]          = "Mesh Merge",
            ["Title"]                = "Mesh Merge And Replace (Safe)",
            ["Info"] =
                "Combines the Mesh of the selected GameObject(s) into a new mesh created in the same hierarchy (directly under the same parent).\n" +
                "HideFlags are not used.\n\n" +
                "Include children:\n" +
                " - ON : also targets every MeshRenderer/MeshFilter under the selected roots\n" +
                " - OFF: only the MeshRenderer/MeshFilter on the selected roots themselves\n\n" +
                "Anchor:\n" +
                " - Parent            : bake into parent local space (legacy)\n" +
                " - FirstSelectedRoot : bake relative to the first selected root (avoids offset when children are mixed in)\n\n" +
                "SubMesh:\n" +
                " - Allow ON  : keep SubMeshes when combining (materials array preserved)\n" +
                " - Allow OFF : warn and skip when SubMesh(>1) is detected (never silently flattens)",

            ["SectionOptions"]       = "Options",
            ["DisableOriginal"]      = "Disable original object",
            ["CopyStaticFlags"]      = "Copy Static flags",
            ["AutoName"]             = "Auto generate name",
            ["SaveAsAsset"]          = "Save mesh as .asset",
            ["SaveAsAssetTip"]       = "Saves the merged mesh as an asset file instead of embedding it in the scene. Saved next to the current scene (or Assets/MergedMeshes if the scene is unsaved).",
            ["SaveFolderNote"]       = "Save to: {0}",
            ["InheritLightmaps"]     = "Inherit baked lightmaps",
            ["InheritLightmapsTip"]  = "Carries over baked lightmaps. Bakes each source's lightmapScaleOffset into its UV2 and assigns the shared lightmap index to the merged renderer.",
            ["InheritLightmapsNote"] = "Only carries lightmaps when all sources share one lightmap index. If indices are mixed, or some are not lightmapped, you'll be warned at merge time.",
            ["IncludeChildren"]      = "Include children",
            ["SetEditorOnly"]        = "Set copied original to EditorOnly",

            ["SectionAnchor"]        = "Anchor / Transform",
            ["AnchorMode"]           = "Anchor Mode",

            ["SectionSubMesh"]       = "SubMesh Policy",
            ["AllowSubMeshes"]       = "Allow SubMeshes",
            ["RequireMatMatch"]      = "Require material match",

            ["SectionPreview"]       = "Preview",
            ["PreviewSelectHint"]    = "Select objects that have a MeshFilter to merge.",
            ["PreviewStats"]         = "Targets {0} / Verts {1:N0} / SubMeshes {2} / Materials {3}",
            ["PreviewUInt32"]        = "Vertex count exceeds 65535, so the merged mesh will use a 32-bit index buffer (UInt32).",
            ["PreviewToggle"]        = "3D preview (right-drag = rotate / wheel = zoom)",

            ["SectionSelection"]     = "Selection",
            ["SelectionCount"]       = "{0} objects selected",
            ["ApplyButton"]          = "Apply (Merge Selected Meshes)",

            ["DlgMixedLightmap"] =
                "The selection uses multiple lightmap indices. A single renderer can reference only one lightmap, so it cannot be carried over.\n" +
                "Select only objects that share the same lightmap index.",
            ["DlgPartialLightmap"] =
                "Some objects have no lightmap assigned.\n" +
                "Carrying lightmaps as-is would break the look of those parts.\n" +
                "Select only lightmapped objects to carry lightmaps safely.",
            ["BtnMergeNoLightmap"]   = "Merge without lightmaps",
            ["BtnCancel"]            = "Cancel",
        };

        private static readonly Dictionary<string, string> JP = new Dictionary<string, string>
        {
            ["WindowTitle"]          = "Mesh Merge",
            ["Title"]                = "メッシュ結合 & 置換（安全版）",
            ["Info"] =
                "選択中の GameObject（複数可）を対象に Mesh を結合し、同じ階層（同じ親の直下）に新規メッシュを生成します。\n" +
                "HideFlags は使用しません。\n\n" +
                "子階層を含める:\n" +
                " - ON : 選択ルート配下の MeshRenderer/MeshFilter もすべて対象\n" +
                " - OFF: 選択ルート自身の MeshRenderer/MeshFilter のみ対象\n\n" +
                "Anchor:\n" +
                " - Parent            : 親ローカル空間に焼き込み（従来方式）\n" +
                " - FirstSelectedRoot : 1つ目の選択ルートを基準に焼き込み（子階層混在時の位置ズレ対策）\n\n" +
                "SubMesh:\n" +
                " - 許可ON  : SubMesh を保持して結合（materials 配列も維持）\n" +
                " - 許可OFF : SubMesh(>1)を検出したら警告してスキップ（勝手に潰さない）",

            ["SectionOptions"]       = "オプション",
            ["DisableOriginal"]      = "元オブジェクトを無効化",
            ["CopyStaticFlags"]      = "Static フラグをコピー",
            ["AutoName"]             = "名前を自動生成",
            ["SaveAsAsset"]          = "メッシュを .asset で保存",
            ["SaveAsAssetTip"]       = "結合メッシュをシーン埋め込みではなくアセットファイルとして保存します。現在のシーンと同じフォルダ（未保存なら Assets/MergedMeshes）に保存します。",
            ["SaveFolderNote"]       = "保存先: {0}",
            ["InheritLightmaps"]     = "ベイク済みライトマップを引き継ぐ",
            ["InheritLightmapsTip"]  = "焼き込み済みライトマップを引き継ぎます。各ソースのUV2にlightmapScaleOffsetを焼き込み、結合後のレンダラへ同じライトマップ番号を割り当てます。",
            ["InheritLightmapsNote"] = "同じライトマップ番号のオブジェクト同士のみ引き継ぎます。番号が混在、または一部がライトマップ未設定の場合は結合時に警告します。",
            ["IncludeChildren"]      = "子階層を含める",
            ["SetEditorOnly"]        = "元オブジェクトを EditorOnly にする",

            ["SectionAnchor"]        = "アンカー / トランスフォーム",
            ["AnchorMode"]           = "アンカーモード",

            ["SectionSubMesh"]       = "サブメッシュ設定",
            ["AllowSubMeshes"]       = "サブメッシュを許可",
            ["RequireMatMatch"]      = "マテリアル一致を要求",

            ["SectionPreview"]       = "プレビュー",
            ["PreviewSelectHint"]    = "結合対象（MeshFilter を持つオブジェクト）を選択してください。",
            ["PreviewStats"]         = "対象 {0} / 頂点 {1:N0} / サブメッシュ {2} / マテリアル {3}",
            ["PreviewUInt32"]        = "頂点数が 65535 を超えるため、結合メッシュは 32bit インデックス (UInt32) になります。",
            ["PreviewToggle"]        = "3D プレビュー（右ドラッグ=回転 / ホイール=拡大縮小）",

            ["SectionSelection"]     = "選択",
            ["SelectionCount"]       = "{0} 個を選択中",
            ["ApplyButton"]          = "実行（選択メッシュを結合）",

            ["DlgMixedLightmap"] =
                "複数のライトマップ番号が混在しています。1レンダラは1ライトマップしか参照できないため引き継げません。\n" +
                "同じライトマップ番号のオブジェクトだけを選んでください。",
            ["DlgPartialLightmap"] =
                "一部のオブジェクトにライトマップが設定されていません。\n" +
                "このまま引き継ぐと未設定部分の見た目が崩れます。\n" +
                "ライトマップ済みのオブジェクトだけを選ぶと安全に引き継げます。",
            ["BtnMergeNoLightmap"]   = "ライトマップ無しで結合",
            ["BtnCancel"]            = "キャンセル",
        };

        public static string Get(string key)
        {
            var dict = Current == MeshMergeLang.Japanese ? JP : EN;
            return dict.TryGetValue(key, out string val) ? val : key;
        }

        public static string Get(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }
}
