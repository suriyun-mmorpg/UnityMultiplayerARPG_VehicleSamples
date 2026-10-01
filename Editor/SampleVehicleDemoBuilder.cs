using System;
using System.IO;
using System.Linq;
using LiteNetLibManager;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MultiplayerARPG
{
    /// <summary>Creates project-specific demo assets without changing the Sample package or kit demos.</summary>
    [InitializeOnLoad]
    public static class SampleVehicleDemoBuilder
    {
        public const string Root = "Assets/VehicleSamples";
        private const string CarSource = "Assets/VehicleSamples/Car/Prefabs/Car.prefab";
        private const string InitSource = "Assets/UnityMultiplayerARPG/Demo/Scenes/00Init.unity";
        private const string DatabaseSource = "Assets/UnityMultiplayerARPG/Demo/GameData/GameDatabase.asset";
        private const string ControllerSource = "Assets/UnityMultiplayerARPG/Demo/Prefabs/Gameplay/PlayerCharacterController.prefab";
        private const string Request = "Temp/SampleIntegration.BuildDemo.request";

        static SampleVehicleDemoBuilder()
        {
            // One-shot local build requests are useful when running Unity versions without Pipeline.
            EditorApplication.delayCall += ProcessRequest;
        }

        private static void ProcessRequest()
        {
            if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ProcessRequest;
                return;
            }
            File.Delete(Request);
            try
            {
                BuildDemo();
                File.WriteAllText("Temp/SampleIntegration.BuildDemo.result", "SUCCESS");
            }
            catch (Exception ex)
            {
                File.WriteAllText("Temp/SampleIntegration.BuildDemo.result", ex.ToString());
                Debug.LogException(ex);
            }
        }

        [MenuItem("Tools/MMORPG KIT/Vehicle Samples/Create Demo Assets")]
        public static void BuildDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before creating demo assets.");
            RequireAsset<GameObject>(CarSource);
            RequireAsset<GameObject>(ControllerSource);
            RequireAsset<GameDatabase>(DatabaseSource);
            EnsureFolder(Root + "/Demo/Scenes");
            EnsureFolder(Root + "/Demo/Prefabs");
            EnsureFolder(Root + "/Demo/GameData");

            string initPath = Root + "/Demo/Scenes/00Init_VehicleSamples.unity";
            string mapPath = Root + "/Demo/Scenes/VehicleSamplesAirfield.unity";
            // Never overwrite authored scenes or assets on subsequent imports.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(initPath) != null || AssetDatabase.LoadAssetAtPath<SceneAsset>(mapPath) != null)
                throw new InvalidOperationException("Sample demo scenes already exist. Existing authored demo assets are preserved.");

            var type = CreateAsset<VehicleType>("VehicleType_SampleCar");
            type.Id = "sample_car";
            var aircraftType = CreateAsset<VehicleType>("VehicleType_SampleAircraft");
            aircraftType.Id = "sample_aircraft";
            var map = CreateAsset<MapInfo>("MapInfo_VehicleSamplesAirfield");
            map.Id = "sample_driving_demo";
            map.respawnPointsByCondition = new WarpPointByCondition[0];
            SetVector(map, "startPosition", new Vector3(-19f, 1f, 0f));

            Scene previous = SceneManager.GetActiveScene();
            Scene stage = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(stage);
                var vehiclePrefabs = new[]
                {
                    CreateVehicle(CarSource, "Sample_Car", type, stage, false),
                    CreateVehicle(Root + "/Aircraft/Prefabs/AircraftJet.prefab", "Sample_Jet", aircraftType, stage, true),
                    CreateVehicle(Root + "/Aircraft/Prefabs/AircraftPropeller.prefab", "Sample_Propeller", aircraftType, stage, true),
                };

                GameObject controls = (GameObject)PrefabUtility.InstantiatePrefab(RequireAsset<GameObject>(ControllerSource), stage);
                controls.name = "PlayerCharacterController_Sample";
                var driverController = controls.AddComponent<SampleVehiclePlayerController>();
                var passengerController = controls.AddComponent<SampleVehiclePlayerController>();
                var controller = controls.GetComponent<BasePlayerCharacterController>();
                var serialized = new SerializedObject(controller);
                var mappings = serialized.FindProperty("vehicleControllers");
                mappings.arraySize = 2;
                var entry = mappings.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("vehicleType").objectReferenceValue = type;
                var seats = entry.FindPropertyRelative("controllersForEachSeats");
                seats.arraySize = 2;
                seats.GetArrayElementAtIndex(0).objectReferenceValue = driverController;
                seats.GetArrayElementAtIndex(1).objectReferenceValue = passengerController;
                var aircraftController = controls.AddComponent<SampleAircraftPlayerController>();
                var aircraftPassenger = controls.AddComponent<SampleAircraftPlayerController>();
                var aircraftEntry = mappings.GetArrayElementAtIndex(1);
                aircraftEntry.FindPropertyRelative("vehicleType").objectReferenceValue = aircraftType;
                var aircraftSeats = aircraftEntry.FindPropertyRelative("controllersForEachSeats");
                aircraftSeats.arraySize = 2;
                aircraftSeats.GetArrayElementAtIndex(0).objectReferenceValue = aircraftController;
                aircraftSeats.GetArrayElementAtIndex(1).objectReferenceValue = aircraftPassenger;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var controllerPrefab = PrefabUtility.SaveAsPrefabAsset(controls, Root + "/Demo/Prefabs/PlayerCharacterController_Sample.prefab");
                Object.DestroyImmediate(controls);

                // Two scene vehicles allow testing ownership handover and passenger seating.
                for (int i = 0; i < vehiclePrefabs.Length; ++i)
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(vehiclePrefabs[i], stage);
                    instance.name = vehiclePrefabs[i].name;
                    instance.transform.position = new Vector3(i == 0 ? -15f : i == 1 ? 0f : 25f, 2f, i == 0 ? 0f : 25f);
                    var sceneIdentity = new SerializedObject(instance.GetComponent<LiteNetLibIdentity>());
                    sceneIdentity.FindProperty("sceneObjectId").stringValue = "sample_demo_car_" + i;
                    sceneIdentity.ApplyModifiedPropertiesWithoutUndo();
                }
                CreateDrivingArea();
                new GameObject("Demo instructions").AddComponent<SampleVehicleDemoInstructions>();
                EditorSceneManager.SaveScene(stage, mapPath);
                var mapSerialized = new SerializedObject(map);
                var scene = mapSerialized.FindProperty("scene");
                scene.FindPropertyRelative("sceneAsset").objectReferenceValue = RequireAsset<SceneAsset>(mapPath);
                scene.FindPropertyRelative("sceneName").stringValue = "VehicleSamplesAirfield";
                mapSerialized.ApplyModifiedPropertiesWithoutUndo();

                var database = Object.Instantiate(RequireAsset<GameDatabase>(DatabaseSource));
                database.name = "GameDatabase_Sample";
