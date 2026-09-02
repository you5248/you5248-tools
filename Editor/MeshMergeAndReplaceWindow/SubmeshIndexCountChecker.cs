using UnityEditor;
using UnityEngine;

namespace you5248.MeshMergeAndReplace
{

    public static class SubmeshIndexCountChecker
    {
        [MenuItem("you5248/Submesh IndexCount (Selected MeshFilter)", priority = 2000)]
        private static void Check()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.LogWarning("Select a GameObject with MeshFilter.");
                return;
            }

            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                Debug.LogWarning("Selected object has no MeshFilter or Mesh.");
                return;
            }

            var m = mf.sharedMesh;
            Debug.Log($"[SubmeshCheck] '{go.name}' mesh='{m.name}' subMeshCount={m.subMeshCount}");

            for (int i = 0; i < m.subMeshCount; i++)
            {
                var idx = m.GetIndexCount(i);
                Debug.Log($"  submesh[{i}] indexCount={idx}");
            }
        }
    }

}
