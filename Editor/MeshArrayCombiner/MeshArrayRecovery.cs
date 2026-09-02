using UnityEngine;
using UnityEditor;
using System.Linq;

namespace you5248.MeshArrayCombiner
{

    // MeshArrayCombiner の Revert 事故からの復旧補助。
    // CreateNew 変換では元オブジェクトは「無効化されただけ・無傷」でシーンに残っているので、
    // それを探して再有効化し、壊れた結合結果（Missing）を削除すれば復旧できる。
    public static class MeshArrayRecovery
    {
        // 手順1：非アクティブで有効なメッシュを持つオブジェクトを選択
        //        （CreateNew 変換で無効化された元オブジェクトの候補。無傷でここに残っている）
        [MenuItem("you5248/Recovery/1. 無効化された元メッシュを選択")]
        static void SelectInactiveOriginals()
        {
            var found = Object.FindObjectsOfType<MeshFilter>(true)
                .Where(mf => !mf.gameObject.activeInHierarchy && mf.sharedMesh != null)
                .Select(mf => mf.gameObject)
                .Distinct().ToArray();
            Selection.objects = found;
            Debug.Log($"[Recovery] 無効＋有効メッシュ持ち {found.Length} 個を選択。これが無効化された元オブジェクトの候補です。" +
                      "本当に戻したいものか確認し（誤って常時無効のものを含むかも）、メニュー『2. 選択を再有効化』で戻せます。");
        }

        // 手順2：選択を再有効化（Undo 可）
        [MenuItem("you5248/Recovery/2. 選択を再有効化")]
        static void ReenableSelected()
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
            {
                // Prefabアセット本体/Prefab Mode編集中の内容は書き換えない（Combiner側と同じガード）
                if (!MeshArrayCombiner.IsMutableSceneObject(go)) continue;
                Undo.RecordObject(go, "Recovery Re-enable");
                go.SetActive(true);
                if (PrefabUtility.IsPartOfPrefabInstance(go))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(go);
                n++;
            }
            Debug.Log($"[Recovery] {n} 個を再有効化しました。");
        }

        // 手順3：メッシュ or マテリアルが Missing(null) のオブジェクトを選択
        //        （壊れた結合結果の候補。CreateNew ならこれらは削除してよい）
        [MenuItem("you5248/Recovery/3. Missing を含むオブジェクトを選択")]
        static void SelectMissing()
        {
            var found = Object.FindObjectsOfType<MeshRenderer>(true)
                .Where(mr =>
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    bool missingMesh = mf != null && mf.sharedMesh == null;
                    bool missingMat  = mr.sharedMaterials.Length == 0 || mr.sharedMaterials.Any(m => m == null);
                    return missingMesh || missingMat;
                })
                .Select(mr => mr.gameObject)
                .Distinct().ToArray();
            Selection.objects = found;
            Debug.Log($"[Recovery] Missing を含む {found.Length} 個を選択。CreateNew の壊れた結合結果ならこれらを削除し、" +
                      "手順1〜2 で元（無効化されていたもの）を再有効化してください。");
        }
    }
}
