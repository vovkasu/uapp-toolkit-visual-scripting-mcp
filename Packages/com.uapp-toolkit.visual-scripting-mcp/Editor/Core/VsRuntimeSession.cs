using System;
using UnityEditor;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    [InitializeOnLoad]
    internal static class VsRuntimeSession
    {
        private static string _id;

        static VsRuntimeSession()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        }

        public static string Current
        {
            get
            {
                if (!EditorApplication.isPlaying) return null;
                return _id ?? (_id = Guid.NewGuid().ToString("N"));
            }
        }

        public static void Require(string expected)
        {
            if (expected != Current)
                throw new VsGraphException("stale_reference", "Play session changed. Discover machines again.");
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            _id = null;
        }

        private static void BeforeReload()
        {
            _id = null;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
        }
    }
}
