// SceneToPrefabBatcherSettings.cs
// Unity 2022.3.22f1 compatible
// Persistent settings for SceneToPrefabBatcher.
// Stored as a ScriptableObject so that category keyword rules travel with the
// project when the user duplicates the project for a new VRChat world.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace you5248.SceneToPrefabBatcher
{
    public class SceneToPrefabBatcherSettings : ScriptableObject
    {
        public const string DefaultAssetPath =
            "Assets/you5248/SceneToPrefabBatcher/CategoryRules.asset";

        [Serializable]
        public class KeywordRule
        {
            public string keyword = "";
            public string category = "";
        }

        [Tooltip("Keyword -> Category mapping. Matched against GameObject name and mesh asset path (case-insensitive, substring).")]
        public List<KeywordRule> rules = new List<KeywordRule>();

        public string fallbackCategoryWhenUnknown = "Unknown";

        public static SceneToPrefabBatcherSettings LoadOrCreate()
        {
            var s = AssetDatabase.LoadAssetAtPath<SceneToPrefabBatcherSettings>(DefaultAssetPath);
            if (s != null) return s;

            var dir = System.IO.Path.GetDirectoryName(DefaultAssetPath).Replace("\\",  "/");
            if (!AssetDatabase.IsValidFolder(dir))
            {
                var parent = System.IO.Path.GetDirectoryName(dir).Replace("\\",  "/");
                var leaf = System.IO.Path.GetFileName(dir);
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    // パッケージ配布後は Assets/you5248 が存在しない前提で親から掘る
                    if (!AssetDatabase.IsValidFolder("Assets/you5248"))
                        AssetDatabase.CreateFolder("Assets", "you5248");
                    AssetDatabase.CreateFolder("Assets/you5248", "SceneToPrefabBatcher");
                }
                else
                {
                    AssetDatabase.CreateFolder(parent, leaf);
                }
            }

            s = ScriptableObject.CreateInstance<SceneToPrefabBatcherSettings>();
            s.rules.Add(new KeywordRule { keyword = "chair", category = "Furniture" });
            s.rules.Add(new KeywordRule { keyword = "table", category = "Furniture" });
            s.rules.Add(new KeywordRule { keyword = "sofa",  category = "Furniture" });
            s.rules.Add(new KeywordRule { keyword = "bottle", category = "Bar" });
            s.rules.Add(new KeywordRule { keyword = "glass",  category = "Bar" });
            s.rules.Add(new KeywordRule { keyword = "bar",    category = "Bar" });
            s.rules.Add(new KeywordRule { keyword = "lamp",   category = "Lighting" });
            s.rules.Add(new KeywordRule { keyword = "light",  category = "Lighting" });
            s.rules.Add(new KeywordRule { keyword = "plant",  category = "Nature" });
            s.rules.Add(new KeywordRule { keyword = "tree",   category = "Nature" });
            AssetDatabase.CreateAsset(s, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return s;
        }

        public string ResolveCategory(string gameObjectName, string meshAssetPath)
        {
            if (rules != null)
            {
                foreach (var r in rules)
                {
                    if (string.IsNullOrWhiteSpace(r.keyword) || string.IsNullOrWhiteSpace(r.category)) continue;
                    var kw = r.keyword.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(gameObjectName) &&
                        gameObjectName.ToLowerInvariant().Contains(kw)) return r.category;
                    if (!string.IsNullOrEmpty(meshAssetPath) &&
                        meshAssetPath.ToLowerInvariant().Contains(kw)) return r.category;
                }
            }

            if (!string.IsNullOrEmpty(meshAssetPath) && meshAssetPath.StartsWith("Assets/"))
            {
                var parts = meshAssetPath.Split('/');
                if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1])) return parts[1];
            }

            return string.IsNullOrEmpty(fallbackCategoryWhenUnknown) ? "Unknown" : fallbackCategoryWhenUnknown;
        }
    }
}

