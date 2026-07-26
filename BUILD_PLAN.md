# PLAYA — Build Plan

Working backward from **18:30 PT · 2026-07-25**. Priorities mirror §9 of `PLAYA_SPEC.md`. Every ship gate is defined in terms of a test that has to pass.

Naming note: the world stays `Playa`, generic desert-gathering aesthetic. No Burning Man name, no Man silhouette, no BMP marks (spec §1).

---

## Repo layout

```
playa/                          Unity 2022.3 LTS project root
├── Assets/
│   ├── Scripts/                Runtime C# (Playa.Runtime asmdef)
│   │   ├── KuramotoMath.cs         pure math, no Unity refs
│   │   ├── TapEstimator.cs         pure logic, no Unity refs
│   │   ├── CrowdSimulator.cs       headless sim used by tests + DanceFloor
│   │   ├── DanceFloor.cs           MonoBehaviour crowd manager
│   │   ├── CrowdTelemetry.cs       consent-gated 20 Hz CSV
│   │   ├── IgnitionController.cs   win condition + outro trace
│   │   ├── PlayerRig.cs            WASD + mouse + Space tap
│   │   ├── HUD.cs                  IMGUI ring / meter / hint
│   │   ├── StartScreen.cs          consent modal
│   │   └── PlayaBoot.cs            procedural scene generator
│   └── Tests/
│       ├── EditMode/           Playa.Tests.EditMode asmdef
│       │   ├── KuramotoMathTests.cs
│       │   ├── TapEstimatorTests.cs
│       │   └── InterventionalTests.cs   ← the load-bearing set
│       └── PlayMode/           Playa.Tests.PlayMode asmdef
│           └── PlayaBootPlayModeTests.cs
├── Packages/manifest.json      URP + Netcode + TestFramework + InputSystem
└── ProjectSettings/ProjectVersion.txt   pinned to 2022.3.42f1
```

There is **no hand-authored `.unity` binary**. The world is built at runtime by `PlayaBoot`. Everything is version-controlled C#, which means the "scene" diffs cleanly and reviewers don't have to open Unity to read what it contains.

---

## P0 — must ship (single-player, coupled crowd, ignition)

Ship gate: **`Test Runner → EditMode → All`** is green AND `PlayaBootPlayModeTests` is green AND one manual playthrough reaches ignition inside 90 s.

- [x] Unity project scaffold (asmdefs, manifest, `ProjectVersion.txt`, `.gitignore`)
- [x] `KuramotoMath` — Euler step, order parameter, wrap-to-4π, K_p falloff
- [x] `TapEstimator` — 3-interval mean, [0.15,2.0] s clamp, 3 s idle timeout
- [x] `CrowdSimulator` — SoA arrays, seeded init, locomotion (separation + cohesion + wander)
- [x] `DanceFloor` MonoBehaviour — one Update, per-agent capsule with MPB tinting
- [x] `CrowdTelemetry` — off by default; 20 Hz; StreamWriter; random 12-hex session id
- [x] `IgnitionController` — dwell timer, dust burst, stage-light snap, R_local(t) trace
- [x] `PlayerRig` + `HUD` + `StartScreen`
- [x] `PlayaBoot` — procedurally spawns ground, stage, silhouettes, string lights, moon, fog
- [x] EditMode unit tests (KuramotoMath, TapEstimator)
- [x] Interventional multi-seed tests
- [x] PlayMode smoke test (scene bootstraps, crowd advances)

## P1 — ship if P0 stable with ≥2 h left

- [ ] Netcode host+client — sync θ per agent at reduced rate with client-side interp (see §12)
- [ ] Two-player mutual entrainment — third state when ω_p converges within 5%
- [ ] Post-processing volume (bloom + film grain) toggleable via `PlayaBoot`
- [ ] Audio: sub-bass pulse on player phase, gain rising with R_local
- [ ] Mixamo idle → dance blend as a P2 visual layer on top of procedural bob

## P2 — only if bored

- [ ] VR input path (head vertical velocity zero-crossings → same TapEstimator input)
- [ ] Audio-reactive stage (FFT-driven emissive on stage stripe)
- [ ] Uniform-grid spatial hash for peer sum — only worth it above ~500 agents

## Explicitly cut

- Gaussian-splat capture (§9 — 2–4 h, no version of today makes it a good trade)
- Hand-authored .unity scene binary (procedural boot obviates it)
- Per-agent MonoBehaviour or per-agent Animator (§5 — explicitly warned against)

---

## Hard stop at T-90 min

1. Freeze features. No merges.
2. Record 60–90 s backup video (OBS at 1080p60).
3. Capture three screenshots: incoherent / mid-entrainment / ignition. That triptych IS the pitch.
4. Export one `R_local(t)` plot from `~/Library/Application Support/.../telemetry/playa_<sid>.csv` (Python/matplotlib one-liner in `README.md`).
5. Ship the single-player build if multiplayer host isn't connecting cleanly. §9 is explicit on this.

---

## Test taxonomy

The test suite makes two orthogonal claims:

**Correctness** — `KuramotoMathTests`, `TapEstimatorTests`. Point-identity properties: R=1 for identical phases, R=0 for uniformly spaced, wrap invariance, subset honoring, BPM clamp, idle-timeout deactivation. If any of these break the model is not the one in §3.

**Behavior under intervention** (`InterventionalTests`, `[Category("Interventional")]`) — run the full pure-C# `CrowdSimulator` across many seeds and assert on aggregate statistics, not on any single seed. This is the code-level analogue of H1 (§2): a causal handle should produce a measurable effect above baseline, robustly across noise realizations.

| Test | Claim | Assertion |
|---|---|---|
| `Baseline_MedianRGlobalStaysLow` | Crowd sits below the synchronization transition | Median R_global over final 5 s < 0.75 across seeds |
| `Baseline_StageOnlyDoesNotLockCrowd` | K_b ≪ σ → stage bias, not lock | Median R_global < 0.85 with player off |
| `PlayerAtCentre_RLocalRisesAboveBaseline` | Intervention lifts R_local | with-player − without-player > 0.20 |
| `PlayerAtCentre_IgnitionThresholdMetInTime` | §4 objective is reachable | Median time to R_local≥0.8 ∧ n_local≥8 ≤ 8 s |
| `DecouplingAfterIdleAllowsRLocalToDecay` | 3 s idle releases the crowd | R_locked − R_after_idle > 0.10 across seeds |
| `SameSeedProducesIdenticalTrajectories` | Determinism | Bit-identical θ / (x,z) after 5 s |
| `DifferentSeedsProduceDifferentTrajectories` | Noise realizations differ | |Δθ| summed > 1 |
| `HigherLocalDensityAmplifiesIntervention` | H1-adjacent monotonicity | R_local(N=100) > R_local(N=30) |

Runtime knobs at the top of `InterventionalTests.cs` (`Count`, `Seeds`, `Dt`) — dial down if iteration feels slow.
