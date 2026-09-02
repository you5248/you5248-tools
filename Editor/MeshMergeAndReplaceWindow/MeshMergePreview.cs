using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace you5248.MeshMergeAndReplace
{

    /// <summary>
    /// Interactive multi-mesh preview viewport for the Mesh Merge tool.
    /// Shows every selected mesh together at its world transform, i.e. what the
    /// merged result will look like. Right-drag to orbit, scroll wheel to zoom.
    /// Wraps PreviewRenderUtility — the owner window MUST call Cleanup() in OnDisable.
    /// </summary>
    public class MeshMergePreview
    {
        public struct Item
        {
            public Mesh mesh;
            public Matrix4x4 matrix;   // world transform of the source (localToWorldMatrix)
            public Material[] mats;
        }

        private PreviewRenderUtility _pru;
        private Material _fallbackMat;

        private Vector2 _drag = new Vector2(120f, -15f); // x = yaw, y = pitch
        private float _zoom = 1f;                          // camera distance multiplier

        private void EnsureInit()
        {
            if (_pru == null)
            {
                _pru = new PreviewRenderUtility();
                _pru.camera.fieldOfView = 30f;
                _pru.camera.nearClipPlane = 0.01f;
                _pru.camera.farClipPlane = 10000f;
                _pru.camera.clearFlags = CameraClearFlags.SolidColor;
                _pru.camera.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            }
            if (_fallbackMat == null)
            {
                var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
                _fallbackMat = new Material(shader);
            }
        }

        /// <summary>Right-drag = orbit, wheel = zoom (inside the area). Returns true if the view changed.</summary>
        public bool HandleInput(Rect area)
        {
            var e = Event.current;
            if (!area.Contains(e.mousePosition)) return false;

            if (e.type == EventType.MouseDrag && e.button == 1)
            {
                _drag.x += e.delta.x;
                _drag.y += e.delta.y;
                e.Use();
                return true;
            }
            if (e.type == EventType.ScrollWheel)
            {
                // wheel up (delta.y < 0) -> zoom in
                _zoom = Mathf.Clamp(_zoom * (1f + e.delta.y * 0.05f), 0.2f, 5f);
                e.Use();
                return true;
            }
            return false;
        }

        /// <summary>Render all items into the rect (acts only on the Repaint event).</summary>
        public void DrawViewport(Rect r, List<Item> items)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (items == null || items.Count == 0) return;

            EnsureInit();

            // Combined bounds across all items, in world space.
            Bounds bounds = default;
            bool has = false;
            foreach (var it in items)
            {
                if (it.mesh == null) continue;
                Vector3 c = it.mesh.bounds.center;
                Vector3 ext = it.mesh.bounds.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3(
                        (i & 1) == 0 ? -ext.x : ext.x,
                        (i & 2) == 0 ? -ext.y : ext.y,
                        (i & 4) == 0 ? -ext.z : ext.z);
                    Vector3 wp = it.matrix.MultiplyPoint3x4(corner);
                    if (!has) { bounds = new Bounds(wp, Vector3.zero); has = true; }
                    else bounds.Encapsulate(wp);
                }
            }
            if (!has) return;

            float size = bounds.size.magnitude;
            if (size < 1e-4f) size = 1f;
            float dist = size * 2.2f * _zoom;

            _pru.BeginPreview(r, GUIStyle.none);

            _pru.camera.transform.position = new Vector3(0f, 0f, -dist);
            _pru.camera.transform.rotation = Quaternion.identity;
            _pru.camera.nearClipPlane = Mathf.Max(0.01f, dist * 0.01f);
            _pru.camera.farClipPlane = dist * 10f + size * 4f;

            _pru.lights[0].intensity = 1.1f;
            _pru.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            _pru.lights[1].intensity = 0.55f;
            _pru.lights[1].transform.rotation = Quaternion.Euler(-20f, -120f, 0f);
            _pru.ambientColor = new Color(0.32f, 0.32f, 0.32f, 1f);

            // Orbit around the combined center: worldPoint -> rot * (worldPoint - center)
            Quaternion rot = Quaternion.Euler(_drag.y, _drag.x, 0f);
            Matrix4x4 view = Matrix4x4.TRS(rot * (-bounds.center), rot, Vector3.one);

            foreach (var it in items)
            {
                if (it.mesh == null) continue;
                Matrix4x4 m = view * it.matrix;
                int subCount = Mathf.Max(1, it.mesh.subMeshCount);
                for (int s = 0; s < subCount; s++)
                {
                    Material mat = (it.mats != null && s < it.mats.Length && it.mats[s] != null)
                        ? it.mats[s] : _fallbackMat;
                    _pru.DrawMesh(it.mesh, m, mat, s);
                }
            }

            _pru.camera.Render();
            Texture tex = _pru.EndPreview();
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
        }

        public void Cleanup()
        {
            if (_pru != null) { _pru.Cleanup(); _pru = null; }
            if (_fallbackMat != null) { Object.DestroyImmediate(_fallbackMat); _fallbackMat = null; }
        }
    }
}
