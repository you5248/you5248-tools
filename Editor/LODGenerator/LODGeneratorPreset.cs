using System;
using UnityEngine;

namespace you5248.LODGenerator
{
    /// <summary>
    /// Per-LOD-level settings.
    /// </summary>
    [Serializable]
    public class LODLevelSetting
    {
        [Tooltip("How this LOD level is built: Simplify the mesh, replace it with a flat billboard quad, or replace it with a bounds-fitted cube.")]
        public LODLevelMode mode = LODLevelMode.Simplify;

        [Tooltip("Mesh reduction ratio (0.0 - 1.0). 0.5 = 50% triangles. (Simplify mode only)")]
        [Range(0.01f, 1f)]
        public float quality = 0.5f;

        [Tooltip("Screen relative height threshold (0.0 - 1.0).")]
        [Range(0f, 1f)]
        public float screenPercent = 0.25f;
    }

    /// <summary>How a generated LOD level is produced.</summary>
    public enum LODLevelMode
    {
        /// <summary>Decimate the source mesh (default).</summary>
        Simplify = 0,
        /// <summary>Replace with a flat billboard quad + captured cutout texture (for thin meshes).</summary>
        Billboard = 1,
        /// <summary>Replace with a bounds-fitted cube reusing the first source material (for box-like meshes: crates, cardboard boxes).</summary>
        Cube = 2,
    }

    /// <summary>
    /// Stores all LOD Generator settings as a reusable preset (ScriptableObject).
    /// </summary>
    [CreateAssetMenu(fileName = "LODPreset", menuName = "you5248/LOD Generator Preset")]
    public class LODGeneratorPreset : ScriptableObject
    {
        [Tooltip("Number of LOD levels to generate (1-4, excluding LOD0 original).")]
        [Range(1, 4)]
        public int lodCount = 3;

        [Tooltip("Screen percent threshold for LOD0 (original mesh).")]
        [Range(0f, 1f)]
        public float lod0ScreenPercent = 0.5f;

        [Tooltip("Settings for each generated LOD level.")]
        public LODLevelSetting[] levels = new LODLevelSetting[]
        {
            new LODLevelSetting { quality = 0.50f, screenPercent = 0.25f },
            new LODLevelSetting { quality = 0.25f, screenPercent = 0.10f },
            new LODLevelSetting { quality = 0.10f, screenPercent = 0.05f },
        };

        [Tooltip("Add a culled (fully hidden) level at the end.")]
        public bool enableCulled = true;

        [Tooltip("Skip a LOD level when its triangle count doesn't drop below the previous level (no further reduction). The mesh, child object and LODGroup entry are all omitted.")]
        public bool skipDuplicateLODs = true;

        [Tooltip("Enable cross-fade (dithering) between LOD levels.")]
        public bool enableCrossFade = false;

        [Tooltip("Cross-fade transition width (0.0 - 1.0).")]
        [Range(0f, 1f)]
        public float crossFadeWidth = 0.1f;

        [Tooltip("Use AnimateCrossFading mode (true) or SpeedTree mode (false).")]
        public bool animateCrossFading = true;

        [Tooltip("Preserve open-mesh border edges (recommended ON). Prevents holes on planes, curtains, leaves, etc.")]
        public bool preserveBorderEdges = true;

        [Tooltip("Preserve UV seam edges (recommended ON). Prevents texture stretching across UV islands.")]
        public bool preserveUVSeamEdges = true;

        [Tooltip("Preserve UV foldover edges. Helps texture-mirrored meshes.")]
        public bool preserveUVFoldoverEdges = false;

        [Tooltip("Preserve surface curvature. Better silhouette but lower reduction.")]
        public bool preserveSurfaceCurvature = false;

        [Tooltip("Save generated meshes in the same folder as the source.")]
        public bool saveSameFolder = true;

        [Tooltip("Custom folder path (used when saveSameFolder is false).")]
        public string customSavePath = "Assets/GeneratedLODs";

        // ── Billboard (板ポリ) settings — used by levels whose mode == Billboard ──

        [Tooltip("Captured billboard texture resolution (long edge, px).")]
        public int billboardResolution = 1024;

        [Tooltip("Alpha cutout threshold for the billboard material (_Cutoff).")]
        [Range(0f, 1f)]
        public float billboardCutoff = 0.5f;

        [Tooltip("Extra padding around the captured silhouette (0.0 - 0.5).")]
        [Range(0f, 0.5f)]
        public float billboardPadding = 0.05f;

        [Tooltip("Generate a two-sided quad so the billboard is visible from behind too.")]
        public bool billboardTwoSided = true;

