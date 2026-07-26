using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Playa.Editor
{
    // One-click regenerator for the DancerLibrary asset. Scans
    // Assets/Characters/Bodies/ for character FBXs, and Assets/Characters/
    // Animations/ for animation FBXs (extracting the AnimationClip
    // sub-asset from each). Creates the asset at
    // Assets/Characters/DancerLibrary.asset if missing.
    public static class DancerLibraryBuilder
    {
        const string BodiesFolder = "Assets/Characters/Bodies";
        const string AnimationsFolder = "Assets/Characters/Animations";
        const string AssetPath = "Assets/Characters/DancerLibrary.asset";

        [MenuItem("Playa/Regenerate Dancer Library")]
        public static void Regenerate()
        {
            var lib = AssetDatabase.LoadAssetAtPath<DancerLibrary>(AssetPath);
            bool created = false;
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<DancerLibrary>();
                AssetDatabase.CreateAsset(lib, AssetPath);
                created = true;
            }

            lib.characterPrefabs = ScanBodies();
            lib.danceClips = ScanClips();

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = lib;
            EditorGUIUtility.PingObject(lib);
            Debug.Log(
                $"{(created ? "Created" : "Updated")} DancerLibrary — " +
                $"{lib.characterPrefabs.Length} characters, {lib.danceClips.Length} clips");
        }

        static GameObject[] ScanBodies()
        {
            var found = new List<GameObject>();
            if (!Directory.Exists(BodiesFolder)) return found.ToArray();
            foreach (var path in Directory.GetFiles(BodiesFolder, "*.fbx"))
            {
                var normalised = path.Replace('\\', '/');
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(normalised);
                if (go != null) found.Add(go);
            }
            return found.ToArray();
        }

        static DancerLibrary.DanceClip[] ScanClips()
        {
            var found = new List<DancerLibrary.DanceClip>();
            if (!Directory.Exists(AnimationsFolder)) return found.ToArray();
            foreach (var path in Directory.GetFiles(AnimationsFolder, "*.fbx"))
            {
                var normalised = path.Replace('\\', '/');
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(normalised))
                {
                    // Skip the __preview__ clip Mixamo bundles alongside the real one.
                    if (sub is AnimationClip clip && !clip.name.StartsWith("__"))
                    {
                        found.Add(new DancerLibrary.DanceClip
                        {
                            clip = clip,
                            beatsPerLoop = 8,   // most Mixamo dance loops span 8 beats
                        });
                    }
                }
            }
            return found.ToArray();
        }
    }
}
