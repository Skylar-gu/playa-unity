# Robot drop-ins for Playa

Playa loads a URDF at boot from `StreamingAssets/robots/<name>/robot.urdf`,
parses it, and hands the joints to the phase-coupled choreographer. Two
robots are supported out of the box (`g1` and `spot`). Any URDF with STL
visual meshes should also work — see "Add your own" at the bottom.

The URDFs are **not committed** to this project — they're pulled from the
manufacturer/community repositories and can be tens to hundreds of MB. Run the
commands below once and Unity will pick them up on next Play.

Set the robot you want in **PlayaBoot → Robot (URDF drop-in) → robotUrdfName**.
Empty or missing URDF → the primitive fallback body renders instead.

---

## 1. Unitree G1 (humanoid, 23–29 DoF) — recommended default

```bash
cd Assets/StreamingAssets/robots
git clone --depth 1 https://github.com/unitreerobotics/unitree_ros.git _unitree
mkdir -p g1
cp -R _unitree/robots/g1_description/. g1/
# Pick one of the URDF variants — G1 ships several DoF configurations.
# The 29-DoF variant is the expressive one; use that unless you have a reason.
ln -sf g1_29dof.urdf g1/robot.urdf   # adjust filename to what's inside g1/
rm -rf _unitree
```

If Unity logs "URDF mesh not found" errors, the path inside the URDF is
`package://g1_description/meshes/...`. The parser strips the package prefix
and looks under the URDF's own directory — so keep the `meshes/` folder
alongside `robot.urdf` (which the copy above already does).

---

## 2. Boston Dynamics Spot (quadruped, 12 DoF)

Community port (BD does not publish an official URDF):

```bash
cd Assets/StreamingAssets/robots
git clone --depth 1 https://github.com/bdaiinstitute/spot_ros2.git _spot
mkdir -p spot
cp -R _spot/spot_description/. spot/
ln -sf spot.urdf spot/robot.urdf     # or whatever the actual URDF file is named
rm -rf _spot
```

Same mesh-path convention — keep `meshes/` next to `robot.urdf`.

---

## Add your own

Point `PlayaBoot.robotUrdfName` at any folder in this directory. Requirements:

- **`robot.urdf`** at the folder root (rename or symlink if the source uses a
  different name).
- **STL meshes only.** OBJ/DAE aren't supported by the runtime loader. Convert
  in Blender: File → Export → Stl (Selection Only), one file per link.
- **`<limit>` tags** on all actuated joints. Feasibility checking depends on
  them — a joint with no limits just gets a "no data" pass from the validator.
- **`<inertial>` tags** with mass + CoM origin. Torque estimation and balance
  checking both need them. Missing mass → zero torque, zero balance meaning.

Xacro files must be expanded to flat URDF first:

```bash
xacro your_robot.urdf.xacro > robot.urdf
```

## What's checked at runtime

Every frame, the FeasibilityAuditor computes:

- **Joint position margin** vs. `<limit lower/upper>`.
- **Joint velocity margin** vs. `<limit velocity>` (from finite-differenced Δq).
- **Torque estimate** vs. `<limit effort>` — heuristic, uses precomputed
  downstream mass + inertia. Within ~2–3× of true torque under moderate motion.
- **Static balance** (legged robots only) — CoM projection vs. convex hull of
  identified foot links, padded by 8 cm.

Results feed the HUD panel (top-right) and get logged as extra columns in the
telemetry CSV: `feas_score, feas_overall, feas_balance, worst_joint,
n_violations, n_warnings`.
