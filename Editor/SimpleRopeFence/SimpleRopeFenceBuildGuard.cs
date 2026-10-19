using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace you5248.RopeFence
{
// ビルドの見張り: 柵の設定（SimpleRopeFence）は EditorOnly タグの
// 「設定 (EditorOnly)」に置き、Unity がビルドで外す。EditorOnly の外に残っていたら VRChat のビルド（Build & Test / Upload）を
// 始める前に止め、Unity のビルド中のシーン処理でも止める。杭・ロープ・房は MeshFilter/MeshRenderer だけで、メッシュは asset。
public class SimpleRopeFenceBuildGuard : IVRCSDKBuildRequestedCallback, IProcessSceneWithReport
{
    public int callbackOrder => -99;

    /// EditorOnly の外にある SimpleRopeFence（階層のパス）
    public static List<string> FindLeaks(IEnumerable<GameObject> roots)
    {
        var res = new List<string>();
        foreach (var r in roots)
            foreach (var c in r.GetComponentsInChildren<SimpleRopeFence>(true))
                if (!UnderEditorOnly(c.transform)) res.Add(Path(c.transform));
        return res;
    }

    /// ビルドに残る柵の物で、メッシュが asset でない（シーンの中のメッシュ）もの
    public static List<string> FindSceneMeshes(IEnumerable<GameObject> roots)
    {
        var res = new List<string>();
        foreach (var r in roots)
            foreach (var f in r.GetComponentsInChildren<SimpleRopeFence>(true))
            {
                var root = f.transform.parent; if (root == null) continue;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && !EditorUtility.IsPersistent(mf.sharedMesh) && !UnderEditorOnly(mf.transform)) res.Add(Path(mf.transform));
            }
        return res;
    }

    static bool UnderEditorOnly(Transform t) { for (; t != null; t = t.parent) if (t.CompareTag("EditorOnly")) return true; return false; }
    static string Path(Transform t) { var s = t.name; for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s; return s; }

    static IEnumerable<GameObject> LoadedRoots()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded) foreach (var g in s.GetRootGameObjects()) yield return g; }
    }

    /// メニュー「ビルドに残らないか確かめる」の文
    public static string Describe()
    {
        var roots = LoadedRoots().ToList();
        var fences = roots.SelectMany(g => g.GetComponentsInChildren<SimpleRopeFence>(true)).ToList();
        var leaks = FindLeaks(roots); var sceneMeshes = FindSceneMeshes(roots);
        var other = new List<string>();
        foreach (var f in fences)
        {
            var root = f.transform.parent; if (root == null) continue;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || UnderEditorOnly(c.transform)) continue;
                if (c is Transform || c is MeshFilter || c is MeshRenderer) continue;
                other.Add(Path(c.transform) + " (" + c.GetType().Name + ")");
            }
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("柵 " + fences.Count + " 本。");
        sb.AppendLine(leaks.Count == 0 ? "○ 設定の部品はすべて EditorOnly の下（アップロードに残りません）。" : "× EditorOnly の外に設定の部品（このままだとビルドを止めます）:\n  " + string.Join("\n  ", leaks));
        sb.AppendLine(sceneMeshes.Count == 0 ? "○ 杭・ロープ・房のメッシュはすべて asset。" : "△ シーンの中のメッシュ（「今すぐ作り直す」で asset にしてください）:\n  " + string.Join("\n  ", sceneMeshes));
        sb.AppendLine(other.Count == 0 ? "○ ビルドに残る物は Transform・MeshFilter・MeshRenderer だけ。" : "△ ほかの部品:\n  " + string.Join("\n  ", other));
        return sb.ToString().TrimEnd();
    }

    public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
    {
        if (requestedBuildType == VRCSDKRequestedBuildType.Avatar) return true;
        var leaks = FindLeaks(LoadedRoots());
        if (leaks.Count == 0) return true;
        var msg = "杭とロープの柵の「設定」が EditorOnly の外にあり、アップロードに残せません。「設定 (EditorOnly)」のタグを EditorOnly にしてください:\n" + string.Join("\n", leaks.Take(20));
        Debug.LogError("[SimpleRopeFence] " + msg);
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("杭とロープの柵", msg, "OK");
        return false;
    }

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report == null && !BuildPipeline.isBuildingPlayer) return;   // 再生（Play）のときは何もしない
        var leaks = FindLeaks(scene.GetRootGameObjects());
        if (leaks.Count > 0) throw new BuildFailedException("[SimpleRopeFence] EditorOnly の外に柵の設定があります: " + string.Join(", ", leaks.Take(10)));
    }
}
}