#if !EXCLUDE_PREFAB_REFS || DISABLE_ADDRESSABLES
                database.vehicleEntities = (database.vehicleEntities ?? new VehicleEntity[0]).Concat(vehiclePrefabs.Select(prefab => prefab.GetComponent<VehicleEntity>())).ToArray();
#endif
                database.mapInfos = (database.mapInfos ?? new BaseMapInfo[0]).Concat(new[] { map }).ToArray();
                database.playerCharacters = (database.playerCharacters ?? new PlayerCharacter[0]).Select(source =>
                {
                    var clone = Object.Instantiate(source);
                    clone.name = source.name + "_Sample";
                    clone.Id = source.Id + "_sample";
                    SetObject(clone, "startMap", map);
                    SetBool(clone, "useOverrideStartPosition", false);
                    SetBool(clone, "useOverrideStartRotation", false);
                    var data = new SerializedObject(clone);
                    data.FindProperty("startPointsByCondition").arraySize = 0;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.CreateAsset(clone, Root + "/Demo/GameData/" + clone.name + ".asset");
                    return clone;
                }).ToArray();
                CreatePlayerEntityPrefabs(database);
                AssetDatabase.CreateAsset(database, Root + "/Demo/GameData/GameDatabase_Sample.asset");
                AssetDatabase.CopyAsset(InitSource, initPath);
                Scene init = EditorSceneManager.OpenScene(initPath, OpenSceneMode.Additive);
                try
                {
                    GameInstance game = init.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<GameInstance>(true)).Single();
                    SetObject(game, "gameDatabase", database);
                    SetObject(game, "defaultControllerPrefab", controllerPrefab.GetComponent<BasePlayerCharacterController>());
                    var gameSerialized = new SerializedObject(game);
                    var addressable = gameSerialized.FindProperty("addressableDefaultControllerPrefab");
                    if (addressable != null)
                        addressable.FindPropertyRelative("m_AssetGUID").stringValue = string.Empty;
                    gameSerialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorSceneManager.SaveScene(init);
                }
                finally { EditorSceneManager.CloseScene(init, true); }
                EditorUtility.SetDirty(map);
                EditorUtility.SetDirty(type);
                AssetDatabase.SaveAssetIfDirty(map);
                AssetDatabase.SaveAssetIfDirty(type);
                AssetDatabase.SaveAssetIfDirty(aircraftType);
                SampleVehicleDemoPresentation.Prepare();
                AddBuildScenes(initPath, "Assets/UnityMultiplayerARPG/Demo/Scenes/01Home.unity", mapPath);
                Debug.Log("Sample demo created. Open 00Init_VehicleSamples, create a new character, and start Single Player or Host.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(stage, true);
            }
        }

        private static GameObject CreateVehicle(string source, string name, VehicleType type, Scene stage, bool aircraft)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(RequireAsset<GameObject>(source), stage);
            try
            {
                root.name = name;
                // Old sample prefabs can contain missing optional Standard Assets components.
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component is UnityStandardAssets.Vehicles.Car.CarUserControl || component is UnityStandardAssets.Vehicles.Car.CarAIControl ||
                        component is UnityStandardAssets.Vehicles.Car.CarSelfRighting || component is UnityStandardAssets.Vehicles.Aeroplane.AeroplaneUserControl2Axis ||
                        component is UnityStandardAssets.Vehicles.Aeroplane.AeroplaneUserControl4Axis || component is UnityStandardAssets.Vehicles.Aeroplane.AeroplaneAiControl)
                        component.enabled = false;
                root.AddComponent<LiteNetLibIdentity>();
                root.AddComponent<SampleVehiclePhysics>();
                var movement = root.AddComponent<SampleVehicleEntityMovement>();
                var body = root.GetComponent<Rigidbody>();
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var bounds = movement.GetMovementBounds();
                var interaction = root.AddComponent<BoxCollider>();
                interaction.isTrigger = true;
                interaction.center = root.transform.InverseTransformPoint(bounds.center);
                interaction.size = new Vector3(bounds.size.x / root.transform.lossyScale.x, bounds.size.y / root.transform.lossyScale.y, bounds.size.z / root.transform.lossyScale.z);
                var entity = root.AddComponent<VehicleEntity>();
                SetObject(entity, "vehicleType", type);
                SetFloat(entity, "activatableDistance", aircraft ? 8f : 4f);
                SetBool(entity, "canBeAttacked", true);
                var data = new SerializedObject(entity);
                data.FindProperty("hp").FindPropertyRelative("baseAmount").intValue = 1000;
                data.FindProperty("destroyDelay").floatValue = 3f;
                data.FindProperty("destroyRespawnDelay").floatValue = 10f;
                data.ApplyModifiedPropertiesWithoutUndo();
                var camera = Child(root.transform, "CameraTarget", new Vector3(0f, aircraft ? 2.5f : 1.5f, 0f));
                entity.CameraTargetTransform = entity.FpsCameraTargetTransform = entity.CombatTextTransform = entity.OpponentAimTransform = camera;
                for (int i = 0; i < 2; ++i)
                    entity.Seats.Add(new VehicleSeat
                    {
                        cameraTarget = VehicleSeatCameraTarget.Vehicle,
                        passengingTransform = Child(root.transform, i == 0 ? "DriverSeat" : "PassengerSeat", new Vector3(i == 0 ? -0.4f : 0.4f, 0.7f, 0f)),
                        exitTransform = Child(root.transform, i == 0 ? "DriverExit" : "PassengerExit", new Vector3((i == 0 ? -1f : 1f) * (interaction.size.x * 0.5f + 1f), 0.5f, 0f)),
                        hidePassenger = true,
                    });
                root.AddComponent<SampleVehicleCrashDamage>();
                var hit = root.AddComponent<SampleVehicleHitDamage>();
                SetVector(hit, "_center", interaction.center);
                SetVector(hit, "_size", interaction.size + Vector3.one * 0.05f);
                return PrefabUtility.SaveAsPrefabAsset(root, Root + "/Demo/Prefabs/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/MMORPG KIT/Vehicle Samples/Create Player Entity Prefabs")]
        public static void CreatePlayerEntityPrefabs()
        {
            var database = RequireAsset<GameDatabase>(Root + "/Demo/GameData/GameDatabase_Sample.asset");
            CreatePlayerEntityPrefabs(database);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
        }

        private static void CreatePlayerEntityPrefabs(GameDatabase database)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before creating player entity prefabs.");
