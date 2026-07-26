using System.Collections.Generic;
using UnityEngine;
using Playa.Urdf;

namespace Playa.Verification
{
    // Static balance check for legged robots:
    //   1. Compute whole-body CoM as the mass-weighted world position of every
    //      link's inertial origin.
    //   2. Project onto ground (Y = robot's foot-plane Y, approximated as the
    //      minimum Y of the identified foot links).
    //   3. Build support polygon from identified foot links, buffered by
    //      footPaddingMeters, and test whether the CoM projection lies inside.
    //
    // Foot links are identified heuristically by name (foot|toe|ankle). For
    // arms and single-link bases the validator is a no-op — Balance = OK.
    //
    // This is *static* balance only — it ignores momentum. A robot that's
    // motionless with CoM inside the polygon is stable; a robot dancing is
    // dynamically stable in ways this test won't catch. Good enough for a
    // "does this pose fall over?" red-flag check.
    public sealed class BalanceValidator
    {
        readonly List<Transform> footTransforms = new List<Transform>();
        readonly List<(Transform tx, float mass, Vector3 comLocal)> massSources =
            new List<(Transform, float, Vector3)>();
        readonly float footPaddingMeters;
        readonly bool applicable;

        public bool Applicable => applicable;

        public BalanceValidator(UrdfRobotInstance robot, float footPaddingMeters = 0.08f)
        {
            this.footPaddingMeters = footPaddingMeters;

            // Prefer explicit foot/sole/toe links; fall back to ankle links
            // if those are the lowest thing the URDF exposes.
            foreach (var kv in robot.LinkTransformByName)
            {
                var n = kv.Key.ToLowerInvariant();
                if (n.Contains("foot") || n.Contains("sole") || n.Contains("toe"))
                    footTransforms.Add(kv.Value);
            }
            if (footTransforms.Count < 2)
            {
                foreach (var kv in robot.LinkTransformByName)
                {
                    var n = kv.Key.ToLowerInvariant();
                    if (n.Contains("ankle")) footTransforms.Add(kv.Value);
                }
            }
            applicable = footTransforms.Count >= 2;

            foreach (var link in robot.Spec.Links)
            {
                if (link.Inertial == null || link.Inertial.Mass <= 0f) continue;
                if (!robot.LinkTransformByName.TryGetValue(link.Name, out var tx)) continue;
                var comLocal = UrdfMath.UrdfToUnityPos(link.Inertial.Origin.Xyz);
                massSources.Add((tx, link.Inertial.Mass, comLocal));
            }
        }

        public void CheckInto(FeasibilityReport report)
        {
            if (!applicable)
            {
                report.BalanceStatus = FeasibilityStatus.OK;
                report.BalanceMarginNormalized = 1f;
                return;
            }

            // World-space CoM.
            float totalMass = 0f;
            Vector3 weighted = Vector3.zero;
            for (int i = 0; i < massSources.Count; i++)
            {
                var (tx, mass, comLocal) = massSources[i];
                weighted += tx.TransformPoint(comLocal) * mass;
                totalMass += mass;
            }
            if (totalMass <= 1e-6f)
            {
                report.BalanceStatus = FeasibilityStatus.OK;
                report.BalanceMarginNormalized = 1f;
                return;
            }
            Vector3 comWorld = weighted / totalMass;

            // Ground plane Y = min foot Y.
            float groundY = float.PositiveInfinity;
            var footPos = new Vector2[footTransforms.Count];
            for (int i = 0; i < footTransforms.Count; i++)
            {
                var p = footTransforms[i].position;
                footPos[i] = new Vector2(p.x, p.z);
                if (p.y < groundY) groundY = p.y;
            }
            Vector2 comXZ = new Vector2(comWorld.x, comWorld.z);

            // Test: CoM projection inside padded convex hull of foot points.
            float signedMargin = SignedMarginToPolygon(comXZ, footPos, footPaddingMeters);
            // signedMargin > 0 → inside by that many meters; < 0 → outside.
            float normalized = Mathf.Clamp01(signedMargin / Mathf.Max(0.05f, footPaddingMeters * 2f));
            report.BalanceMarginNormalized = normalized;

            if (signedMargin < 0f)          report.BalanceStatus = FeasibilityStatus.Violation;
            else if (normalized < 0.20f)    report.BalanceStatus = FeasibilityStatus.Warn;
            else                            report.BalanceStatus = FeasibilityStatus.OK;
        }

        // ---- geometry -------------------------------------------------------

        // Returns signed distance: positive = inside polygon by that much,
        // negative = outside. Polygon is the padded convex hull of `points`.
        static float SignedMarginToPolygon(Vector2 query, Vector2[] points, float pad)
        {
            if (points.Length == 0) return -1f;
            if (points.Length == 1) return pad - Vector2.Distance(query, points[0]);
            if (points.Length == 2)
            {
                float d = DistanceToSegment(query, points[0], points[1]);
                return pad - d;
            }
            var hull = ConvexHull(points);
            // Point-in-polygon via winding, then distance to nearest edge.
            bool inside = PointInPolygon(query, hull);
            float minEdgeDist = float.PositiveInfinity;
            for (int i = 0; i < hull.Length; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Length];
                float d = DistanceToSegment(query, a, b);
                if (d < minEdgeDist) minEdgeDist = d;
            }
            return inside ? (minEdgeDist + pad) : -(minEdgeDist - pad);
        }

        static bool PointInPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) /
                        (poly[j].y - poly[i].y + 1e-9f) + poly[i].x))
                    inside = !inside;
            }
            return inside;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-9f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        // Andrew's monotone chain — O(n log n). Overkill for 2-4 feet but correct.
        static Vector2[] ConvexHull(Vector2[] pts)
        {
            var sorted = new List<Vector2>(pts);
            sorted.Sort((u, v) => u.x != v.x ? u.x.CompareTo(v.x) : u.y.CompareTo(v.y));
            int n = sorted.Count;
            var hull = new List<Vector2>(2 * n);
            // Lower
            for (int i = 0; i < n; i++)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], sorted[i]) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(sorted[i]);
            }
            // Upper
            int lower = hull.Count + 1;
            for (int i = n - 2; i >= 0; i--)
            {
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], sorted[i]) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(sorted[i]);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull.ToArray();
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
            (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }
}
