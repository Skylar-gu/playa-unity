# PLAYA — Technical Specification

**Open World Hackathon · Track 2 (VLGE Together) · Unity · 25 Jul 2026**

> Sections marked **[SUBMIT]** map directly onto required fields of the Google Form. Draft them here, paste them there.

---

## 1. Summary

A night-time desert gathering. Twenty strangers are dancing slightly out of time with each other. You walk onto the floor and tap out a rhythm; the dancers near you begin to entrain to *you* rather than to the stage. Hold a local cluster in phase for five seconds and the circle ignites.

The world is a playable instrument for studying **collective entrainment** — how rhythmic coordination propagates through a dense crowd, and what a physical agent would need to observe in order to predict where bodies will be when nobody is going anywhere.

**[SUBMIT] One-sentence pitch.** *Tap out a rhythm and pull a crowd of strangers into sync with you — a playable model of collective entrainment that generates the coordination data robots need in order to move through crowds that aren't walking anywhere.*

**[SUBMIT] Track + engine.** Track 2 (VLGE Together). Unity 2022 LTS, URP. Netcode for GameObjects (P1, see §9).

**Naming.** The world is called *Playa*. It is a generic desert-gathering aesthetic. Do not use the Burning Man name, the Man effigy silhouette, or any Burning Man Project imagery — the Project enforces its marks, and the hackathon rules bar rights-infringing content. There is no design cost to this.

---

## 2. Why this is a Track 2 project and not a screensaver

The track is scored on whether the social interaction is *purposeful* and has a *credible future data-collection use*. The load-bearing design decision is therefore that the crowd is **coupled**, not random. Independently dancing agents emit i.i.d. noise; there is no structure to recover and nothing a model could learn. Coupled agents emit a signal with a measurable order parameter, a controllable phase transition, and a causal handle (the player) that perturbs it.

**The robotics gap this addresses.** Essentially every social-navigation dataset in use records *goal-directed pedestrians walking from A to B*: <cite index="8-1">ETH and UCY capture over 1,500 real pedestrian trajectories in outdoor public spaces from an overhead perspective at 2.5 Hz</cite>; <cite index="10-1">THÖR records curated scenarios of humans visiting and inspecting areas or carrying objects, and SCAND provides socially-compliant navigation demonstrations recorded by teleoperating mobile robots</cite>; <cite index="5-1">SCAND specifically comprises 8.7 hours, 138 trajectories and 25 miles of human-teleoperated driving demonstrations</cite>; <cite index="3-1">JRDB was recorded from a social robot in indoor and outdoor settings</cite>.

A dancefloor is the adversarial complement to all of these. It is dense, **non-goal-directed**, rhythmically structured, and heavily occluded. Constant-velocity and social-force priors — the workhorses of pedestrian prediction — assume an agent is heading somewhere, and degrade badly when the dominant motion component is oscillatory and phase-locked to neighbours. A predictor that knows an agent's *phase* and the *local order parameter* should beat one that knows only position and velocity. **That comparison is the experiment this world is built to make possible.**

Stated as a hypothesis (Free Track discipline applied to a Track 2 build):

> **H1.** In a phase-coupled crowd, adding local phase $\theta$ and local order $R$ to the state reduces short-horizon (0.5–2 s) position prediction error relative to a position/velocity-only baseline, and the gap widens with crowd density.

This is not claimed as a result. It is the thing the data would let you test.

---

## 3. The model

Each dancer $i$ is a phase oscillator. Phase $\theta_i$ drives the visible body motion (vertical bob, lateral sway), so synchrony is *seen*, not just measured.

$$\dot\theta_i \;=\; \underbrace{\omega_i}_{\text{natural}} \;+\; \underbrace{K_b\sin(\theta_{\text{beat}}-\theta_i)}_{\text{stage PA}} \;+\; \underbrace{\frac{K_s}{|N_i|}\sum_{j\in N_i}\sin(\theta_j-\theta_i)}_{\text{nearby dancers}} \;+\; \underbrace{K_p(d_i)\,\sin(\theta_p-\theta_i)}_{\text{you}}$$

with $N_i = \{j : \lVert x_j-x_i\rVert < r_s\}$, $\;\omega_i\sim\mathcal N(\omega_{\text{beat}},\sigma^2)$, and $K_p(d) = K_p^0\max(0,\,1-d/r_p)$.

