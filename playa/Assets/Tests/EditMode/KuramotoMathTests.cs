using System;
using NUnit.Framework;

namespace Playa.Tests
{
    public class KuramotoMathTests
    {
        [Test]
        public void WrapTo4Pi_IdentityWithinRange()
        {
            for (float t = 0f; t < KuramotoMath.FourPi; t += 0.5f)
            {
                Assert.AreEqual(t, KuramotoMath.WrapTo4Pi(t), 1e-4f);
            }
        }

        [Test]
        public void WrapTo4Pi_HandlesNegative()
        {
            float w = KuramotoMath.WrapTo4Pi(-0.5f);
            Assert.Greater(w, 0f);
            Assert.Less(w, KuramotoMath.FourPi);
            Assert.AreEqual(KuramotoMath.FourPi - 0.5f, w, 1e-3f);
        }

        [Test]
        public void WrapTo4Pi_HandlesLarge()
        {
            float w = KuramotoMath.WrapTo4Pi(3f * KuramotoMath.FourPi + 1.25f);
            Assert.AreEqual(1.25f, w, 1e-3f);
        }

        [Test]
        public void PlayerFalloff_LinearBetweenZeroAndRp()
        {
            Assert.AreEqual(3f, KuramotoMath.PlayerCouplingFalloff(0f, 3f, 6f), 1e-6f);
            Assert.AreEqual(1.5f, KuramotoMath.PlayerCouplingFalloff(3f, 3f, 6f), 1e-6f);
            Assert.AreEqual(0f, KuramotoMath.PlayerCouplingFalloff(6f, 3f, 6f), 1e-6f);
            Assert.AreEqual(0f, KuramotoMath.PlayerCouplingFalloff(9f, 3f, 6f), 1e-6f);
            Assert.AreEqual(0f, KuramotoMath.PlayerCouplingFalloff(1f, 3f, 0f), 1e-6f);
        }

        [Test]
        public void OrderParameter_IdenticalPhasesGivesR1()
        {
            var theta = new[] { 1.2345f, 1.2345f, 1.2345f, 1.2345f };
            KuramotoMath.OrderParameter(theta, null, out float r, out float psi);
            Assert.AreEqual(1f, r, 1e-5f);
            // ψ ≡ common phase (mod 2π)
            Assert.AreEqual(1.2345f, ((psi % KuramotoMath.TwoPi) + KuramotoMath.TwoPi) % KuramotoMath.TwoPi, 1e-4f);
        }

        [Test]
        public void OrderParameter_UniformlySpacedGivesR0()
        {
            int n = 32;
            var theta = new float[n];
            for (int i = 0; i < n; i++) theta[i] = i * KuramotoMath.TwoPi / n;
            Assert.AreEqual(0f, KuramotoMath.OrderParameterR(theta), 1e-5f);
        }

        [Test]
        public void OrderParameter_InvariantUnderGlobalShift()
        {
            var rng = new Random(1);
            int n = 40;
            var theta = new float[n];
            for (int i = 0; i < n; i++)
                theta[i] = (float)(rng.NextDouble() * KuramotoMath.FourPi);
            float r0 = KuramotoMath.OrderParameterR(theta);

            var shifted = (float[])theta.Clone();
            for (int i = 0; i < n; i++)
                shifted[i] = KuramotoMath.WrapTo4Pi(shifted[i] + 0.75f);
            float r1 = KuramotoMath.OrderParameterR(shifted);
            Assert.AreEqual(r0, r1, 1e-5f);
        }

        [Test]
        public void OrderParameter_SubsetIsHonored()
        {
            var theta = new[] { 0f, 0f, 0f, (float)Math.PI, (float)Math.PI, (float)Math.PI };
            KuramotoMath.OrderParameter(theta, new[] { 0, 1, 2 }, out float rInPhase, out _);
            KuramotoMath.OrderParameter(theta, new[] { 0, 3 }, out float rAntiphase, out _);
            Assert.AreEqual(1f, rInPhase, 1e-5f);
            Assert.AreEqual(0f, rAntiphase, 1e-5f);
        }

