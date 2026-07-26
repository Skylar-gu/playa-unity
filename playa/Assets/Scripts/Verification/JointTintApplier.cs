using System.Collections.Generic;
using UnityEngine;

namespace Playa.Verification
{
    // Applies a per-joint color tint to the child link's visual meshes based
    // on each joint's Worst feasibility status. Uses MaterialPropertyBlocks so
    // we don't leak per-joint material instances.
    //
    // Runs after FeasibilityAuditor. Toggle on/off from a HUD hotkey; when
    // disabled, all tints are reset to a neutral base.
    [DefaultExecutionOrder(-15)]
    public sealed class JointTintApplier : MonoBehaviour
    {
        public FeasibilityAuditor auditor;
        public bool enableTint = true;
        public Color okTint = new Color(0.85f, 0.9f, 0.9f);
        public Color warnTint = new Color(1.0f, 0.85f, 0.35f);
        public Color violationTint = new Color(1.0f, 0.35f, 0.30f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock mpb;
        readonly Dictionary<string, Renderer[]> renderersByJoint =
            new Dictionary<string, Renderer[]>();

        void Awake() { mpb = new MaterialPropertyBlock(); }

        public void RebindFrom(FeasibilityAuditor a)
        {
            auditor = a;
            renderersByJoint.Clear();
            if (a == null || !a.IsBound) return;
            foreach (var jc in a.Report.Joints)
            {
                var t = jc.Joint.JointTransform;
                if (t == null) continue;
                renderersByJoint[jc.Joint.Name] = t.GetComponentsInChildren<Renderer>(true);
            }
        }

        void LateUpdate()
        {
            if (auditor == null || !auditor.IsBound) return;
            if (!enableTint) { ResetAll(); return; }

            var joints = auditor.Report.Joints;
            for (int i = 0; i < joints.Count; i++)
            {
                var jc = joints[i];
                var tint = ColorFor(jc.Worst);
                if (!renderersByJoint.TryGetValue(jc.Joint.Name, out var rs)) continue;
                for (int r = 0; r < rs.Length; r++)
                {
                    var rend = rs[r];
                    if (rend == null) continue;
                    rend.GetPropertyBlock(mpb);
                    if (rend.sharedMaterial != null)
                    {
                        if (rend.sharedMaterial.HasProperty(BaseColorId))
                            mpb.SetColor(BaseColorId, tint);
                        if (rend.sharedMaterial.HasProperty(ColorId))
                            mpb.SetColor(ColorId, tint);
                        if (jc.Worst == FeasibilityStatus.Violation
                            && rend.sharedMaterial.HasProperty(EmissionColorId))
                            mpb.SetColor(EmissionColorId, tint * 2.5f);
                    }
                    rend.SetPropertyBlock(mpb);
                }
            }
        }

        void ResetAll()
        {
            foreach (var pair in renderersByJoint)
            {
                foreach (var rend in pair.Value)
                {
                    if (rend == null) continue;
                    rend.GetPropertyBlock(mpb);
                    if (rend.sharedMaterial != null &&
                        rend.sharedMaterial.HasProperty(BaseColorId))
                        mpb.SetColor(BaseColorId, okTint);
                    rend.SetPropertyBlock(mpb);
                }
            }
        }

        Color ColorFor(FeasibilityStatus s)
        {
            switch (s)
            {
                case FeasibilityStatus.Violation: return violationTint;
                case FeasibilityStatus.Warn: return warnTint;
                default: return okTint;
            }
        }
    }
}
