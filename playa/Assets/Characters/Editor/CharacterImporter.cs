using UnityEditor;
using UnityEngine;

namespace Playa.Editor
{
    // Applies the settings Mixamo assets need to work with PhaseAnimatorSync
    // and DancerLibrary — automatically on import — so the user doesn't have
    // to click through every FBX. Scoped to Assets/Characters/ so nothing
    // else in the project is affected.
    public sealed class CharacterImporter : AssetPostprocessor
    {
        const string ScopePath = "Assets/Characters/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ScopePath)) return;
            var importer = (ModelImporter)assetImporter;

            // Humanoid rig — required for cross-character animation retargeting.
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(ScopePath + "Animations/")) return;
            var importer = (ModelImporter)assetImporter;

            var clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = true;
                clips[i].loopPose = true;
                // "In Place" downloads still have a tiny root drift — kill it
                // by locking root height + XZ position + root rotation.
                clips[i].lockRootHeightY = true;
                clips[i].lockRootPositionXZ = true;
                clips[i].lockRootRotation = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
