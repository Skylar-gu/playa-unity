 Not a physics simulation — an analytical check running alongside the kinematic motion. Four independent validators derive feasibility from URDF-declared limits + observed
  motion, run every frame in FeasibilityAuditor.LateUpdate.
                                                                                                                                                                                  
  The four checks
                                                                                                                                                                                  
  1. Position limit — JointLimitValidator.cs:38                                                                                                                                   
          
  The simplest one. Every URDF actuated joint declares <limit lower= upper=/>. Each frame:                                                                                        
  margin = 1 - |q - midpoint| / half_range                                                                                                                                      
  - margin > 0.20 → OK                                                                                                                                                            
  - margin ∈ [0, 0.20] → Warn (yellow tint)                                                                                                                                       
  - margin < 0 → Violation (red tint, means we drove past the limit)
                                                                                                                                                                                  
  The choreographer also clamps to these limits before applying, so violations here are rare — mostly they show up if a URDF has extremely tight ranges.                          
                                                                                                                                                                                  
  2. Velocity limit — JointLimitValidator.cs:60                                                                                                                                   
                                                                                                                                                                                  
  URDF <limit velocity=/> is the max joint speed the actuator can command.                                                                                                      
  |q̇| ← finite difference: (q_current - q_previous) / dt
  margin = 1 - |q̇| / limit.velocity
  Same three-band scoring. This is what catches "your dance moves too fast for the servo" — often the first thing that goes red when a robot tries to dance to a fast BPM.
                                                                                                                                                                          
  3. Torque estimate — TorqueEstimator.cs:53
                                                                                                                                                                                  
  The most involved one. It's a heuristic, not full recursive Newton-Euler dynamics. Two components:                                                                              
                                                                                                                                                                                  
  Dynamic term — Newton's second law for rotation, τ = I·α:                                                                                                                       
  - At load time (InertiaPrecomputer.cs), for each joint we walk the kinematic subtree, sum masses and mass-weighted CoM positions from every downstream link's <inertial> block.
  - We compute I_effective = m_downstream · r_perp² — the effective inertia of the downstream chain about the joint axis, treated as a point mass at the CoM. (Real inertia is a  
  3×3 tensor; this is the axis-projected scalar.)                                                                                                                               
  - Angular acceleration is finite-differenced: α = (q̇_current - q̇_previous) / dt.                                                                                                
  - τ_dyn = I_effective · |α|                                                                                                                                                   
                                                                                                                                                                                  
  Gravity term — static hold torque:                                                                                                                                              
  - Project the world-down vector onto the plane perpendicular to the joint's world-space axis. The magnitude of that projection scales the gravity contribution — a joint axis
  parallel to gravity feels no gravity torque, perpendicular feels all of it.                                                                                                     
  - τ_grav = m_downstream · g · r_arm · ‖gravity_perp‖                                                                                                                          
                                                                                                                                                                                  
  Combined: τ_est = τ_dyn + τ_grav, compared to URDF <limit effort=/>.                                                                                                            
                                                                                                                                                                                  
  Why it's approximate (called out in the file comment):                                                                                                                          
  - Ignores Coriolis/centrifugal                                                                                                                                                  
  - Ignores reaction torques from downstream joints moving                                                                                                                      
  - Uses scalar inertia projection, not the full 3×3      
  - Ignores rotor inertia and gear ratios                                                                                                                                         
  - Uses rest-pose moment arm, not current-pose
                                                                                                                                                                                  
  In practice it's within ~2–3× of true torque under moderate motion — enough to rank joints ("this one's working hardest") and catch obvious over-actuation, not enough to     
  certify a real controller.                                                                                                                                                      
                                                                                                                                                                                
  4. Static balance — BalanceValidator.cs                                                                                                                                         
                                                                                                                                                                                
  Only runs on legged robots (≥ 2 identified foot links). Every frame:                                                                                                            
                                             
  1. CoM — Σ (link_mass · link_com_world) / Σ link_mass. Uses the same <inertial> mass and origin from URDF.                                                                      
  2. Support polygon — take all foot-link world positions, project to XZ plane, compute their convex hull (Andrew's monotone chain), pad outward by 8 cm to approximate foot    
  width.                                                                                                                                                                          
  3. Test — is the CoM's XZ projection inside the padded polygon?                                                                                                               
    - Inside by > 20% of pad width → OK                                                                                                                                           
    - Inside marginally → Warn                                                                                                                                                    
    - Outside → Violation (robot would tip over)                                                                                                                                  
                                                                                                                                                                                  
  Static balance only — it ignores momentum (a robot mid-lunge is dynamically stable in ways this misses) and it ignores contact forces (assumes both feet flat on ground). It    
  catches "your pose has CoM outside the feet" which is the failure mode a hackathon judge would notice.
                                                                                                                                                                                  
  Where the results go — FeasibilityAuditor.cs:29                                                                                                                                 
                                                                            
  Each frame, every validator writes into a shared FeasibilityReport (FeasibilityTypes.cs):                                                                                       
  - Per-joint Position / Velocity / Torque status + margin                                                                                                                      
  - Global BalanceStatus + margin                                                                                                                                                 
  - Aggregate FeasibilityScore ∈ [0, 1] — averaged normalized margins minus penalty for violations/warns                                                                        
                                                                                                                                                                                  
  Three downstream consumers read the report:                                                                                                                                     
  - JointTintApplier — green/yellow/red MPB tint per joint per frame (the visible "which joint is straining" signal)                                                              
  - HUD — top-right panel: score bar, balance status, tightest joint, counts                                                                                                      
  - CrowdTelemetry — CSV columns feas_score, feas_overall, feas_balance, worst_joint, n_violations, n_warnings at 20 Hz alongside the crowd data                                
                                                                                                                                                                                  
  What this catches vs. misses                                                                                                                                                    
                                                                                                                                                                                  
  Catches — driving joints too fast, driving past limits, torques that clearly exceed actuator spec, poses with CoM outside feet, ranking which joints are hardest-worked.        
                                                                                                                                                                                
  Misses — actual dynamic behavior (would the robot fall in a real physics sim?), contact-rich stuff (foot slip, ground friction), controller stability (would a PD controller    
  track this?), thermal/duty-cycle limits.                                                                                                                                      
                                                                                                                                                                                  
  The IJointCommand interface exists specifically so you can later swap KinematicJointDriver → an ArticulationJointDriver and get real PhysX dynamics on top of the same          
  choreography — that upgrade path was the reason for splitting driver from choreographer.