#if !EXCLUDE_PREFAB_REFS || DISABLE_ADDRESSABLES
            var sources = RequireAsset<GameDatabase>(DatabaseSource).playerCharacterEntities
                .Where(source => source != null).ToArray();
            if (sources.Length == 0)
                throw new InvalidOperationException("Source database has no valid player character entity prefabs.");
            var classes = database.playerCharacters.ToDictionary(data => data.Id);
            var prefabs = new BasePlayerCharacterEntity[sources.Length];
            Scene previous = SceneManager.GetActiveScene();
            Scene stage = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                for (int i = 0; i < sources.Length; ++i)
                {
                    var source = sources[i];
                    string path = Root + "/Demo/Prefabs/" + source.name + "_Sample.prefab";
                    var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(existing != null ? existing : source.gameObject, stage);
                    try
                    {
                        instance.name = source.name + "_Sample";
                        var entity = instance.GetComponent<BasePlayerCharacterEntity>();
                        var serialized = new SerializedObject(entity);
                        var data = serialized.FindProperty("characterDatabases");
                        var sourceClasses = source.CharacterDatabases;
                        if (sourceClasses.Length == 0)
                            throw new InvalidOperationException("Source player entity has no classes: " + source.name);
                        data.arraySize = sourceClasses.Length;
                        for (int c = 0; c < sourceClasses.Length; ++c)
                        {
                            if (sourceClasses[c] == null || !classes.TryGetValue(sourceClasses[c].Id + "_sample", out var demoClass))
                                throw new InvalidOperationException("Missing demo class for " + source.name);
                            data.GetArrayElementAtIndex(c).objectReferenceValue = demoClass;
                        }
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        prefabs[i] = PrefabUtility.SaveAsPrefabAsset(instance, path).GetComponent<BasePlayerCharacterEntity>();
                    }
                    finally { Object.DestroyImmediate(instance); }
                }
                database.playerCharacterEntities = prefabs;
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(stage, true);
            }
