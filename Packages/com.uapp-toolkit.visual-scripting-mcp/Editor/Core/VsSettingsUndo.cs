using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    // Package settings are not an AssetDatabase asset. This Editor-only token records
    // one patch; Undo replays it through the same public metadata API as Apply.
    internal sealed class VsSettingsUndo : ScriptableObject
    {
        private const string UndoName = "Update Visual Scripting settings via MCP";
        private static readonly List<VsSettingsUndo> Records = new List<VsSettingsUndo>();
        [SerializeField] private bool _applied;
        private bool _lastApplied;
        private JObject _before;
        private JObject _after;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            Undo.undoRedoPerformed += Replay;
            AssemblyReloadEvents.beforeAssemblyReload += ClearRecords;
        }

        public static void Commit(JObject before, JObject after)
        {
            var record = CreateInstance<VsSettingsUndo>();
            record.hideFlags = HideFlags.HideAndDontSave;
            record._before = (JObject)before.DeepClone();
            record._after = (JObject)after.DeepClone();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            try
            {
                Undo.RegisterCompleteObjectUndo(record, UndoName);
                VsSettingsService.Apply(before, after);
                record._applied = record._lastApplied = true;
                Records.Add(record);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.ClearUndo(record);
                DestroyImmediate(record);
                throw;
            }
            finally { Undo.IncrementCurrentGroup(); }
        }

        private static void Replay()
        {
            var changed = Records.Where(record => record != null && record._applied != record._lastApplied).ToArray();
            foreach (var record in changed.Where(record => !record._applied).Reverse().Concat(changed.Where(record => record._applied)))
            {
                if (record == null || record._applied == record._lastApplied) continue;
                try
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                        throw new VsGraphException("editor_busy", "Settings Undo requires idle Edit Mode.");
                    VsNodeRegeneration.CheckAvailable();
                    VsSettingsService.Apply(record._applied ? record._before : record._after, record._applied ? record._after : record._before);
                    record._lastApplied = record._applied;
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning("VS settings Undo skipped and its record discarded: " + exception.Message);
                    Records.Remove(record);
                    Undo.ClearUndo(record);
                    DestroyImmediate(record);
                }
            }
        }

        internal static void ClearRecords()
        {
            foreach (var record in Records)
            {
                if (record == null) continue;
                Undo.ClearUndo(record);
                DestroyImmediate(record);
            }
            Records.Clear();
        }
    }
}
