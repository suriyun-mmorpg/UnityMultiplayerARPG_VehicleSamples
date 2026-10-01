# Vehicle Samples integration

Networked car, jet and propeller aircraft for MMORPG KIT. The integration is independent of UVC.

## Try the demo

Open `Demo/Scenes/00Init_VehicleSamples.unity`, enter Play Mode, create a **new** demo character, then start Single Player or Host. For a second player, use a build launched from the same init scene and connect to the host. The builder appends the init and airfield scenes to Build Settings; select the demo init as the startup scene for a standalone demo build.

The airfield contains three scene vehicles, a 1,200 m runway, a car slalom and an impact wall. Each vehicle has a driver and passenger seat. Riders are hidden inside the models; cockpit/pilot animations are not included. Demo male/female character-controller and NavMesh player prefabs have the four demo classes assigned and registered in `GameDatabase_Sample`.

Use the kit's **Activate**, **ExitVehicle** and **CameraRotate** bindings for boarding, leaving and looking around.

| Vehicle | Controls |
| --- | --- |
| Car | A/D steering; W gas; S brake/reverse; Space handbrake |
| Aircraft | A/D roll; W nose down; S nose up; Q/E yaw |
| Aircraft engine | Hold Left Shift to increase throttle, Left Ctrl to decrease; Space applies airbrakes and wheel brakes |

Aircraft throttle holds its selected position while flying. Hold Shift to reach full throttle, accelerate along the runway, then hold S to rotate and climb. Avoid excessive pitch and reduce power for landing. Focus loss, UI input blocking, leaving the seat and input timeout cut the engine and apply brakes. An airborne unoccupied aircraft continues falling/gliding under server physics; it is not frozen in midair.

## Runtime components

- `SampleVehicleEntityMovement`: kit movement-channel adapter, server physics, optional driver prediction and bounded reconciliation, full body rotation/velocity, car wheel poses, aircraft telemetry, teleport revisions and per-driver input generations.
- `SampleVehiclePhysics`: routes sanitized controls to `CarController` or `AeroplaneController`, disables competing sample player/AI/self-righting components, controls Rigidbody/WheelCollider simulation, and feeds replicated audio/animation state.
- `SampleVehiclePlayerController` and `SampleAircraftPlayerController`: seat controllers selected by VehicleType. Passengers retain the kit camera/exit controls but cannot send driving input. Serialized bindings support alternate input axes; public Set* methods support mobile UI.
- `SampleVehicleCrashDamage`: server-only environmental collision HP damage. Demo vehicles have 1,000 HP and use the kit destruction/ejection/scene-respawn lifecycle. Normal impact speed and impulse filter glancing/light collisions; compound contacts are coalesced and rate limited.
- `SampleVehicleHitDamage`: server character impacts from collision and swept chassis queries, attributed to the driver and routed through normal combat permissions/armor/death. Occupants, invincible targets, safe areas and disallowed PvP are excluded. Unoccupied vehicles do not damage characters. The demo cap is 250 raw damage with a 0.75 s per-target cooldown.

Crash damage affects kit HP. These samples do not implement UVC's separate engine/wheel condition system, mesh deformation or nitrous.

## Create another vehicle

Start from one of `Demo/Prefabs/Sample_*.prefab`, or add Rigidbody, exactly one sample physics controller, `SampleVehiclePhysics`, `SampleVehicleEntityMovement`, LiteNetLibIdentity and VehicleEntity to the vehicle root. Configure seats, a root interaction trigger, a VehicleType and optional damage components. Register the prefab in your GameDatabase, then map that VehicleType and each seat to controllers on your player-controller prefab. Do not add a second transform synchronizer. The kit owns connection and driver validation; clients transmit controls, never authoritative poses or damage.

Car wheel meshes and WheelColliders must match the original four-wheel ordering. Additional moving parts can be assigned to the movement adapter's additional-visuals list, parent before child. Aircraft control surfaces, propellers, engine audio and landing gear use replicated telemetry; late observers receive the current gear state.

## Sample-script changes

Car initialization is idempotent, brake and motor torque clear every physics step, parking torque remains finite, remote telemetry feeds audio, and audio tolerates a dedicated server without a camera. Wheel effects tolerate missing optional particles and keep generated trails in the vehicle's scene.

Aircraft expose absolute-throttle control, explicit reset and replicated telemetry; wheel brakes release on input release. Native rate-based `Move` remains available for standalone samples. Physics code uses the fixed timestep. Legacy sample user-input scripts use the kit input service because the imported asset set does not include CrossPlatformInput.

Demo effect materials and skid-trail prefab have URP variants in Demo. Original source materials are preserved. No UVC references or pipeline-settings changes are required.

## Editor tools and verification

Under **Tools > MMORPG KIT > Vehicle Samples**:

- **Create Demo Assets** generates the demo through Unity APIs and refuses to overwrite existing authored demo scenes.
- **Create Player Entity Prefabs** rebuilds the class assignments from the demo database.
- **Prepare Demo Effects** assigns demo-owned URP effect variants when URP is active.
- **Validate Integration** checks packet serialization over localhost UDP, owner prediction/passenger exclusion, driver handoff, stale input, timeout and teleport behavior; car brake release and aircraft power reset; isolated PhysX driving/takeoff for all three vehicles; character-hit combat permissions/queries; and demo registration/class assignments.

Validation uses isolated edit-mode replicas and preview physics scenes. It does not replace a live two-player gameplay test under real latency. Run one before shipping, particularly to tune aircraft prediction and camera distance for your game.
