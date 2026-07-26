using System;

namespace Playa
{
    // Headless, deterministic Kuramoto crowd. Owns SoA arrays and advances one
    // timestep on demand. No Unity dependencies — DanceFloor wraps this and
    // supplies visuals; InterventionalTests wraps it and runs many seeds.
    //
    // Locomotion: 1/r separation + weak local cohesion + low-frequency Perlin-ish
    // wander, velocity-clamped. Since the whole point is "people not going
    // anywhere", locomotion is deliberately soft — bodies mill.
    public sealed class CrowdSimulator
    {
        public readonly int Count;
        public readonly float FloorRadius;

        public readonly float[] Theta;    // phase, wrapped to [0, 4π)
        public readonly float[] Omega;    // natural angular frequency (rad/s)
        public readonly float[] PosXZ;    // interleaved (x,z) of length 2·N
        public readonly float[] VelXZ;    // interleaved (vx,vz) of length 2·N

        public KuramotoParams Params;

        // Rebuild Params with a new beat frequency (music tempo). Everything
        // else stays. Called by DanceFloor when the MusicBeat's BPM changes.
        public void SetBeatOmega(float newOmega)
        {
            Params = new KuramotoParams(
                newOmega,
                Params.BeatCoupling,
                Params.PeerCoupling,
                Params.PeerRadius,
                Params.PlayerCoupling,
                Params.PlayerRadius);
        }

        // Locomotion knobs — tuned for milling rather than goal-directed motion.
        public float MaxSpeed = 0.9f;
        public float SeparationRadius = 0.9f;
        public float SeparationStrength = 1.6f;
        public float CohesionRadius = 3.0f;
        public float CohesionStrength = 0.15f;
        public float WanderStrength = 0.35f;
        public float VelocityDamping = 3.0f;   // (1/s) exponential drag

        readonly float[] nextTheta;
        readonly float[] wanderPhaseX;
        readonly float[] wanderPhaseZ;
        readonly Random rng;

        public float BeatPhase { get; private set; }
        public float TimeSeconds { get; private set; }

        public CrowdSimulator(
            int count,
            float floorRadius,
            int seed,
            KuramotoParams parameters,
            float freqSigma = 0.7f)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
            FloorRadius = floorRadius;
            Params = parameters;
            rng = new Random(seed);

            Theta = new float[count];
            Omega = new float[count];
            PosXZ = new float[count * 2];
            VelXZ = new float[count * 2];
            nextTheta = new float[count];
            wanderPhaseX = new float[count];
            wanderPhaseZ = new float[count];

            for (int i = 0; i < count; i++)
            {
                Theta[i] = (float)(rng.NextDouble() * KuramotoMath.FourPi);
                // ω_i ~ N(ω_beat, σ²) via Box–Muller
                double u1 = 1.0 - rng.NextDouble();
                double u2 = 1.0 - rng.NextDouble();
                double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                Omega[i] = parameters.BeatOmega + freqSigma * (float)z;

                // Poisson-ish disc scatter: reject if too close to another dancer.
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    float r = (float)Math.Sqrt(rng.NextDouble()) * floorRadius * 0.92f;
                    float a = (float)(rng.NextDouble() * KuramotoMath.TwoPi);
                    float x = r * (float)Math.Cos(a);
                    float z2 = r * (float)Math.Sin(a);
                    bool ok = true;
                    for (int j = 0; j < i; j++)
                    {
                        float dx = PosXZ[2 * j] - x;
                        float dz = PosXZ[2 * j + 1] - z2;
                        if (dx * dx + dz * dz < 0.6f * 0.6f) { ok = false; break; }
                    }
                    if (ok || attempt == 31)
                    {
                        PosXZ[2 * i] = x;
                        PosXZ[2 * i + 1] = z2;
                        break;
                    }
                }

