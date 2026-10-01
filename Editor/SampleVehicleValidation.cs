using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteNetLib.Utils;
using LiteNetLibManager;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG
{
    [InitializeOnLoad]
    public static class SampleVehicleValidation
    {
        private sealed class Peer
        {
            public LiteNetLibGameManager manager;
            public VehicleEntity vehicle;
            public SampleVehicleEntityMovement movement;
            public BaseGameEntity driverA, driverB;
        }
        static SampleVehicleValidation() { EditorApplication.delayCall += ProcessRequest; }
        private static void ProcessRequest()
        {
            const string request = "Temp/SampleVehicle.Validate.request";
            if (!File.Exists(request)) return;
            if (File.Exists("Temp/SampleIntegration.BuildDemo.request") || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += ProcessRequest; return; }
            File.Delete(request);
            try { Validate(); File.WriteAllText("Temp/SampleVehicle.Validate.result", "SUCCESS"); }
            catch (Exception ex) { File.WriteAllText("Temp/SampleVehicle.Validate.result", ex.ToString()); Debug.LogException(ex); }
        }
        [MenuItem("Tools/MMORPG KIT/Vehicle Samples/Validate Integration")]
        public static void Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var session = new SampleVehicleControlSession();
            session.UpdateDriver(10, 100);
            uint generation = session.Generation;
            Check(session.Accept(generation, 100, new SampleVehicleInput { throttle = 1, yaw = float.NaN }, 1), "Accept current driver.");
            Check(session.GetInput(1.1f, 0.5f, true).yaw == 0f, "Reject nonfinite control values.");
            Check(session.GetInput(2f, 0.5f, true).throttle == 0f && session.GetInput(2f, 0.5f, true).handbrake, "Timeout cuts engine and brakes.");
            session.UpdateDriver(20, 200);
            Check(!session.Accept(generation, 1000, default, 2), "Reject previous driver's generation.");
            Check(session.Accept(session.Generation, 1, default, 2), "New driver clock is independent.");
            Check(SampleCrashDamageModel.Calculate(15f, 15f, 5f, 2.5f, 1000) == 250, "Crash damage curve.");
            foreach (string name in new[] { "Sample_Car", "Sample_Jet", "Sample_Propeller" })
            {
                ValidateVehicle(name);
                ValidatePhysics(name);
            }
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(SampleVehicleDemoBuilder.Root + "/Demo/GameData/GameDatabase_Sample.asset");
            Check(database != null, "Demo database exists.");
#if !EXCLUDE_PREFAB_REFS || DISABLE_ADDRESSABLES
            Check(database.playerCharacterEntities.Length > 0, "Demo player prefabs exist.");
            foreach (var player in database.playerCharacterEntities)
                Check(player != null && player.CharacterDatabases.Length > 0 && player.CharacterDatabases.All(data => database.playerCharacters.Contains(data)), "Player prefab classes match demo database.");
            Check(database.vehicleEntities.Count(vehicle => vehicle != null && vehicle.GetComponent<SampleVehicleEntityMovement>() != null) == 3, "All three vehicles registered.");
#endif
            SampleVehicleHitDamageValidation.Validate();
            Debug.Log("Vehicle Samples validation passed: car/jet/propeller physics controls, snapshots over UDP, driver handoff, prediction, timeout, brakes, character hit damage and demo class registration.");
        }

        private static void ValidateVehicle(string name)
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                using var network = new SampleValidationTransport();
                string path = SampleVehicleDemoBuilder.Root + "/Demo/Prefabs/" + name + ".prefab";
                var server = CreatePeer(true, -1, scene, path);
                var owner = CreatePeer(false, 10, scene, path);
                var observer = CreatePeer(false, 20, scene, path);
                Seat(server, 10); Seat(owner, 10); Seat(observer, 10);
                server.movement.Body.position = new Vector3(12, 30, 40);
                server.movement.Body.rotation = Quaternion.Euler(25, 40, -35);
                Snapshot(server, owner, 100, network, 1); Snapshot(server, observer, 100, network, 2);
                Invoke(owner.movement, "RefreshSimulation"); Invoke(observer.movement, "RefreshSimulation");
                Check(owner.movement.IsPredicting && !owner.movement.Body.isKinematic, "Owner predicts.");
                Check(!observer.movement.IsPredicting && observer.movement.Body.isKinematic, "Passengers stay kinematic.");
                Check(Quaternion.Angle(observer.movement.Body.rotation, server.movement.Body.rotation) < 0.01f, "Full pitch/roll snapshot.");
                var input = new SampleVehicleInput { throttle = 1, pitch = -0.5f, yaw = 0.25f, steering = 0.5f };
                var writer = new NetDataWriter();
                observer.movement.SetInput(input);
                Check(!observer.movement.WriteClientState(101, writer, out _), "Passenger cannot send input.");
                owner.movement.SetInput(input);
                Check(owner.movement.WriteClientState(101, writer, out _), "Owner sends controls.");
                byte[] old = writer.CopyData();
                server.movement.ReadClientStateAtServer(101, new NetDataReader(network.Transfer(1, 0, old)));
                Check(Session(server).GetInput(Time.unscaledTime, 0.5f, true).throttle == 1, "UDP input reaches server.");
                Invoke(server.movement, "FixedUpdate");
                var physics = server.movement.PhysicsController;
                if (physics.Aircraft != null)
                {
                    Check(physics.Aircraft.Throttle == 1f && physics.Aircraft.EnginePower > 0f, "Aircraft gets absolute engine power.");
                    ValidateAircraftAudio(server.vehicle.gameObject);
                    physics.Aircraft.GetComponent<UnityStandardAssets.Vehicles.Aeroplane.LandingGear>()?.ApplyNetworkState(-1);
                    Snapshot(server, observer, 102, network, 2);
                    observer.movement.PhysicsController.ApplyTelemetry(observer.movement.Telemetry, Vector3.forward * 30f);
                    Check(observer.movement.PhysicsController.Aircraft.Throttle == 1f && observer.movement.PhysicsController.Aircraft.PitchInput == -0.5f, "Aircraft animation/audio telemetry.");
                    var gear = observer.movement.PhysicsController.Aircraft.GetComponent<UnityStandardAssets.Vehicles.Aeroplane.LandingGear>();
                    Check(gear == null || gear.NetworkState == -1, "Late observer receives raised landing gear.");
                    server.movement.StopMove(); Invoke(server.movement, "FixedUpdate");
                    Check(physics.Aircraft.Throttle == 0f && physics.Aircraft.EnginePower == 0f, "StopMove cuts aircraft power immediately on next physics step.");
                }
                else
                {
                    physics.Simulate(SampleVehicleInput.Parked);
                    Check(server.vehicle.GetComponentsInChildren<WheelCollider>().Any(wheel => wheel.brakeTorque > 0f), "Parking brakes applied.");
                    physics.Simulate(default);
                    Check(server.vehicle.GetComponentsInChildren<WheelCollider>().All(wheel => wheel.brakeTorque == 0f), "All brakes release at rest.");
                }
                Seat(server, 20); Seat(owner, 20); Seat(observer, 20);
                Snapshot(server, observer, 103, network, 2);
                server.movement.ReadClientStateAtServer(999, new NetDataReader(old));
                Check(Session(server).GetInput(Time.unscaledTime, 0.5f, true).throttle == 0f, "Late former-driver input rejected.");
                writer.Reset(); observer.movement.SetInput(input);
                Check(observer.movement.WriteClientState(1, writer, out _), "New owner starts independent timestamp.");
                server.movement.ReadClientStateAtServer(1, new NetDataReader(network.Transfer(2, 0, writer.CopyData())));
                Check(Session(server).GetInput(Time.unscaledTime, 0.5f, true).throttle == 1f, "New owner controls vehicle.");
                server.movement.Teleport(new Vector3(100, 200, 300), Quaternion.Euler(-30, 80, 45), false);
                Snapshot(server, observer, 104, network, 2);
                Check(Vector3.Distance(observer.movement.Body.position, server.movement.Body.position) < 0.01f, "Teleport snaps predicted client.");
                Seat(server, -1); Invoke(server.movement, "FixedUpdate");
                if (physics.Aircraft != null) Check(physics.Aircraft.Throttle == 0f, "Empty aircraft engine is off.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void ValidateAircraftAudio(GameObject aircraft)
        {
            var audio = aircraft.GetComponent<UnityStandardAssets.Vehicles.Aeroplane.AeroplaneAudio>();
            Check(audio != null && audio.enabled, "Aircraft audio component is present and enabled.");
            Invoke(audio, "Awake");
            Invoke(audio, "OnEnable");
            var sources = aircraft.GetComponents<AudioSource>();
            Check(sources.Length == 2 && sources.All(source => source.clip != null && source.loop && source.spatialBlend == 1f), "Aircraft has spatial engine and wind loops with valid clips.");
            Check(sources[0].volume > 0f && sources[0].volume <= 0.5f, "Engine power produces audible volume respecting master gain.");
            Invoke(audio, "OnDisable");
            Check(sources.All(source => !source.isPlaying), "Disabling aircraft stops both audio loops.");
            Invoke(audio, "OnEnable");
            Check(sources.All(source => source.isPlaying), "Reactivating a spawned aircraft restarts both loops.");
        }

        private static void ValidatePhysics(string name)
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = new Vector3(0f, -0.5f, 500f);
                ground.transform.localScale = new Vector3(500f, 1f, 2000f);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                    SampleVehicleDemoBuilder.Root + "/Demo/Prefabs/" + name + ".prefab"), scene);
                go.transform.position = new Vector3(0f, 2f, 0f);
                var controller = go.GetComponent<SampleVehiclePhysics>();
                controller.Initialize(); controller.SetSimulation(true);
                foreach (var effects in go.GetComponentsInChildren<UnityStandardAssets.Vehicles.Car.WheelEffects>()) Invoke(effects, "Start");
                var physics = scene.GetPhysicsScene();
                Check(physics != Physics.defaultPhysicsScene, "Physics checks must not simulate the user scene.");
                Physics.SyncTransforms();
                for (int i = 0; i < 150; ++i) { controller.Simulate(SampleVehicleInput.Parked); physics.Simulate(0.02f); }
                Vector3 start = controller.Body.position;
                float maxAltitude = start.y, maxSpeed = 0f;
                for (int i = 0; i < 1000; ++i)
                {
                    float pitch = controller.Aircraft != null && controller.Aircraft.ForwardSpeed > 30f ? Mathf.Clamp((-30f - Mathf.DeltaAngle(0f, controller.Body.rotation.eulerAngles.x)) * 0.05f, -0.5f, 0.5f) : 0f;
                    controller.Simulate(new SampleVehicleInput { throttle = 1f, pitch = pitch });
                    physics.Simulate(0.02f);
                    maxAltitude = Mathf.Max(maxAltitude, controller.Body.position.y);
                    maxSpeed = Mathf.Max(maxSpeed, controller.Body.velocity.magnitude);
                }
                Debug.Log($"{name} physical result: speed {maxSpeed}, altitude {maxAltitude}, start {start}, final {controller.Body.position}, rotation {controller.Body.rotation.eulerAngles}");
                Check(maxSpeed > 10f && Vector3.Distance(start, controller.Body.position) > 30f, name + " accelerates and travels under PhysX.");
                if (controller.Aircraft != null) Check(maxAltitude > start.y + 3f, name + " takes off from runway.");
                Debug.Log($"{name} physics: peak speed {maxSpeed:F1} m/s, peak altitude {maxAltitude:F1} m, distance {Vector3.Distance(start, controller.Body.position):F1} m.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static Peer CreatePeer(bool server, long clientId, Scene scene, string prefabPath)
        {
            var peer = new Peer();
            peer.manager = new GameObject("Sample validation peer").AddComponent<LiteNetLibGameManager>();
            Property(peer.manager, "IsServer", server);
            Property(peer.manager, "IsClient", !server);
            Property(peer.manager, "ClientConnectionId", clientId);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath ?? SampleVehicleDemoBuilder.Root + "/Demo/Prefabs/Sample_Car.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            peer.vehicle = go.GetComponent<VehicleEntity>();
            peer.vehicle.CurrentHp = 1;
            peer.movement = go.GetComponent<SampleVehicleEntityMovement>();
            Identity(peer.vehicle.Identity, peer.manager, 50, -1);
            Invoke(peer.movement, "Awake");
            Field(peer.movement, "_initialized").SetValue(peer.movement, true);
            foreach (var effects in go.GetComponentsInChildren<UnityStandardAssets.Vehicles.Car.WheelEffects>()) Invoke(effects, "Start");
            peer.driverA = new GameObject("driver A").AddComponent<VehicleEntity>();
            peer.driverB = new GameObject("driver B").AddComponent<VehicleEntity>();
            Identity(peer.driverA.Identity, peer.manager, 100, 10);
            Identity(peer.driverB.Identity, peer.manager, 200, 20);
            return peer;
        }

        private static void Seat(Peer peer, long owner)
        {
            var passengers = (Dictionary<byte, BaseGameEntity>)Field(peer.vehicle, "_passengers").GetValue(peer.vehicle);
            passengers.Clear();
            if (owner >= 0)
            {
                passengers[0] = owner == 10 ? peer.driverA : peer.driverB;
                passengers[1] = owner == 10 ? peer.driverB : peer.driverA;
            }
            Property(peer.vehicle.Identity, "ConnectionId", owner);
            peer.movement.OnSetOwnerClient(owner >= 0 && peer.manager.ClientConnectionId == owner);
        }

        private static SampleVehicleControlSession Session(Peer peer) => (SampleVehicleControlSession)Field(peer.movement, "_controls").GetValue(peer.movement);
        private static void Snapshot(Peer server, Peer client, long timestamp, SampleValidationTransport network, int clientIndex)
        {
            var writer = new NetDataWriter();
            Check(server.movement.WriteServerState(timestamp, writer, out _), "Server snapshot missing.");
            var reader = new NetDataReader(network.Transfer(0, clientIndex, writer.CopyData()));
            client.movement.ReadServerStateAtClient(timestamp, reader);
            Check(reader.AvailableBytes == 0, "Snapshot must consume its complete packet.");
        }
        private static void Identity(LiteNetLibIdentity identity, LiteNetLibGameManager manager, uint id, long owner)
        {
            Property(identity, "Manager", manager); Property(identity, "ObjectId", id); Property(identity, "ConnectionId", owner);
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Sample multiplayer validation: " + message); }
        private static FieldInfo Field(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }
        private static void Property(object target, string name, object value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property == null) continue;
                property.SetValue(target, value);
                return;
            }
            throw new MissingMemberException(target.GetType().Name, name);
        }
        private static void Invoke(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (method == null) continue;
                method.Invoke(target, null);
                return;
            }
            throw new MissingMethodException(target.GetType().Name, name);
        }
    }
}