#else
            throw new InvalidOperationException("Demo generation requires direct prefab references enabled.");
#endif
        }

        private static void CreateDrivingArea()
        {
            CreateBox("Airfield ground", new Vector3(0f, -1f, 500f), new Vector3(5000f, 1f, 5000f), new Color(0.25f, 0.35f, 0.22f));
            CreateBox("Runway", new Vector3(0f, -0.5f, 500f), new Vector3(80f, 1f, 1200f), new Color(0.22f, 0.25f, 0.28f));
            for (int i = 0; i < 28; ++i)
                CreateBox("Runway center stripe", new Vector3(0f, 0.01f, 60f + i * 35f), new Vector3(0.5f, 0.02f, 12f), Color.white);
            for (int i = 0; i < 8; ++i)
                CreateBox("Car slalom marker", new Vector3(-25f + (i % 2 == 0 ? -4f : 4f), 0.5f, 55f + i * 8f), Vector3.one, new Color(1f, 0.5f, 0.1f));
            CreateBox("Car impact wall", new Vector3(-25f, 1f, 150f), new Vector3(10f, 2f, 1f), Color.gray);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
        }

        private static GameObject CreateBox(string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            string materialPath = Root + "/Demo/GameData/" + name.Replace(" ", "_") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = color;
                AssetDatabase.CreateAsset(material, materialPath);
            }
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static Transform Child(Transform parent, string name, Vector3 position)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            return child;
        }

        private static T RequireAsset<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException("Required demo dependency not found: " + path);

        private static T CreateAsset<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            AssetDatabase.CreateAsset(asset, Root + "/Demo/GameData/" + name + ".asset");
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        private static void SetObject(Object target, string field, Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetBool(Object target, string field, bool value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).boolValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetFloat(Object target, string field, float value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).floatValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetVector(Object target, string field, Vector3 value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).vector3Value = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void AddBuildScenes(params string[] paths)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (string path in paths)
            {
                if (!scenes.Any(scene => scene.path == path))
                    scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
