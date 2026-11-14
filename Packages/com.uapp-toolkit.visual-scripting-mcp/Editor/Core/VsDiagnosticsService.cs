using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsDiagnosticsService
    {
        public static JObject Capture()
        {
            var scene = SceneManager.GetActiveScene();
            var package = PackageInfo.FindForAssembly(typeof(FlowGraph).Assembly);

            return new JObject
            {
                ["assets_path"] = UnityEngine.Application.dataPath,
                ["unity_version"] = UnityEngine.Application.unityVersion,
                ["visual_scripting_version"] = package?.version,
                ["active_scene_path"] = scene.path,
                ["active_scene_dirty"] = scene.isDirty,
                ["debug_provider_bound"] = GraphPointer.fetchRootDebugDataBinding != null,
                ["full_execution_trace_supported"] = false
            };
        }
    }
}