        [Tooltip("Auto-suggest billboard when min/max bounds extent ratio is at or below this (lower = thinner).")]
        [Range(0.01f, 1f)]
        public float billboardThinRatio = 0.15f;

        // ── Structural simplify (構造簡略化 / Limited Dissolve) — Replace mode ──

        [Tooltip("Replace mode: swap the mesh with a bounds-fitted 12-tri cube (first material only). Quality / structural settings are ignored.")]
        public bool replaceWithCube = false;

        [Tooltip("Replace mode: run a structural simplify pass (merge coplanar faces, collapse bevels) before reduction. Keeps original UVs / normal maps.")]
        public bool replaceStructuralSimplify = false;

        [Tooltip("Adjacent faces within this angle (deg) are merged into one planar region.")]
        [Range(0f, 30f)]
        public float structuralCoplanarAngle = 3f;

        [Tooltip("Edges shorter than this (world units) that sit on a crease are welded (bevel removal). 0 = off.")]
        public float structuralBevelLength = 0f;

        [Tooltip("Assign flat per-region normals (keeps box corners sharp). Off = keep original normals.")]
        public bool structuralFlatShade = true;

        /// <summary>Ensure the levels array matches lodCount.</summary>
        public void SyncLevels()
        {
            if (levels == null || levels.Length != lodCount)
            {
                var old = levels;
                levels = new LODLevelSetting[lodCount];
                for (int i = 0; i < lodCount; i++)
                {
                    if (old != null && i < old.Length)
                        levels[i] = old[i];
                    else
                        levels[i] = GetDefaultLevel(i, lodCount);
                }
            }
        }

        /// <summary>Returns sensible defaults for a given level index.</summary>
        public static LODLevelSetting GetDefaultLevel(int index, int total)
        {
            float t = (index + 1f) / (total + 1f);
            return new LODLevelSetting
            {
                quality = Mathf.Clamp01(1f - t * 0.8f),
                screenPercent = Mathf.Clamp01(Mathf.Lerp(0.3f, 0.02f, t))
            };
        }

        /// <summary>Create runtime defaults (not saved as asset).</summary>
        public static LODGeneratorPreset CreateDefault()
        {
            var preset = CreateInstance<LODGeneratorPreset>();
            preset.name = LODGeneratorLocalization.Get("PresetDefault");
            preset.lodCount = 3;
            preset.lod0ScreenPercent = 0.5f;
            preset.levels = new LODLevelSetting[]
            {
                new LODLevelSetting { quality = 0.50f, screenPercent = 0.25f },
                new LODLevelSetting { quality = 0.25f, screenPercent = 0.10f },
                new LODLevelSetting { quality = 0.10f, screenPercent = 0.05f },
            };
            preset.enableCulled = true;
            preset.skipDuplicateLODs = true;
            preset.enableCrossFade = false;
            preset.crossFadeWidth = 0.1f;
            preset.animateCrossFading = true;
            preset.saveSameFolder = true;
            preset.customSavePath = "Assets/GeneratedLODs";
            return preset;
        }

        /// <summary>Copy settings from another preset.</summary>
        public void CopyFrom(LODGeneratorPreset other)
        {
            lodCount = other.lodCount;
            lod0ScreenPercent = other.lod0ScreenPercent;
            enableCulled = other.enableCulled;
            skipDuplicateLODs = other.skipDuplicateLODs;
            enableCrossFade = other.enableCrossFade;
            crossFadeWidth = other.crossFadeWidth;
            animateCrossFading = other.animateCrossFading;
            saveSameFolder = other.saveSameFolder;
            customSavePath = other.customSavePath;

            billboardResolution = other.billboardResolution;
            billboardCutoff = other.billboardCutoff;
            billboardPadding = other.billboardPadding;
            billboardTwoSided = other.billboardTwoSided;
            billboardThinRatio = other.billboardThinRatio;

            replaceWithCube = other.replaceWithCube;
            replaceStructuralSimplify = other.replaceStructuralSimplify;
            structuralCoplanarAngle = other.structuralCoplanarAngle;
            structuralBevelLength = other.structuralBevelLength;
            structuralFlatShade = other.structuralFlatShade;

            levels = new LODLevelSetting[other.levels.Length];
            for (int i = 0; i < other.levels.Length; i++)
            {
                levels[i] = new LODLevelSetting
                {
                    mode = other.levels[i].mode,
                    quality = other.levels[i].quality,
                    screenPercent = other.levels[i].screenPercent
                };
            }
        }
    }
}

