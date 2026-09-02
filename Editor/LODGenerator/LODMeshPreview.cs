using UnityEditor;
using UnityEngine;

namespace you5248.LODGenerator
{
    /// <summary>
    /// Renders a mesh into an interactive (drag-to-rotate) preview viewport.
    /// Wraps PreviewRenderUtility. The owner window MUST call Cleanup() from OnDisable
    /// to release the render texture / camera resources.
    /// </summary>
    public class LODMeshPreview
    {
        private PreviewRenderUtility _pru;
        private Material _fallbackMat;

        // 共有回転 (x=yaw, y=pitch)。両ビューポートで同じ向きにそろえる。
        public Vector2 drag = new Vector2(120f, -15f);

        private void EnsureInit()
        {
            if (_pru == null)
            {
                _pru = new PreviewRenderUtility();
                _pru.camera.fieldOfView = 30f;
                _pru.camera.nearClipPlane = 0.01f;
                _pru.camera.farClipPlane = 1000f;
                _pru.camera.clearFlags = CameraClearFlags.SolidColor;
                _pru.camera.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            }
            if (_fallbackMat == null)
            {
                var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
                _fallbackMat = new Material(shader);
            }
        }

        /// <summary>Update rotation from a mouse drag inside the given area. Returns true if changed.</summary>
        public bool HandleDrag(Rect area)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDrag && area.Contains(e.mousePosition))
            {
                drag.x += e.delta.x;
                drag.y += e.delta.y;
                e.Use();
                return true;
            }
            return false;
        }

        /// <summary>Render a mesh into the given rect (only acts on the Repaint event).</summary>
        public void DrawViewport(Rect r, Mesh mesh, Material[] mats)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (mesh == null) return;

            EnsureInit();
            _pru.BeginPreview(r, GUIStyle.none);

            var bounds = mesh.bounds;
            float size = bounds.size.magnitude;
            if (size < 1e-4f) size = 1f;
            float dist = size * 2.2f;

            _pru.camera.transform.position = new Vector3(0f, 0f, -dist);
            _pru.camera.transform.rotation = Quaternion.identity;

            _pru.lights[0].intensity = 1.1f;
            _pru.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            _pru.lights[1].intensity = 0.55f;
            _pru.lights[1].transform.rotation = Quaternion.Euler(-20f, -120f, 0f);
            _pru.ambientColor = new Color(0.32f, 0.32f, 0.32f, 1f);

            Quaternion rot = Quaternion.Euler(drag.y, drag.x, 0f);
            // メッシュ中心まわりに回転させる: point' = rot * (point - center)
            Matrix4x4 m = Matrix4x4.TRS(rot * (-bounds.center), rot, Vector3.one);

            int subCount = Mathf.Max(1, mesh.subMeshCount);
            for (int s = 0; s < subCount; s++)
            {
                Material mat = (mats != null && s < mats.Length && mats[s] != null)
                    ? mats[s] : _fallbackMat;
                _pru.DrawMesh(mesh, m, mat, s);
            }

            _pru.camera.Render();
            var tex = _pru.EndPreview();
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
        }

        public void Cleanup()
        {
            if (_pru != null)
            {
                _pru.Cleanup();
                _pru = null;
            }
            if (_fallbackMat != null)
            {
                Object.DestroyImmediate(_fallbackMat);
                _fallbackMat = null;
            }
        }
    }
}