**Readout.** The Kuramoto order parameter over a set $S$:

$$R_S e^{i\psi_S} \;=\; \frac{1}{|S|}\sum_{j\in S} e^{i\theta_j}, \qquad R_S\in[0,1]$$

$R=0$ is a uniformly-spread crowd, $R=1$ is perfect lockstep. Report both $R_{\text{global}}$ and $R_{\text{local}}$ (within $r_p$ of the player).

### 3.1 Parameter regime — this is the part that makes it a game

Two conditions must hold or the interaction is dead on arrival.

**(a) The crowd must not already be locked to the stage.** A single oscillator forced at strength $K_b$ locks to the drive iff $|\omega_i - \omega_{\text{beat}}| \le K_b$ (Adler). So set $K_b \ll \sigma$ — the PA gives a *tendency*, not a lock. If $K_b$ is too large everyone is already in time and the player has nothing to do.

**(b) The crowd must sit just below the synchronization transition.** For the mean-field Kuramoto model with unimodal symmetric $g(\omega)$, the critical coupling is

$$K_c = \frac{2}{\pi g(0)}, \qquad\text{Gaussian: } K_c = \sigma\sqrt{8/\pi} \approx 1.596\,\sigma.$$

At $\sigma = 0.7$, $K_c \approx 1.12$. Setting $K_s \approx 0.9$ puts the crowd at $\approx 0.8 K_c$: incoherent on its own, but with large susceptibility to perturbation. That susceptibility *is* the game feel. Above $K_c$ the crowd syncs by itself and the player is irrelevant; far below, the player's influence dies at arm's length.

Caveat on rigour: $K_c$ is the **mean-field, all-to-all, $N\to\infty$** result. This crowd is finite ($N\approx80$) and **spatially local** ($r_s\approx4$ m), so the true transition sits somewhere else — finite-size effects smear it, and local coupling generally *raises* the effective threshold and permits spatial domains of partial sync rather than one global order parameter. Treat $1.596\sigma$ as a tuning anchor, not a prediction. Tune empirically: if the crowd syncs before you tap, lower $K_s$.

### 3.2 Player phase

The player's phase is not simulated, it is *measured from input*. Each tap sets $\theta_p \equiv 0$; $\omega_p = 2\pi/\bar{T}$ where $\bar T$ is the mean of the last 3 inter-tap intervals, clamped to $T\in[0.15, 2.0]$ s (40–400 BPM). No tap for 3 s → the player decouples ($K_p=0$).

Desktop: spacebar. VR (if reached): head vertical velocity zero-crossings, which is the same estimator on a different sensor.

---

## 4. Core loop **[SUBMIT: setup / controls / expected outcome]**

| | |
|---|---|
| **Start** | Spawn at the edge of the floor. HUD shows a phase ring and a sync meter reading ~0.2. |
| **Controls** | WASD + mouse to move/look. **Space** = tap the beat. That is the entire control surface. |
| **Objective** | Walk into the crowd, tap a steady rhythm, and hold $R_{\text{local}} \ge 0.80$ with $n_{\text{local}} \ge 8$ for 5 continuous seconds. |
| **Feedback** | Dancers within $r_p$ tint toward your phase colour as they lock. The sync meter fills. Audio: a sub-bass pulse on your tap phase, rising in gain with $R_{\text{local}}$. |
| **End state** | **Ignition** — dust-ring particle burst, floor lights snap to your phase, meter locks, ~4 s outro card showing your $R_{\text{local}}(t)$ trace. |
| **Expected outcome** | A first-time player ignites a circle within 60–90 s without instruction beyond one on-screen line. |

Failure modes are informative and should be left in: tapping too fast (outside the entrainment band, nothing happens), standing at the edge (too few neighbours), and moving while tapping (you keep resetting your neighbour set).

**Multiplayer objective (P1).** Two players in the same world tapping different tempos compete for the crowd; a third state, *mutual entrainment*, is reached when both players' $\omega_p$ converge within 5%. This is the version worth demoing if it runs.

---

## 5. Systems

