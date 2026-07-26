using System.Text.RegularExpressions;
using UnityEngine;

namespace Playa.Urdf
{
    public enum JointCategory
    {
        Unknown,
        Shoulder, Elbow, Wrist, Finger,
        Spine, Waist, Neck, Head,
        Hip, Knee, Ankle, Toe,
        Base, Gripper,
    }

    public enum JointSide { None, Left, Right, FrontLeft, FrontRight, RearLeft, RearRight }

    public struct JointClassification
    {
        public JointCategory Category;
        public JointSide Side;
        // "Sub-axis" hint — many robots break a shoulder into pitch/roll/yaw joints;
        // dance amplitudes differ. 0 = primary (usually pitch), 1 = secondary (roll), 2 = yaw.
        public int SubAxisHint;
    }

    // Regex-based name classifier. Every hobby URDF names joints differently,
    // so heuristics here are permissive — hit rate on common humanoid + quadruped
    // naming conventions is what matters, not being a formal grammar.
    public static class JointClassifier
    {
        public static JointClassification Classify(string rawName)
        {
            var n = (rawName ?? "").ToLowerInvariant();
            return new JointClassification
            {
                Category = ClassifyCategory(n),
                Side = ClassifySide(n),
                SubAxisHint = ClassifySubAxis(n),
            };
        }

        static JointCategory ClassifyCategory(string n)
        {
            // Fingers first — otherwise "index" or "thumb" get missed.
            if (Match(n, @"(finger|thumb|index|middle|ring|pinky|knuckle|phalanx)"))
                return JointCategory.Finger;
            if (Match(n, @"(gripper|jaw|claw|clamp)"))                 return JointCategory.Gripper;
            if (Match(n, @"(shoulder|clavicle|upper_?arm_?joint)"))    return JointCategory.Shoulder;
            if (Match(n, @"(elbow|forearm_?joint)"))                   return JointCategory.Elbow;
            if (Match(n, @"(wrist)"))                                  return JointCategory.Wrist;
            if (Match(n, @"(head)"))                                   return JointCategory.Head;
            if (Match(n, @"(neck)"))                                   return JointCategory.Neck;
            if (Match(n, @"(waist|torso|chest|spine|back)"))           return JointCategory.Spine;
            if (Match(n, @"(hip|thigh)"))                              return JointCategory.Hip;
            if (Match(n, @"(knee|calf|shin)"))                         return JointCategory.Knee;
            if (Match(n, @"(ankle|foot_?joint)"))                      return JointCategory.Ankle;
            if (Match(n, @"(toe)"))                                    return JointCategory.Toe;
            if (Match(n, @"(base|root|pelvis|floating_?base)"))        return JointCategory.Base;
            return JointCategory.Unknown;
        }

        static JointSide ClassifySide(string n)
        {
            // Quadruped quadrants (Unitree/Anymal convention).
            if (Match(n, @"\bfl[_\W]"))  return JointSide.FrontLeft;
            if (Match(n, @"\bfr[_\W]"))  return JointSide.FrontRight;
            if (Match(n, @"\brl[_\W]"))  return JointSide.RearLeft;
            if (Match(n, @"\brr[_\W]"))  return JointSide.RearRight;
            if (Match(n, @"^front_?left|_front_?left")) return JointSide.FrontLeft;
            if (Match(n, @"^front_?right|_front_?right")) return JointSide.FrontRight;
            if (Match(n, @"^rear_?left|_rear_?left|^back_?left|_back_?left")) return JointSide.RearLeft;
            if (Match(n, @"^rear_?right|_rear_?right|^back_?right|_back_?right")) return JointSide.RearRight;

            // Humanoid L/R.
            if (Match(n, @"(^|_)(left|l)(_|\d|$)"))  return JointSide.Left;
            if (Match(n, @"(^|_)(right|r)(_|\d|$)")) return JointSide.Right;

            return JointSide.None;
        }

        static int ClassifySubAxis(string n)
        {
            if (n.Contains("roll"))  return 1;
            if (n.Contains("yaw"))   return 2;
            if (n.Contains("pitch")) return 0;
            return 0;
        }

        static bool Match(string s, string pat) =>
            Regex.IsMatch(s, pat, RegexOptions.CultureInvariant);
    }

    public enum RobotMorphology { Unknown, Humanoid, Quadruped, Arm }

    public static class MorphologyDetector
    {
        public static RobotMorphology Detect(UrdfRobotInstance robot)
        {
            int shoulders = 0, hips = 0;
            int fl = 0, fr = 0, rl = 0, rr = 0;
            foreach (var j in robot.ActuatedJoints)
            {
                var c = JointClassifier.Classify(j.Name);
                if (c.Category == JointCategory.Shoulder) shoulders++;
                if (c.Category == JointCategory.Hip)      hips++;
                if (c.Side == JointSide.FrontLeft)  fl++;
                if (c.Side == JointSide.FrontRight) fr++;
                if (c.Side == JointSide.RearLeft)   rl++;
                if (c.Side == JointSide.RearRight)  rr++;
            }
            if (fl > 0 && fr > 0 && rl > 0 && rr > 0) return RobotMorphology.Quadruped;
            if (shoulders >= 2 && hips >= 2)          return RobotMorphology.Humanoid;
            if (shoulders == 0 && hips == 0)          return RobotMorphology.Arm;
            return RobotMorphology.Unknown;
        }
    }
}