                wanderPhaseX[i] = (float)(rng.NextDouble() * KuramotoMath.TwoPi);
                wanderPhaseZ[i] = (float)(rng.NextDouble() * KuramotoMath.TwoPi);
            }
        }

        // Advance dt seconds. Callers who want a static crowd (baseline tests)
        // can pass moveAgents=false. `extras` broadcasts additional pullers
        // (e.g. the robot when Partnering) into every agent's dynamics.
        public void Step(
            float dt,
            float playerX,
            float playerZ,
            float playerPhase,
            bool playerActive,
            bool moveAgents = true,
            KuramotoMath.ExternalInfluence[] extras = null)
        {
            BeatPhase = KuramotoMath.WrapTo4Pi(BeatPhase + Params.BeatOmega * dt);
            TimeSeconds += dt;

            KuramotoMath.StepPhases(
                Theta, Omega, PosXZ,
                BeatPhase,
                playerX, playerZ, playerPhase, playerActive,
                dt, Params, nextTheta, extras);
            Buffer.BlockCopy(nextTheta, 0, Theta, 0, sizeof(float) * Count);

            if (moveAgents) StepLocomotion(dt);
        }

        void StepLocomotion(float dt)
        {
            float sepR2 = SeparationRadius * SeparationRadius;
            float cohR2 = CohesionRadius * CohesionRadius;
            float damp = (float)Math.Exp(-VelocityDamping * dt);

            for (int i = 0; i < Count; i++)
            {
                float xi = PosXZ[2 * i];
                float zi = PosXZ[2 * i + 1];
                float ax = 0f, az = 0f;

                float cohX = 0f, cohZ = 0f;
                int cohN = 0;
                for (int j = 0; j < Count; j++)
                {
                    if (j == i) continue;
                    float dx = PosXZ[2 * j] - xi;
                    float dz = PosXZ[2 * j + 1] - zi;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < 1e-4f) continue;
                    if (d2 < sepR2)
                    {
                        float invD = 1f / (float)Math.Sqrt(d2);
                        float w = SeparationStrength * (SeparationRadius * invD - 1f);
                        ax -= dx * invD * w;
                        az -= dz * invD * w;
                    }
                    if (d2 < cohR2)
                    {
                        cohX += PosXZ[2 * j];
                        cohZ += PosXZ[2 * j + 1];
                        cohN++;
                    }
                }
                if (cohN > 0)
                {
                    ax += CohesionStrength * (cohX / cohN - xi);
                    az += CohesionStrength * (cohZ / cohN - zi);
                }

                // Slow wander — two independent low-frequency oscillators.
                float wt = TimeSeconds;
                ax += WanderStrength * (float)Math.Sin(0.31f * wt + wanderPhaseX[i]);
                az += WanderStrength * (float)Math.Cos(0.27f * wt + wanderPhaseZ[i]);

                // Soft ring — keep bodies on the playa floor.
                float r = (float)Math.Sqrt(xi * xi + zi * zi);
                float edge = FloorRadius * 0.95f;
                if (r > edge)
                {
                    float invR = 1f / Math.Max(r, 1e-3f);
                    float push = 4f * (r - edge);
                    ax -= xi * invR * push;
                    az -= zi * invR * push;
                }

                float vx = VelXZ[2 * i] * damp + ax * dt;
                float vz = VelXZ[2 * i + 1] * damp + az * dt;
                float sp = (float)Math.Sqrt(vx * vx + vz * vz);
                if (sp > MaxSpeed)
                {
                    float s = MaxSpeed / sp;
                    vx *= s; vz *= s;
                }
                VelXZ[2 * i] = vx;
                VelXZ[2 * i + 1] = vz;
                PosXZ[2 * i] += vx * dt;
                PosXZ[2 * i + 1] += vz * dt;
            }
        }

        public float OrderGlobal()
        {
            return KuramotoMath.OrderParameterR(Theta);
        }

        // Fills `subsetBuffer` (must be ≥ Count) with indices within radius of
        // (cx,cz) and returns (count, R, ψ).
        public int OrderLocal(
            float cx, float cz, float radius,
            int[] subsetBuffer,
            out float r, out float psi)
        {
            int n = KuramotoMath.GatherWithinRadius(PosXZ, cx, cz, radius, subsetBuffer);
            KuramotoMath.OrderParameter(Theta, subsetBuffer, n, out r, out psi);
            return n;
        }
    }
}