        [Test]
        public void StepPhases_FreeRunnerAdvancesAtOmega()
        {
            var theta = new[] { 0f };
            var omega = new[] { 2.0f };
            var pos = new[] { 0f, 0f };
            var next = new float[1];
            var p = new KuramotoParams(4f * (float)Math.PI, 0f, 0f, 1f, 0f, 1f);
            KuramotoMath.StepPhases(theta, omega, pos, 0f, 0f, 0f, 0f, false, 0.1f, p, next);
            Assert.AreEqual(0.2f, next[0], 1e-5f);
        }

        [Test]
        public void StepPhases_BeatCouplingPullsSingleOscillator()
        {
            // Single oscillator with ω = beatOmega, strong beat coupling → phase
            // difference to beat monotonically shrinks each step.
            var theta = new[] { 1.0f };
            var omega = new[] { 4f * (float)Math.PI };
            var pos = new[] { 0f, 0f };
            var next = new float[1];
            var p = new KuramotoParams(4f * (float)Math.PI, 2.0f, 0f, 1f, 0f, 1f);
            float betaPhase = 1.0f; // Start at the same phase to isolate coupling term.
            // Actually to isolate coupling: place agent at θ=1.5, beat at θ=1.0 → sin(-0.5) < 0 → dθ decrements
            theta[0] = 1.5f;
            KuramotoMath.StepPhases(theta, omega, pos, betaPhase, 0f, 0f, 0f, false, 0.01f, p, next);
            float driftFromNatural = (4f * (float)Math.PI) * 0.01f;
            Assert.Less(next[0] - theta[0], driftFromNatural,
                "Beat coupling should slow advance when agent leads the beat.");
        }

        [Test]
        public void StepPhases_PeerCouplingSyncsTwoAgents()
        {
            var theta = new[] { 0f, 1.0f };
            var omega = new[] { 0f, 0f };
            var pos = new[] { 0f, 0f, 0.5f, 0f };  // 0.5m apart, inside peer radius
            var next = new float[2];
            var p = new KuramotoParams(0f, 0f, 4f, 4f, 0f, 1f);

            for (int step = 0; step < 200; step++)
            {
                KuramotoMath.StepPhases(theta, omega, pos, 0f, 0f, 0f, 0f, false, 1f / 120f, p, next);
                Array.Copy(next, theta, 2);
            }
            float diff = Math.Abs(theta[0] - theta[1]);
            Assert.Less(diff, 0.05f, "Two peer-coupled agents should collapse to nearly the same phase.");
        }

        [Test]
        public void StepPhases_NoPeerContributionOutsideRadius()
        {
            var theta = new[] { 0f, 1.0f };
            var omega = new[] { 0f, 0f };
            var pos = new[] { 0f, 0f, 100f, 0f };  // Far beyond peer radius
            var next = new float[2];
            var p = new KuramotoParams(0f, 0f, 4f, 4f, 0f, 1f);
            KuramotoMath.StepPhases(theta, omega, pos, 0f, 0f, 0f, 0f, false, 1f / 60f, p, next);
            Assert.AreEqual(theta[0], next[0], 1e-6f);
            Assert.AreEqual(theta[1], next[1], 1e-6f);
        }

        [Test]
        public void GatherWithinRadius_CountsAndIndices()
        {
            var pos = new[] {
                0f, 0f,
                1f, 0f,
                5f, 5f,
                -1f, -1f,
            };
            var buf = new int[4];
            int n = KuramotoMath.GatherWithinRadius(pos, 0f, 0f, 2f, buf);
            Assert.AreEqual(3, n);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 3 }, new[] { buf[0], buf[1], buf[2] });
        }
    }
}
