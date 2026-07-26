using System.Collections.Generic;
using UnityEngine;

namespace Playa.Urdf
{
    // Maps a scalar phase θ (radians, from RobotDancer) into per-joint target
    // values so the robot dances on the beat. Everything is deterministic —
    // same θ → same pose every frame — because the visual is kinematic.
    //
    //   q_i(θ) = bias_i · range_i + amp_i · range_i · sin(f_i · θ + φ_i + sideOffset_i)
    //
    // where range_i is derived from the URDF <limit> for the joint (or a
    // per-category default if the joint is continuous / unlimited).
    // f_i, amp_i, φ_i, bias_i come from a per-category CategoryProfile,
    // and sideOffset_i introduces limb-pair antiphase for humanoids and the
    // diagonal-pair trot gait for quadrupeds.
    [DefaultExecutionOrder(-30)]
    public sealed class RobotChoreographer : MonoBehaviour
    {
        [Header("Runtime bindings (set by loader)")]
        [System.NonSerialized] public UrdfRobotInstance robot;
        public RobotMorphology morphology = RobotMorphology.Unknown;

        [Header("Global scaling")]
        [Range(0f, 1.5f)] public float overallAmplitude = 1.0f;
        [Tooltip("Multiplier: robot phase θ is passed through as-is when 1. Higher = faster motion at the joint level (careful, can violate velocity limits).")]
        [Range(0.25f, 4f)] public float overallFrequency = 1.0f;

        // Set by RobotDancer each frame.
        public float PhaseRadians { get; set; }
        public bool Enabled { get; set; } = true;

        readonly List<PerJointState> jointStates = new List<PerJointState>();
        IJointCommand driver;

        public IJointCommand Driver
        {
            get => driver ??= new KinematicJointDriver();
            set => driver = value;
        }

        public void Bind(UrdfRobotInstance instance, RobotMorphology detected)
        {
            robot = instance;
            morphology = detected;
            jointStates.Clear();
            var profiles = DanceProfiles.ForMorphology(morphology);
            foreach (var j in robot.ActuatedJoints)
            {
                var cls = JointClassifier.Classify(j.Name);
                var prof = profiles.Get(cls.Category);
                float range = ComputeRange(j, prof);
                float sidePhase = SidePhaseOffset(cls.Side, morphology);
                jointStates.Add(new PerJointState
                {
                    Joint = j,
                    Classification = cls,
                    Profile = prof,
                    RangeRadians = range,
                    SidePhaseOffsetRadians = sidePhase,
                });
            }
        }

        void Update()
        {
            if (!Enabled || robot == null || jointStates.Count == 0) return;
            float now = Time.time;
            float theta = PhaseRadians;
            var d = Driver;

            for (int i = 0; i < jointStates.Count; i++)
            {
                var s = jointStates[i];
                float phase = s.Profile.Frequency * theta * overallFrequency
                              + s.Profile.PhaseOffsetRadians
                              + s.SidePhaseOffsetRadians;
                float target = s.RangeRadians * s.Profile.BiasFraction
                             + s.RangeRadians * s.Profile.AmplitudeFraction * overallAmplitude
                               * Mathf.Sin(phase);

                // Clamp to URDF limits if present — never drive out of spec on purpose.
                if (s.Joint.Limit != null && s.Joint.Limit.HasPositionLimits)
                    target = Mathf.Clamp(target, s.Joint.Limit.Lower, s.Joint.Limit.Upper);

                d.Apply(s.Joint, target, now);
            }
        }

        // ---- helpers --------------------------------------------------------

        // Range: prefer URDF-declared limits; fall back to profile default for
        // continuous / unlimited joints. Prismatic joints use the same numerics
        // (meters instead of radians) — profiles are intentionally small for them.
        float ComputeRange(UrdfJoint j, CategoryProfile prof)
        {
            if (j.Limit != null && j.Limit.HasPositionLimits)
                return 0.5f * (j.Limit.Upper - j.Limit.Lower);
            return prof.DefaultRangeRadians;
        }

        static float SidePhaseOffset(JointSide side, RobotMorphology m)
        {
            // Humanoids: left/right limbs swing in antiphase → π offset.
            if (m == RobotMorphology.Humanoid)
                return side == JointSide.Right ? Mathf.PI : 0f;
            // Quadruped trot: diagonal pairs (FL+RR) vs (FR+RL) in antiphase.
            if (m == RobotMorphology.Quadruped)
            {
                switch (side)
                {
                    case JointSide.FrontLeft:  return 0f;
                    case JointSide.RearRight:  return 0f;
                    case JointSide.FrontRight: return Mathf.PI;
                    case JointSide.RearLeft:   return Mathf.PI;
                }
            }
            return 0f;
        }

        struct PerJointState
        {
            public UrdfJoint Joint;
            public JointClassification Classification;
            public CategoryProfile Profile;
            public float RangeRadians;
            public float SidePhaseOffsetRadians;
        }
    }

    // -------------------------------------------------------------------------
    // Per-category oscillation profiles. Values are tuned to look expressive
    // but stay near the middle of typical joint ranges, so URDF limits rarely
    // clamp. Amplitudes are as a fraction of the joint's range (or default).
    // -------------------------------------------------------------------------

    public struct CategoryProfile
    {
        public float AmplitudeFraction;         // 0..1
        public float BiasFraction;              // -1..1 (offset in units of range)
        public float Frequency;                 // multiplier on θ
        public float PhaseOffsetRadians;
        public float DefaultRangeRadians;       // used when URDF omits <limit>
    }

