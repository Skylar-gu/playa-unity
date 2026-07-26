using System;
using NUnit.Framework;

namespace Playa.Tests
{
    // Interventional tests — the load-bearing ones for this project. Each runs
    // the pure CrowdSimulator across many seeds and asserts on the aggregate
    // statistic, not on any single seed. This is the code-level analogue of
    // §2's H1: a causal handle (the player) should produce a measurable effect
    // above baseline, robustly across noise realizations.
    //
    // Categories:
    //   Baseline        — no player, verify sub-transition behavior
    //   PlayerControl   — with a phase-locked tap, verify R_local rises
    //   Decoupling      — verify the sim releases the crowd when player idles
    //   Reproducibility — same seed → same trajectory
    //   Density         — H1-adjacent: intervention effect scales with n_local
    //
    // Runtime tuning knobs at top of file — lower Count/Seeds if these get slow.
    [Category("Interventional")]
    public class InterventionalTests
    {
        // Tuned to guarantee n_local ≥ 8 within player radius on typical seeds.
        // At Count=80, FloorRadius=16, expected neighbours within r_p=6 is ~11.
        const int Count = 80;
        const float FloorRadius = 16f;
        const int Seeds = 16;
        const float Dt = 1f / 60f;

        static KuramotoParams StandardParams() => new KuramotoParams(
            beatOmega: 4f * (float)Math.PI,
            beatCoupling: 0.15f,
            peerCoupling: 0.9f,
            peerRadius: 4.0f,
            playerCoupling: 3.0f,
            playerRadius: 6.0f);

        // Small helper — median R_global over the last `windowSeconds` of the
        // simulation, averaged across seeds.
        static float MedianAcrossSeeds(
            Func<int, float> perSeed)
        {
            var values = new float[Seeds];
            for (int s = 0; s < Seeds; s++) values[s] = perSeed(s);
            Array.Sort(values);
            return values[Seeds / 2];
        }

        // ---- Baseline: no player, crowd sits below transition -----------

        [Test]
        public void Baseline_MedianRGlobalStaysLow()
        {
            const float SimSeconds = 30f;
            const float WindowSeconds = 5f;
            float median = MedianAcrossSeeds(seed =>
            {
                var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
                float sumR = 0f; int samples = 0;
                for (float t = 0f; t < SimSeconds; t += Dt)
                {
                    sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);
                    if (t >= SimSeconds - WindowSeconds)
                    {
                        sumR += sim.OrderGlobal();
                        samples++;
                    }
                }
                return sumR / samples;
            });
            // Below K_c we do not expect full sync. With K_s = 0.8 K_c and local
            // coupling, partial spatial domains can push R_global up somewhat,
            // but the median across seeds should stay comfortably below 1.
            Assert.Less(median, 0.75f,
                $"Baseline crowd should not fully self-lock; median R_global={median:F3}.");
        }

