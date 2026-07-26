using System;
using NUnit.Framework;

namespace Playa.Tests
{
    public class TapEstimatorTests
    {
        [Test]
        public void InitiallyInactive()
        {
            var te = new TapEstimator();
            te.Sample(0f, out float phase, out float omega, out bool active);
            Assert.IsFalse(active);
            Assert.AreEqual(0f, phase);
            Assert.AreEqual(0f, omega);
        }

        [Test]
        public void OneTapStillInactive()
        {
            var te = new TapEstimator();
            te.Tap(1.0f);
            te.Sample(1.1f, out _, out _, out bool active);
            Assert.IsFalse(active, "Need ≥2 taps to have an interval.");
        }

        [Test]
        public void TwoTapsGiveActiveAndCorrectOmega()
        {
            var te = new TapEstimator();
            te.Tap(0f);
            te.Tap(0.5f);
            te.Sample(0.5f, out float phase, out float omega, out bool active);
            Assert.IsTrue(active);
            Assert.AreEqual(2f * (float)Math.PI / 0.5f, omega, 1e-4f); // 120 BPM = 4π rad/s
            Assert.AreEqual(0f, phase, 1e-4f); // phase resets to 0 at each tap
        }

        [Test]
        public void PhaseAdvancesBetweenTaps()
        {
            var te = new TapEstimator();
            te.Tap(0f);
            te.Tap(1.0f); // period 1.0s → ω = 2π
            te.Sample(1.25f, out float phase, out float omega, out bool active);
            Assert.IsTrue(active);
            Assert.AreEqual(2f * (float)Math.PI, omega, 1e-4f);
            Assert.AreEqual(0.5f * (float)Math.PI, phase, 1e-3f);
        }

        [Test]
        public void IdleTimeoutDeactivates()
        {
            var te = new TapEstimator();
            te.Tap(0f);
            te.Tap(0.5f);
            te.Sample(0.5f + TapEstimator.IdleTimeout + 0.01f, out _, out _, out bool active);
            Assert.IsFalse(active);
        }

        [Test]
        public void FastTapsClampToMinPeriod()
        {
            var te = new TapEstimator();
            te.Tap(0f);
            te.Tap(0.02f);  // 20 ms interval → should clamp to 0.15 s
            te.Sample(0.02f, out _, out float omega, out bool active);
            Assert.IsTrue(active);
            float expected = 2f * (float)Math.PI / TapEstimator.MinPeriod;
            Assert.AreEqual(expected, omega, 1e-3f);
        }

        [Test]
        public void SlowTapsClampToMaxPeriod()
        {
            var te = new TapEstimator();
            te.Tap(0f);
            te.Tap(5f); // 5 s interval, but also 5s > IdleTimeout so it's a fresh start
            // Need to test the clamp path — use 1.99s interval, then sample just after.
            te.Reset();
            te.Tap(0f);
            te.Tap(1.99f);
            te.Sample(1.99f, out _, out float omega, out _);
            float expected = 2f * (float)Math.PI / 1.99f;
            Assert.AreEqual(expected, omega, 1e-3f);
            // Now with 3s → clamped to MaxPeriod=2s
            te.Reset();
            te.Tap(0f);
            te.Tap(2.5f);
            // But 2.5f > IdleTimeout? IdleTimeout is 3.0 so 2.5 is fine.
            te.Sample(2.5f, out _, out omega, out bool active);
            Assert.IsTrue(active);
            expected = 2f * (float)Math.PI / TapEstimator.MaxPeriod;
            Assert.AreEqual(expected, omega, 1e-3f);
        }

        [Test]
        public void MeanOfLastThreeIntervals()
        {
            var te = new TapEstimator();
            // Intervals: 0.4, 0.6, 1.0 → mean 0.6667
            te.Tap(0.0f);
            te.Tap(0.4f);
            te.Tap(1.0f);
            te.Tap(2.0f);
            te.Sample(2.0f, out _, out float omega, out _);
            float mean = (0.4f + 0.6f + 1.0f) / 3f;
            Assert.AreEqual(2f * (float)Math.PI / mean, omega, 1e-4f);
        }

        [Test]
        public void RingBufferKeepsOnlyLastThree()
        {
            var te = new TapEstimator();
            // Intervals 0.2, 0.2, 0.2, 1.0, 1.0, 1.0 — last 3 should win: mean 1.0
            te.Tap(0f);
            te.Tap(0.2f);
            te.Tap(0.4f);
            te.Tap(0.6f);
            te.Tap(1.6f);
            te.Tap(2.6f);
            te.Tap(3.6f);
            te.Sample(3.6f, out _, out float omega, out _);
            Assert.AreEqual(2f * (float)Math.PI / 1.0f, omega, 1e-4f);
        }

        [Test]
        public void ResetClearsState()
        {
            var te = new TapEstimator();
            te.Tap(0f); te.Tap(0.5f);
            te.Reset();
            te.Sample(0.6f, out _, out _, out bool active);
            Assert.IsFalse(active);
            Assert.AreEqual(0, te.TapCount);
        }
    }
}
