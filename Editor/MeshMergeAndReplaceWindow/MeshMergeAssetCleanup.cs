using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace you5248.MeshMergeAndReplace
{

    /// <summary>
    /// Deletes a merged-mesh .asset when its merge is undone.
    /// The merged MeshRenderer is the sentinel: once Undo destroys it the tracked
    /// asset is orphaned and removed (within a freshness window, mirroring the LOD
    /// Generator's behaviour). Saving the scene is treated as "kept" implicitly —
    /// stale entries simply fall outside the freshness window.
    /// </summary>
    [InitializeOnLoad]
    static class MeshMergeAssetCleanup
    {
        class Entry
        {
            public Component sentinel;   // null (Unity-destroyed) once the merge is undone
            public string assetPath;
            public System.DateTime createdAt;
        }

        static readonly List<Entry> _entries = new List<Entry>();
        const double FreshnessSeconds = 120.0;

        static MeshMergeAssetCleanup()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public static void Track(Component sentinel, string assetPath)
        {
            if (sentinel == null || string.IsNullOrEmpty(assetPath)) return;
            _entries.Add(new Entry
            {
                sentinel = sentinel,
                assetPath = assetPath,
                createdAt = System.DateTime.UtcNow
            });
        }

        static void OnUndoRedo()
        {
            bool changed = false;
            var now = System.DateTime.UtcNow;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.sentinel != null) continue; // still in scene -> not undone

                double age = (now - e.createdAt).TotalSeconds;
                if (age <= FreshnessSeconds &&
                    !string.IsNullOrEmpty(e.assetPath) &&
                    AssetDatabase.LoadAssetAtPath<Object>(e.assetPath) != null)
                {
                    if (AssetDatabase.DeleteAsset(e.assetPath))
                    {
                        changed = true;
                        Debug.Log($"[MeshMerge] Undo cleanup: removed {e.assetPath}");
                    }
                }
                _entries.RemoveAt(i);
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
    }
}