        [Test]
        public void Baseline_StageOnlyDoesNotLockCrowd()
        {
            // K_b = 0.15 ≪ σ = 0.7 → per Adler, most agents are outside the
            // locking band. The stage sets a bias, not a lock.
            const float SimSeconds = 20f;
            float median = MedianAcrossSeeds(seed =>
            {
                var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
                for (float t = 0f; t < SimSeconds; t += Dt)
                    sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);
                return sim.OrderGlobal();
            });
            Assert.Less(median, 0.85f,
                $"Weak stage coupling must not produce near-perfect global sync; got {median:F3}.");
        }

        // ---- Intervention: player at centre, tapping the beat -----------

        [Test]
        public void PlayerAtCentre_RLocalRisesAboveBaseline()
        {
            const float SimSeconds = 15f;
            const float PlayerRadius = 6f;

            float withPlayer = MedianAcrossSeeds(seed =>
            {
                var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
                var localBuf = new int[Count];
                // Warm the crowd up first without a player so R_local starts low.
                for (float t = 0f; t < 3f; t += Dt)
                    sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);

                float playerPhase = 0f;
                float finalRLocal = 0f;
                for (float t = 0f; t < SimSeconds; t += Dt)
                {
                    playerPhase = KuramotoMath.WrapTo4Pi(playerPhase + 4f * (float)Math.PI * Dt);
                    sim.Step(Dt, 0f, 0f, playerPhase, true, moveAgents: false);
                    sim.OrderLocal(0f, 0f, PlayerRadius, localBuf, out finalRLocal, out _);
                }
                return finalRLocal;
            });

            float withoutPlayer = MedianAcrossSeeds(seed =>
            {
                var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
                var localBuf = new int[Count];
                for (float t = 0f; t < 3f + SimSeconds; t += Dt)
                    sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);
                sim.OrderLocal(0f, 0f, PlayerRadius, localBuf, out float r, out _);
                return r;
            });

            Assert.Greater(withPlayer, 0.75f,
                $"With a player tapping the beat at the centre, median R_local should be high; got {withPlayer:F3}.");
            Assert.Greater(withPlayer - withoutPlayer, 0.2f,
                $"Intervention effect too small: with={withPlayer:F3}, without={withoutPlayer:F3}.");
        }

        [Test]
        public void PlayerAtCentre_IgnitionThresholdMetInTime()
        {
            // Objective from §4: R_local ≥ 0.80 with n_local ≥ 8 for 5 s.
            // A stricter test: median time-to-first-cross of R_local=0.8 with
            // the player parked at the crowd centre should be ≤ 8 s.
            const float MaxSeconds = 20f;
            const float PlayerRadius = 6f;

            var timesToCross = new float[Seeds];
            for (int seed = 0; seed < Seeds; seed++)
            {
                var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
                var localBuf = new int[Count];
                for (float t = 0f; t < 2f; t += Dt) sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);
                float playerPhase = 0f;
                float crossed = -1f;
                for (float t = 0f; t < MaxSeconds; t += Dt)
                {
                    playerPhase = KuramotoMath.WrapTo4Pi(playerPhase + 4f * (float)Math.PI * Dt);
                    sim.Step(Dt, 0f, 0f, playerPhase, true, moveAgents: false);
                    int n = sim.OrderLocal(0f, 0f, PlayerRadius, localBuf, out float r, out _);
                    if (r >= 0.8f && n >= 8) { crossed = t; break; }
                }
                timesToCross[seed] = crossed < 0f ? MaxSeconds : crossed;
            }
            Array.Sort(timesToCross);
            float median = timesToCross[Seeds / 2];
            Assert.Less(median, 8f,
                $"Median time to cross R_local≥0.8 with 8 neighbours should be ≤ 8 s; got {median:F2}s.");
        }

        // ---- Decoupling: idle player releases the crowd -----------------

        [Test]
        public void DecouplingAfterIdleAllowsRLocalToDecay()
        {
            const float PlayerRadius = 6f;
            float delta = MedianAcrossSeeds(seed =>
            {
                var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
                var localBuf = new int[Count];
                float playerPhase = 0f;

                // Drive to lock.
                for (float t = 0f; t < 12f; t += Dt)
                {
                    playerPhase = KuramotoMath.WrapTo4Pi(playerPhase + 4f * (float)Math.PI * Dt);
                    sim.Step(Dt, 0f, 0f, playerPhase, true, moveAgents: false);
                }
                sim.OrderLocal(0f, 0f, PlayerRadius, localBuf, out float rLocked, out _);

                // Withdraw player and let σ-noise drag the crowd back apart.
                for (float t = 0f; t < 10f; t += Dt)
                    sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);
                sim.OrderLocal(0f, 0f, PlayerRadius, localBuf, out float rAfter, out _);
                return rLocked - rAfter;
            });
            Assert.Greater(delta, 0.10f,
                $"R_local should decay after the player decouples; median Δ = {delta:F3}.");
        }

        // ---- Reproducibility --------------------------------------------

        [Test]
        public void SameSeedProducesIdenticalTrajectories()
        {
            var a = new CrowdSimulator(Count, FloorRadius, 42, StandardParams());
            var b = new CrowdSimulator(Count, FloorRadius, 42, StandardParams());
            float playerPhase = 0f;
            for (float t = 0f; t < 5f; t += Dt)
            {
                playerPhase = KuramotoMath.WrapTo4Pi(playerPhase + 4f * (float)Math.PI * Dt);
                a.Step(Dt, 0.3f, 0.7f, playerPhase, true);
                b.Step(Dt, 0.3f, 0.7f, playerPhase, true);
            }
            for (int i = 0; i < Count; i++)
            {
                Assert.AreEqual(a.Theta[i], b.Theta[i], 1e-6f,
                    $"Determinism broken at agent {i}");
                Assert.AreEqual(a.PosXZ[2 * i], b.PosXZ[2 * i], 1e-4f);
                Assert.AreEqual(a.PosXZ[2 * i + 1], b.PosXZ[2 * i + 1], 1e-4f);
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentTrajectories()
        {
            var a = new CrowdSimulator(Count, FloorRadius, 1, StandardParams());
            var b = new CrowdSimulator(Count, FloorRadius, 2, StandardParams());
            for (float t = 0f; t < 2f; t += Dt)
            {
                a.Step(Dt, 0f, 0f, 0f, false);
                b.Step(Dt, 0f, 0f, 0f, false);
            }
            float diffSum = 0f;
            for (int i = 0; i < Count; i++)
                diffSum += Math.Abs(a.Theta[i] - b.Theta[i]);
            Assert.Greater(diffSum, 1f, "Distinct seeds should diverge.");
        }

        // ---- Robot broadcast: partner-dance pulls the crowd -----------

        [Test]
        public void ExternalInfluence_LocksNearbyAgents()
        {
            // The robot's Partnering broadcast is an ExternalInfluence at its
            // position. Verify a stationary strong external influence at (0,0)
            // pulls local agents to R ≥ 0.85 across seeds.
            const float SimSeconds = 10f;
            const float R = 6f;
            float median = MedianAcrossSeeds(seed =>
            {
                var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
                var localBuf = new int[Count];
                for (float t = 0f; t < 2f; t += Dt) sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);

                float rp = 0f;
                var extras = new KuramotoMath.ExternalInfluence[1];
                for (float t = 0f; t < SimSeconds; t += Dt)
                {
                    rp = KuramotoMath.WrapTo4Pi(rp + 4f * (float)Math.PI * Dt);
                    extras[0] = new KuramotoMath.ExternalInfluence {
                        X = 0f, Z = 0f, Phase = rp,
                        Coupling0 = 3.0f, Radius = R, Active = true,
                    };
                    // No player, just the external "robot" broadcast.
                    sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false, extras: extras);
                }
                sim.OrderLocal(0f, 0f, R, localBuf, out float rLocal, out _);
                return rLocal;
            });
            Assert.Greater(median, 0.80f,
                $"External influence should lock local agents; median R_local = {median:F3}.");
        }

        [Test]
        public void PlayerAndRobot_TogetherDominateCrowd()
        {
            // The partner-dance scenario: player at (0,0) tapping beat, robot
            // right beside them broadcasting the same beat. Combined coupling
            // should exceed either alone.
            const float SimSeconds = 10f;
            const float R = 6f;

            float bothMedian = MedianAcrossSeeds(seed =>
            {
                var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
                var localBuf = new int[Count];
                for (float t = 0f; t < 2f; t += Dt) sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);
                float phase = 0f;
                var extras = new KuramotoMath.ExternalInfluence[1];
                for (float t = 0f; t < SimSeconds; t += Dt)
                {
                    phase = KuramotoMath.WrapTo4Pi(phase + 4f * (float)Math.PI * Dt);
                    extras[0] = new KuramotoMath.ExternalInfluence {
                        X = 0.5f, Z = 0f, Phase = phase,
                        Coupling0 = 2.4f, Radius = 8f, Active = true,
                    };
                    sim.Step(Dt, 0f, 0f, phase, true, moveAgents: false, extras: extras);
                }
                sim.OrderLocal(0f, 0f, R, localBuf, out float r, out _);
                return r;
            });

            float playerOnlyMedian = MedianAcrossSeeds(seed =>
            {
                var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
                var localBuf = new int[Count];
                for (float t = 0f; t < 2f; t += Dt) sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);
                float phase = 0f;
                for (float t = 0f; t < SimSeconds; t += Dt)
                {
                    phase = KuramotoMath.WrapTo4Pi(phase + 4f * (float)Math.PI * Dt);
                    sim.Step(Dt, 0f, 0f, phase, true, moveAgents: false);
                }
                sim.OrderLocal(0f, 0f, R, localBuf, out float r, out _);
                return r;
            });

            Assert.GreaterOrEqual(bothMedian, playerOnlyMedian - 0.02f,
                $"Player+robot combined should not be weaker than player alone. " +
                $"both={bothMedian:F3}, playerOnly={playerOnlyMedian:F3}.");
            Assert.Greater(bothMedian, 0.85f,
                $"Partner-dance broadcast should lock the local crowd; got {bothMedian:F3}.");
        }

        [Test]
        public void InactiveExtras_DoNotAffectSim()
        {
            // Extras with Active=false must be no-ops so the sim behaves
            // identically to no-extras runs. Prevents regressions where the
            // broadcast leaks when the robot is Observing.
            var a = new CrowdSimulator(Count, FloorRadius, 7, StandardParams());
            var b = new CrowdSimulator(Count, FloorRadius, 7, StandardParams());
            var extras = new[] { new KuramotoMath.ExternalInfluence {
                X = 0f, Z = 0f, Phase = 0f,
                Coupling0 = 5f, Radius = 20f, Active = false,
            }};
            for (float t = 0f; t < 3f; t += Dt)
            {
                a.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);
                b.Step(Dt, 0f, 0f, 0f, false, moveAgents: false, extras: extras);
            }
            for (int i = 0; i < Count; i++)
                Assert.AreEqual(a.Theta[i], b.Theta[i], 1e-6f,
                    $"Inactive extras must be a no-op (agent {i})");
        }

        // ---- Spatial handle: H1-adjacent, unambiguous by construction --

        [Test]
        public void PlayerEffectFallsOffWithDistance()
        {
            // K_p(d) is linear falloff to zero at r_p, so agents INSIDE r_p
            // should be more phase-locked to the player than agents OUTSIDE.
            // This is the observable H1 predicts a phase-aware predictor would
            // exploit — the effect is spatially structured, not global.
            const float PlayerRadius = 6f;
            float innerMedian = MedianAcrossSeeds(seed =>
                LockingToPlayer(seed, insideRadius: true, PlayerRadius));
            float outerMedian = MedianAcrossSeeds(seed =>
                LockingToPlayer(seed, insideRadius: false, PlayerRadius));
            Assert.Greater(innerMedian - outerMedian, 0.15f,
                $"Locking to player should be stronger inside r_p than outside; " +
                $"inside={innerMedian:F3}, outside={outerMedian:F3}.");
        }

        // Mean cos(θ_i - θ_p) over agents in the requested annulus, at end of
        // a 12 s player-active run. This is the per-agent phase-locking value
        // (PLV surrogate) — high when i and player share phase, ~0 otherwise.
        static float LockingToPlayer(int seed, bool insideRadius, float playerRadius)
        {
            var sim = new CrowdSimulator(Count, FloorRadius, seed, StandardParams());
            for (float t = 0f; t < 2f; t += Dt) sim.Step(Dt, 0f, 0f, 0f, false, moveAgents: false);
            float phase = 0f;
            for (float t = 0f; t < 12f; t += Dt)
            {
                phase = KuramotoMath.WrapTo4Pi(phase + 4f * (float)Math.PI * Dt);
                sim.Step(Dt, 0f, 0f, phase, true, moveAgents: false);
            }
            double sum = 0.0; int n = 0;
            for (int i = 0; i < sim.Count; i++)
            {
                float dx = sim.PosXZ[2 * i];
                float dz = sim.PosXZ[2 * i + 1];
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                bool inside = d <= playerRadius;
                if (inside != insideRadius) continue;
                sum += Math.Cos(sim.Theta[i] - phase);
                n++;
            }
            return n == 0 ? 0f : (float)(sum / n);
        }
    }
}
