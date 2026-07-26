# playa-robotics

A night-time desert gathering. Twenty strangers dance out of time; you walk on and tap a rhythm; a circle ignites. A playable model of **collective entrainment** — the data robots need for crowds that aren't going anywhere.

- Full technical spec: [`PLAYA_SPEC.md`](./PLAYA_SPEC.md)
- Build plan and test taxonomy: [`BUILD_PLAN.md`](./BUILD_PLAN.md)
- Unity project: [`playa/`](./playa)

## Quickstart

1. Install **Unity 2022.3 LTS** (`2022.3.42f1` is pinned in `ProjectVersion.txt` — the newest standard-LTS Personal-eligible build. Post-`.62f1` moved to Extended LTS which requires a paid Industry/Enterprise seat.)
2. Open `playa/` via Unity Hub → *Add project from disk*.
3. First open: let it install URP, Netcode, and Test Framework from `Packages/manifest.json`.
4. `File → New Scene → Basic (URP)`, save to `Assets/Scenes/Playa.unity`.
5. Create an empty GameObject, add the **`PlayaBoot`** component, delete the default camera + directional light in the scene (`PlayaBoot` creates its own).
6. Press ▶.
7. On the start screen, optionally tick *record telemetry locally* (§7 — this is the only path that opens a file). Click *walk onto the playa*.
8. WASD to walk. Space to tap. Hold `R_local ≥ 0.80` with `n_local ≥ 8` for 5 s → ignition.

If you have "Active Input Handling" set to "New Input System (package)" only, flip it to *Both* in `Project Settings → Player → Other Settings` — `PlayerRig` uses the legacy `Input` API for the one-key control surface.

If the world looks flat/unbloomed, verify: (a) your URP asset (`Assets/Settings/*.asset`) has *HDR* and *Post Processing* enabled, and (b) the URP asset is assigned in `Project Settings → Graphics → Scriptable Render Pipeline Settings`. `PlayaBoot` flips *Post Processing* on the camera itself via code; it can't override the render pipeline asset.

## Running the tests

`Window → General → Test Runner`

- **EditMode**: `KuramotoMathTests`, `TapEstimatorTests` — pure math/logic, fast.
- **EditMode · Category "Interventional"**: `InterventionalTests` — multi-seed causal claims (see `BUILD_PLAN.md`). Slower; a few seconds.
- **PlayMode**: `PlayaBootPlayModeTests` — smoke test that the scene bootstraps.

## Reading a telemetry CSV

Schema per §6:

```
session, t, agent, x, z, vx, vz, theta,
order_local, order_global, n_local,
player_x, player_z, player_phase, beat_phase
```

File lives at `<Application.persistentDataPath>/telemetry/playa_<sid>.csv` — on macOS that's `~/Library/Application Support/DefaultCompany/playa/telemetry/`.

Minimal R_local(t) plot for the outro card:

```python
import pandas as pd, matplotlib.pyplot as plt
df = pd.read_csv("playa_<sid>.csv")
g = df.groupby("t").first()  # order_local is per-frame, not per-agent
plt.plot(g.index, g["order_local"])
plt.axhline(0.8, ls="--"); plt.xlabel("t (s)"); plt.ylabel("R_local")
plt.show()
```

## Why the world isn't called Burning Man

Because the Project actively enforces its trademarks and the hackathon rules bar rights-infringing content. The design cost of switching to a generic desert-gathering aesthetic — dust, night, ember lights, angular silhouettes — is zero. See spec §1.
