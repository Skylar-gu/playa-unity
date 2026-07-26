using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playa
{
    // Plays a single AnimationClip on a rigged character via the Playables
    // API and scrubs its timeline by an externally-supplied Kuramoto phase θ.
    //
    // Setup: one-node graph in Manual update mode. Each LateUpdate sets the
    // clip's time from Phase and calls graph.Evaluate(). The Animator picks
    // up the sampled pose in its own update cycle.
    //
    // Phase convention (matches CrowdSimulator): one musical beat = 2π rad
    // of θ. A clip that spans `beatsPerLoop` beats covers beatsPerLoop*2π
    // per full playback.
    //
    // Lazy-build in LateUpdate: DanceFloor's SpawnRigged assigns `animator`
    // and `clip` AFTER AddComponent<>, so OnEnable fires with them null.
    [DefaultExecutionOrder(-30)]
    public sealed class PhaseAnimatorSync : MonoBehaviour
    {
        public Animator animator;
        public AnimationClip clip;
        [Min(1)] public int beatsPerLoop = 8;

        // Set by DanceFloor each frame.
        public float Phase;

        PlayableGraph graph;
        AnimationClipPlayable clipPlayable;
        bool graphBuilt;

        void OnDisable() { Teardown(); }
        void OnDestroy() { Teardown(); }

        void Build()
        {
            if (graphBuilt) return;
            if (animator == null)
            {
                Debug.LogWarning($"PhaseAnimatorSync on {name}: no Animator assigned.");
                return;
            }
            if (clip == null)
            {
                Debug.LogWarning($"PhaseAnimatorSync on {name}: no AnimationClip assigned.");
                return;
            }
            if (animator.avatar == null)
            {
                Debug.LogWarning(
                    $"PhaseAnimatorSync on {name}: Animator has no Avatar. " +
                    "Set the character FBX Rig → Animation Type = Humanoid.");
                return;
            }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            graph = PlayableGraph.Create($"PhaseSync-{name}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clipPlayable = AnimationClipPlayable.Create(graph, clip);
            clipPlayable.SetApplyFootIK(false);
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            output.SetSourcePlayable(clipPlayable);
            graph.Play();
            graphBuilt = true;
            Debug.Log($"PhaseAnimatorSync on {name}: graph built. clip={clip.name} len={clip.length:F2}s isHuman={clip.isHumanMotion}");
        }

        void Teardown()
        {
            if (!graphBuilt) return;
            if (graph.IsValid()) graph.Destroy();
            graphBuilt = false;
        }

        void LateUpdate()
        {
            if (!graphBuilt) Build();
            if (!graphBuilt || clip == null) return;
            const float twoPi = Mathf.PI * 2f;
            float clipT = (Phase / twoPi) / Mathf.Max(1, beatsPerLoop);
            clipT -= Mathf.Floor(clipT);
            clipPlayable.SetTime(clipT * clip.length);
            graph.Evaluate();
        }
    }
}
