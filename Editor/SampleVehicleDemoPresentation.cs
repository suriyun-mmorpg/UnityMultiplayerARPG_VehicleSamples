using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiplayerARPG
{
    [InitializeOnLoad]
    public static class SampleVehicleDemoPresentation
    {
        static SampleVehicleDemoPresentation() { EditorApplication.delayCall += ProcessRequest; }
        private static void ProcessRequest()
        {
            const string request = "Temp/SampleVehicle.Presentation.request";
            if (!File.Exists(request)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += ProcessRequest; return; }
            File.Delete(request);
            try { Prepare(); File.WriteAllText("Temp/SampleVehicle.Presentation.result", "SUCCESS"); }
            catch (Exception ex) { File.WriteAllText("Temp/SampleVehicle.Presentation.result", ex.ToString()); Debug.LogException(ex); }
        }

        [MenuItem("Tools/MMORPG KIT/Vehicle Samples/Prepare Demo Effects")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (!(GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)) return;
            foreach (string name in new[] { "Sample_Car", "Sample_Jet", "Sample_Propeller" })
            {
                string path = SampleVehicleDemoBuilder.Root + "/Demo/Prefabs/" + name + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ReplaceEffects(root);
                    foreach (var effect in root.GetComponentsInChildren<UnityStandardAssets.Vehicles.Car.WheelEffects>(true))
                    {
                        if (effect.SkidTrailPrefab == null) continue;
                        string trailPath = SampleVehicleDemoBuilder.Root + "/Demo/Prefabs/Sample_SkidTrail.prefab";
                        var trail = AssetDatabase.LoadAssetAtPath<GameObject>(trailPath);
                        if (trail == null)
                        {
                            var instance = UnityEngine.Object.Instantiate(effect.SkidTrailPrefab.gameObject);
                            try { ReplaceEffects(instance); trail = PrefabUtility.SaveAsPrefabAsset(instance, trailPath); }
                            finally { UnityEngine.Object.DestroyImmediate(instance); }
                        }
                        effect.SkidTrailPrefab = trail.transform;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            string scenePath = SampleVehicleDemoBuilder.Root + "/Demo/Scenes/VehicleSamplesAirfield.unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(SampleVehicleDemoBuilder.Root + "/Demo/GameData/" + root.name.Replace(" ", "_") + ".mat");
                    if (material != null && root.TryGetComponent<Renderer>(out var renderer)) renderer.sharedMaterial = material;
                }
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (!wasLoaded) EditorSceneManager.CloseScene(scene, true); }
        }

        private static void ReplaceEffects(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(CreateEffect).ToArray();
        }
        private static Material CreateEffect(Material source)
        {
            if (source == null || source.shader == null || source.shader.name.StartsWith("Universal Render Pipeline/")) return source;
            string shaderName = source.shader.name;
            bool effect = shaderName.Contains("Particles") || shaderName.Contains("Transparent");
            if (!effect && shaderName != "Legacy Shaders/Diffuse") return source;
            string path = SampleVehicleDemoBuilder.Root + "/Demo/GameData/" + source.name + "_URP.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            var scale = source.HasProperty("_MainTex") ? source.GetTextureScale("_MainTex") : Vector2.one;
            var offset = source.HasProperty("_MainTex") ? source.GetTextureOffset("_MainTex") : Vector2.zero;
            var color = source.HasProperty("_TintColor") ? source.GetColor("_TintColor") : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            bool additive = shaderName.Contains("Additive");
            var material = new Material(Shader.Find(effect ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Lit"));
            material.name = source.name + "_URP";
            material.SetTexture("_BaseMap", texture); material.SetTextureScale("_BaseMap", scale); material.SetTextureOffset("_BaseMap", offset);
            material.SetColor("_BaseColor", color);
            if (effect)
            {
                material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", additive ? 2f : 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                material.SetFloat("_ZWrite", 0f); material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(material, path);
            var saved = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (saved.GetTexture("_BaseMap") != texture || saved.GetColor("_BaseColor") != color)
                throw new InvalidOperationException("Effect texture or tint was not preserved: " + path);
            return saved;
        }
    }
}