    public sealed class ProfileTable
    {
        readonly Dictionary<JointCategory, CategoryProfile> table =
            new Dictionary<JointCategory, CategoryProfile>();
        public CategoryProfile Default;

        public void Set(JointCategory c, CategoryProfile p) => table[c] = p;
        public CategoryProfile Get(JointCategory c) =>
            table.TryGetValue(c, out var p) ? p : Default;
    }

    public static class DanceProfiles
    {
        public static ProfileTable ForMorphology(RobotMorphology m)
        {
            switch (m)
            {
                case RobotMorphology.Humanoid:  return Humanoid();
                case RobotMorphology.Quadruped: return Quadruped();
                case RobotMorphology.Arm:       return Arm();
                default:                        return Humanoid(); // safe fallback
            }
        }

        static ProfileTable Humanoid()
        {
            var t = new ProfileTable();
            t.Default = new CategoryProfile { AmplitudeFraction = 0.15f, Frequency = 1f, DefaultRangeRadians = 0.5f };
            t.Set(JointCategory.Shoulder, P(amp: 0.55f, freq: 1f,   bias: 0f,     defRange: 1.2f));
            t.Set(JointCategory.Elbow,    P(amp: 0.35f, freq: 1f,   bias: -0.3f, defRange: 1.0f));
            t.Set(JointCategory.Wrist,    P(amp: 0.2f,  freq: 2f,   bias: 0f,    defRange: 0.6f));
            t.Set(JointCategory.Finger,   P(amp: 0.15f, freq: 2f,   bias: 0f,    defRange: 0.4f));
            t.Set(JointCategory.Gripper,  P(amp: 0.15f, freq: 2f,   bias: 0f,    defRange: 0.4f));
            t.Set(JointCategory.Spine,    P(amp: 0.20f, freq: 0.5f, bias: 0f,    defRange: 0.3f));
            t.Set(JointCategory.Waist,    P(amp: 0.20f, freq: 0.5f, bias: 0f,    defRange: 0.3f));
            t.Set(JointCategory.Neck,     P(amp: 0.15f, freq: 0.5f, bias: 0f,    defRange: 0.4f));
            t.Set(JointCategory.Head,     P(amp: 0.15f, freq: 0.5f, bias: 0f,    defRange: 0.4f));
            t.Set(JointCategory.Hip,      P(amp: 0.20f, freq: 1f,   bias: 0f,    defRange: 0.6f));
            t.Set(JointCategory.Knee,     P(amp: 0.35f, freq: 1f,   bias: 0.3f,  defRange: 0.8f));
            t.Set(JointCategory.Ankle,    P(amp: 0.20f, freq: 1f,   bias: -0.2f, defRange: 0.4f));
            t.Set(JointCategory.Toe,      P(amp: 0.10f, freq: 1f,   bias: 0f,    defRange: 0.3f));
            return t;
        }

        static ProfileTable Quadruped()
        {
            var t = new ProfileTable();
            t.Default = new CategoryProfile { AmplitudeFraction = 0.15f, Frequency = 1f, DefaultRangeRadians = 0.5f };
            // Quadruped: gentle bounce/trot rather than expressive limb waving.
            // Hip pitch: main driver of the bob; hip roll/yaw kept small.
            t.Set(JointCategory.Hip,   P(amp: 0.35f, freq: 1f,   bias: 0f,   defRange: 0.8f));
            t.Set(JointCategory.Knee,  P(amp: 0.30f, freq: 1f,   bias: 0.3f, defRange: 1.2f));
            t.Set(JointCategory.Ankle, P(amp: 0.15f, freq: 1f,   bias: 0f,   defRange: 0.4f));
            t.Set(JointCategory.Head,  P(amp: 0.20f, freq: 0.5f, bias: 0f,   defRange: 0.5f));
            t.Set(JointCategory.Spine, P(amp: 0.10f, freq: 0.5f, bias: 0f,   defRange: 0.3f));
            // Arms (e.g. Spot has an optional 7-DoF arm attached):
            t.Set(JointCategory.Shoulder, P(amp: 0.35f, freq: 1f, bias: 0f,   defRange: 1.0f));
            t.Set(JointCategory.Elbow,    P(amp: 0.25f, freq: 1f, bias: -0.3f, defRange: 0.8f));
            t.Set(JointCategory.Wrist,    P(amp: 0.15f, freq: 2f, bias: 0f,   defRange: 0.5f));
            return t;
        }

        static ProfileTable Arm()
        {
            var t = new ProfileTable();
            t.Default = new CategoryProfile { AmplitudeFraction = 0.30f, Frequency = 1f, DefaultRangeRadians = 1.0f };
            t.Set(JointCategory.Shoulder, P(amp: 0.55f, freq: 1f, bias: 0f,   defRange: 1.5f));
            t.Set(JointCategory.Elbow,    P(amp: 0.40f, freq: 1f, bias: 0.2f, defRange: 1.2f));
            t.Set(JointCategory.Wrist,    P(amp: 0.30f, freq: 2f, bias: 0f,   defRange: 0.8f));
            t.Set(JointCategory.Gripper,  P(amp: 0.30f, freq: 2f, bias: 0f,   defRange: 0.5f));
            return t;
        }

        static CategoryProfile P(float amp, float freq, float bias, float defRange)
        {
            return new CategoryProfile
            {
                AmplitudeFraction = amp,
                Frequency = freq,
                BiasFraction = bias,
                DefaultRangeRadians = defRange,
            };
        }
    }
}
