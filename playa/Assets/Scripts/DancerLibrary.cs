using System;
using UnityEngine;

namespace Playa
{
    // Drop Mixamo characters + dance clips into these arrays and DanceFloor
    // spawns rigged crowd members instead of capsules. If both arrays are
    // empty the crowd falls back to primitives so the demo boots regardless.
    [CreateAssetMenu(menuName = "Playa/Dancer Library", fileName = "DancerLibrary")]
    public sealed class DancerLibrary : ScriptableObject
    {
        [Serializable]
        public struct DanceClip
        {
            public AnimationClip clip;
            [Tooltip("How many musical beats one full loop of the clip covers. Most Mixamo dance loops = 4 or 8. Wrong value → clip plays at half/double speed.")]
            [Min(1)] public int beatsPerLoop;
        }

        [Serializable]
        public struct OutfitPalette
        {
            [Tooltip("Multiplied onto the character's SkinnedMeshRenderer _BaseColor. White = leave textures alone.")]
            public Color linen;
        }

        [Header("Character prefabs — drag Mixamo T-pose FBXs here (no animation, with skin)")]
        public GameObject[] characterPrefabs;

        [Header("Dance clips — Mixamo 'in place' animation FBXs (drag the AnimationClip sub-asset)")]
        public DanceClip[] danceClips;

        [Header("Outfit palettes — white-linen family, one picked per dancer")]
        public OutfitPalette[] palettes = new OutfitPalette[]
        {
            new OutfitPalette { linen = new Color(0.96f, 0.94f, 0.88f) }, // ivory
            new OutfitPalette { linen = new Color(0.92f, 0.88f, 0.78f) }, // oat
            new OutfitPalette { linen = new Color(0.98f, 0.96f, 0.92f) }, // bone
            new OutfitPalette { linen = new Color(0.88f, 0.82f, 0.70f) }, // sand
        };

        [Header("Height variation")]
        [Range(0.7f, 1.3f)] public float minScale = 0.92f;
        [Range(0.7f, 1.3f)] public float maxScale = 1.08f;

        [Header("Procedural linen skirt")]
        public bool spawnSkirt = true;
        [Tooltip("Bone name to parent the skirt to. Mixamo default is 'mixamorig:Hips'.")]
        public string skirtParentBone = "mixamorig:Hips";
        [Range(0.2f, 1.5f)] public float skirtLength = 0.75f;
        [Range(0.1f, 0.8f)] public float skirtTopRadius = 0.20f;
        [Range(0.2f, 1.2f)] public float skirtBottomRadius = 0.55f;
        [Range(6, 32)] public int skirtSegments = 18;
        [Range(2, 6)] public int skirtRings = 4;
        [Tooltip("Vertical offset of the skirt top from the hip bone origin.")]
        public float skirtHipOffset = -0.05f;

        public bool IsUsable => characterPrefabs != null && characterPrefabs.Length > 0
                                && danceClips != null && danceClips.Length > 0;
    }
}