1. **`DanceFloor.cs`** — single manager, flat arrays, one `Update`. Integrates the phase ODE (forward Euler; valid while $K\,\Delta t \ll 1$, which holds at $K\lesssim3$, 60 fps), does locomotion (1/r separation + weak local cohesion + Perlin wander, velocity-clamped), applies phase → transform (bob $= \tfrac{h}{2}(1-\cos\theta)$ so feet touch ground at $\theta=0$; sway $= A\sin(\theta/2)$, hence phases wrapped to $[0,4\pi)$ so the half-frequency sway stays continuous — $\sin$ and $e^{i\theta}$ are unaffected since $4\pi$ is a multiple of $2\pi$), and accumulates $R$.
2. **`CrowdTelemetry.cs`** — consent-gated fixed-rate CSV writer (§7).
3. **`IgnitionController.cs`** *(to write)* — win condition, dwell timer, VFX trigger, outro card.
4. **World art** — flat ground plane, 6–10 large low-poly structures for silhouette, one central stage. Night + volumetric dust fog + heavy bloom + emissive strips. **Darkness and fog are the art budget**: they hide untextured geometry, kill draw distance cost, and make point lights read as production value.

**Performance.** The neighbour sum is $O(N^2)$: at $N=80$ that is 6,400 sine evaluations per frame, sub-millisecond, and not worth optimizing. Add a uniform-grid spatial hash only above ~500 agents. Do **not** put a MonoBehaviour on each dancer; do **not** run 80 Mecanim humanoid Animators.

**Known risk, flagged as inference not tested fact:** driving a looping animation clip's playhead every frame via `Animator.Play(hash, layer, normalizedTime)` to force phase-lock is a technique I have *not* verified against a live project today, and it can fight Mecanim's own state advance. The spec therefore specifies **procedural** bob/sway driven directly from $\theta$, which is guaranteed to work and reads as synchrony more legibly anyway. Mixamo clips are a P2 visual layer on top, not the mechanism.

---

## 6. Telemetry schema

Fixed 20 Hz. One row per agent per sample.

```
session, t, agent, x, z, vx, vz, theta,
order_local, order_global, n_local,
player_x, player_z, player_phase, beat_phase
```

Volume: $80 \times 20 \times \approx60\,\text{B} \approx 96$ KB/s $\approx 5.8$ MB/min. Buffered `StreamWriter`, flush on quit.

Note the sampling rate is deliberately ~8× the 2.5 Hz of ETH/UCY — oscillatory motion at 2 Hz is aliased into meaninglessness below ~10 Hz, which is itself part of why existing datasets cannot support this analysis.

**Derived quantities available offline:** per-agent instantaneous frequency $\dot\theta_i$; phase-locking value between any agent pair; entrainment latency (time from player tap onset to $R_{\text{local}}$ crossing threshold) as a function of distance and local density; the spatial decay profile of $K_p$'s effect, which is directly measurable and comparable to the assumed linear falloff.

---

## 7. Consent, provenance, PII **[SUBMIT: disclosures]**

Provenance is an explicit tie-breaker in the rubric, and the rules require any data collection to address consent, provenance and identifier removal. Concretely:

- **No file is opened before consent.** `CrowdTelemetry.Consent()` is called only by the start-screen checkbox. Default is off.
- **Session ids are random 12-hex GUID prefixes.** No account name, no device id, no IP, no timestamps tied to wall-clock date.
- **Agent trajectories are synthetic** and carry no personal data. The only human-derived stream is the player's position and tap times.
- **That human stream is not innocuous, and saying so is a strength.** <cite index="11-1,16-1">Nair et al. (USENIX Security 2023) showed that 55,541 real VR users could be uniquely identified across sessions from head and hand motion alone — 94.33% accuracy from 100 seconds of motion and 73.20% from 10 seconds</cite> — <cite index="11-1">work described as the first demonstration that biomechanics can serve as a unique identifier on par with facial or fingerprint recognition</cite>. Their source data was a rhythm game, which is to say: *motion of exactly this kind*. So motion telemetry is treated as pseudo-biometric, retained locally, and a deletion path is offered by session id.
- **Assets:** disclose Unity version, Netcode, any Mixamo/Poly Haven/Kenney assets and their licences, and this specification's AI-assisted drafting.

---

## 8. Parameters

