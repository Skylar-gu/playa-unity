using System;

namespace Playa
{
    public readonly struct KuramotoParams
    {
        public readonly float BeatOmega;
        public readonly float BeatCoupling;
        public readonly float PeerCoupling;
        public readonly float PeerRadius;
        public readonly float PlayerCoupling;
        public readonly float PlayerRadius;

        public KuramotoParams(
            float beatOmega,
            float beatCoupling,
            float peerCoupling,
            float peerRadius,
            float playerCoupling,
            float playerRadius)
        {
            BeatOmega = beatOmega;
            BeatCoupling = beatCoupling;
            PeerCoupling = peerCoupling;
            PeerRadius = peerRadius;
            PlayerCoupling = playerCoupling;
            PlayerRadius = playerRadius;
        }

        public static KuramotoParams Defaults => new KuramotoParams(
            beatOmega: 4f * (float)Math.PI,
            beatCoupling: 0.15f,
            peerCoupling: 0.9f,
            peerRadius: 4.0f,
            playerCoupling: 3.0f,
            playerRadius: 6.0f);
    }

    // Pure static math for the phase-coupled crowd. No Unity dependencies so
    // every function is exercisable from EditMode tests without a scene.
    public static class KuramotoMath
    {
        // An external phase-coupling agent (the player, the robot, or any other
        // outside actor that pulls on the crowd but is not itself part of the
        // Kuramoto SoA). Linear-falloff coupling identical to the player term.
        public struct ExternalInfluence
        {
            public float X, Z;
            public float Phase;
            public float Coupling0;
            public float Radius;
            public bool Active;
        }

        public const float TwoPi = 6.28318530717958647692f;
        public const float FourPi = 12.5663706143591729538f;

        // Wrap to [0, 4π). Phases are carried at 4π period so the half-frequency
        // sway term sin(θ/2) stays continuous across the seam. Since 4π is a
        // multiple of 2π, sin(θ) and e^{iθ} are unaffected.
        public static float WrapTo4Pi(float theta)
        {
            theta %= FourPi;
            if (theta < 0f) theta += FourPi;
            return theta;
        }

        // Kp(d) = Kp0 · max(0, 1 - d/rp). Linear falloff to zero at rp.
        public static float PlayerCouplingFalloff(float distance, float k0, float rp)
        {
            if (rp <= 0f) return 0f;
            float t = 1f - distance / rp;
            if (t <= 0f) return 0f;
            return k0 * t;
        }

        // Kuramoto order parameter R e^{iψ} over the indices in `subset`.
        // Returns R∈[0,1] and ψ∈[-π,π]. If `subset` is null, uses all agents.
        public static void OrderParameter(
            float[] theta,
            int[] subset,
            out float r,
            out float psi)
        {
            OrderParameter(theta, subset, subset?.Length ?? theta.Length, out r, out psi);
        }

        // Overload that takes an explicit count — lets callers reuse a scratch
        // int[] buffer without slicing it.
        public static void OrderParameter(
            float[] theta,
            int[] subset,
            int count,
            out float r,
            out float psi)
        {
            if (count == 0) { r = 0f; psi = 0f; return; }
            double cx = 0.0, cy = 0.0;
            for (int k = 0; k < count; k++)
            {
                int i = subset == null ? k : subset[k];
                cx += Math.Cos(theta[i]);
                cy += Math.Sin(theta[i]);
            }
            cx /= count; cy /= count;
            r = (float)Math.Sqrt(cx * cx + cy * cy);
            psi = (float)Math.Atan2(cy, cx);
        }

        // Convenience overload — R over the whole array.
        public static float OrderParameterR(float[] theta)
        {
            OrderParameter(theta, null, out float r, out _);
            return r;
        }

        // One forward-Euler step of the coupled phase ODE for all agents.
        //
        //   θ̇_i = ω_i + K_b sin(θ_beat - θ_i)
        //              + (K_s / |N_i|) Σ_{j∈N_i} sin(θ_j - θ_i)
        //              + K_p(d_i) sin(θ_p - θ_i)             ← the player
        //              + Σ_k K_extra(d_ik) sin(θ_k - θ_i)    ← e.g. the robot
        //
        // Writes new phases into `next`. Reads previous phases from `theta`.
        // Positions are packed as (x,z) pairs of length 2N in `posXZ`.
        // Stable while K·Δt ≪ 1, which holds for K≲3 at 60 fps.
        public static void StepPhases(
            float[] theta,
            float[] omega,
            float[] posXZ,
            float betaPhase,
            float playerX,
            float playerZ,
            float playerPhase,
            bool playerActive,
            float dt,
            in KuramotoParams p,
            float[] next,
            ExternalInfluence[] extras = null)
        {
            int n = theta.Length;
            float rs2 = p.PeerRadius * p.PeerRadius;
            int extraCount = extras?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                float ti = theta[i];
                float dtheta = omega[i];
                dtheta += p.BeatCoupling * (float)Math.Sin(betaPhase - ti);

                double peerSum = 0.0;
                int neighbours = 0;
                float xi = posXZ[2 * i];
                float zi = posXZ[2 * i + 1];
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    float dx = posXZ[2 * j] - xi;
                    float dz = posXZ[2 * j + 1] - zi;
                    if (dx * dx + dz * dz > rs2) continue;
                    peerSum += Math.Sin(theta[j] - ti);
                    neighbours++;
                }
                if (neighbours > 0)
                    dtheta += (float)(p.PeerCoupling * peerSum / neighbours);

                if (playerActive)
                {
                    float dxp = playerX - xi;
                    float dzp = playerZ - zi;
                    float dp = (float)Math.Sqrt(dxp * dxp + dzp * dzp);
                    float kp = PlayerCouplingFalloff(dp, p.PlayerCoupling, p.PlayerRadius);
                    if (kp > 0f)
                        dtheta += kp * (float)Math.Sin(playerPhase - ti);
                }

                for (int k = 0; k < extraCount; k++)
                {
                    var e = extras[k];
                    if (!e.Active) continue;
                    float dxe = e.X - xi;
                    float dze = e.Z - zi;
                    float de = (float)Math.Sqrt(dxe * dxe + dze * dze);
                    float ke = PlayerCouplingFalloff(de, e.Coupling0, e.Radius);
                    if (ke > 0f)
                        dtheta += ke * (float)Math.Sin(e.Phase - ti);
                }

                next[i] = WrapTo4Pi(ti + dtheta * dt);
            }
        }

        // Count of agents whose (x,z) is within `radius` of (cx,cz), and fills
        // `subset` (must be sized ≥ theta.Length) with their indices.
        public static int GatherWithinRadius(
            float[] posXZ,
            float cx,
            float cz,
            float radius,
            int[] subset)
        {
            int n = posXZ.Length / 2;
            float r2 = radius * radius;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                float dx = posXZ[2 * i] - cx;
                float dz = posXZ[2 * i + 1] - cz;
                if (dx * dx + dz * dz <= r2)
                {
                    subset[count++] = i;
                }
            }
            return count;
        }
    }
}