| Symbol | Field | Default | Note |
|---|---|---|---|
| $N$ | `count` | 80 | 60 if frame budget is tight |
| $\omega_{\text{beat}}$ | `beatOmega` | $4\pi$ (120 BPM) | |
| $\sigma$ | `freqSigma` | 0.7 | sets $K_c\approx1.12$ |
| $K_b$ | `beatCoupling` | 0.15 | **must** be $\ll\sigma$ |
| $K_s$ | `peerCoupling` | 0.9 | $\approx0.8K_c$; lower if it self-syncs |
| $r_s$ | `peerRadius` | 4.0 m | |
| $K_p^0$ | `playerCoupling` | 3.0 | must dominate $K_s$ locally |
| $r_p$ | `playerRadius` | 6.0 m | |
| $h$ | `bobHeight` | 0.22 m | |

**Tuning order:** (1) $K_s$ until the crowd is stably incoherent alone; (2) $K_p^0$ until a tap visibly grabs neighbours within ~3 s; (3) $r_p$ until the objective takes 60–90 s.

---

## 9. Build order and cut lines

Working backward from **18:30 PT**.

**P0 — must ship.** Grey-box floor, 80 agents, phase coupling, tap input, HUD meter, ignition, telemetry CSV, single-player.
**P1 — ship if P0 is stable with ≥2 h left.** Netcode host+client, two-player competition/mutual entrainment, art pass (fog, lights, structures).
**P2 — only if bored.** Mixamo clip layer, VR input, audio-reactive stage.

**Explicitly cut:** Gaussian splat capture. The handbook itself puts generation at 2–4 hours and advises timeboxing; there is no version of today in which that is a good trade.

**Hard stop at T-90 min.** Freeze features. Record the 60–90 s backup video, capture three screenshots (crowd incoherent / mid-entrainment / ignition — the three-frame story *is* the pitch), export one $R_{\text{local}}(t)$ plot from the CSV, submit the form. **Ship a working single-player build over a broken multiplayer one**; the checklist wants multiplayer tested, but an honest data-collection roadmap beats a host that won't connect in front of a judge.

---

## 10. Rubric mapping

| Criterion | Pts | Where it is earned |
|---|---|---|
| Experience + usability | 25 | One-key control surface; §4 feedback loop; 60–90 s to first ignition |
| Technical execution | 25 | Single-manager $O(N^2)$ crowd, stable 60 fps, no per-agent MonoBehaviour |
| Track fit + impact | 20 | §2 — coupled (not random) behaviour, with a stated dataset gap |
| Originality | 20 | A phase-transition-as-game-mechanic; crowd sync as spatial data |
| Demo + reproducibility | 10 | CSV + plot shown live, §7 disclosures, §8 parameter table |

**Tie-breakers:** working live build; the core interaction is one key; provenance is §7; the CSV schema is publishable as-is.

**One-sentence "why does this matter?"** — *Robots have plenty of data on people walking somewhere and almost none on people moving together going nowhere, which is most of what humans actually do in a crowd.*

---

## 11. Honest weaknesses (have answers ready)

1. **Synthetic behaviour cannot validate a model of real behaviour.** The crowd's dynamics are assumed, not measured, so any predictor trained here has learned the Kuramoto model, not humans. The defensible claim is narrower: this is an *instrument and a schema*, and the real value is the human player's response to the crowd — plus a controllable testbed where ground-truth coupling is known, which no real dataset offers.
2. **Kuramoto is a caricature of dancing.** Real entrainment has amplitude dynamics, anticipation, visual attention, and asymmetric leader/follower coupling. Phase-only is the simplest model with the right qualitative transition, not a claim about human motor coordination.
3. **$N=80$ is small and the coupling is local**, so mean-field $K_c$ is an anchor rather than a prediction (§3.1).
4. **Consent from a hackathon playtester is thin consent.** It covers a demo, not a dataset. Any real collection needs a proper protocol.
5. **H1 is untested.** No claim is made that phase features improve prediction; that is the proposed experiment.

---

## 12. To verify before demoing

- Netcode phase state sync: transmit $\theta$ or reconstruct client-side from $(\theta_0, \omega)$? Reconstruction is cheaper but drifts under coupling — likely must transmit, at reduced rate with client-side interpolation. **Untested.**
- Whether forward Euler on the phase ODE stays stable at $K_p^0=3$ under frame drops; if not, clamp $\Delta t$ to 1/30 s.
- Dataset citations in §2 were checked today against the primary literature; re-check any figure before quoting it on stage.
