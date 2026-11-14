using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    [InitializeOnLoad]
    internal static class VsTraceSession
    {
        private const double SampleInterval = 0.1;
        private static VsTraceBuffer _buffer;
        private static VsTraceRequest _request;
        private static MonoBehaviour _owner;
        private static EventHook _hook;
        private static readonly Action<CustomEventArgs> EventHandler = OnCustomEvent;
        private static Dictionary<string, string> _previous = new Dictionary<string, string>();
        private static string _status;
        private static string _debugStatus;
        private static string _stopReason;
        private static bool _active;
        private static bool _baseline;
        private static double _nextSample;

        static VsTraceSession()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Reset;
        }

        public static JObject Start(VsTraceRequest request)
        {
            if (!EditorApplication.isPlaying) throw new VsGraphException("not_playing", "Trace requires Play Mode.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !EditorApplication.isPlayingOrWillChangePlaymode)
                throw new VsGraphException("editor_busy", "Wait for stable Play Mode.");
            VsRuntimeSession.Require(request.Runtime.SessionId);
            if (_active) throw new VsGraphException("trace_already_running", "Stop the active trace before starting another.");
            var owner = VsRuntimeReader.Resolve(request.Runtime.InstanceId.Value);
            VsRuntimeReader.Status(request.Runtime); // Validate nested path before subscribing.
            Reset();
            _request = request;
            _owner = owner;
            _buffer = new VsTraceBuffer(request.Capacity);
            _hook = new EventHook(EventHooks.Custom, owner.gameObject);
            _active = true;
            _baseline = true;
            _nextSample = 0;
            EventBus.Register(_hook, EventHandler);
            EditorApplication.update += Tick;
            Add(new JObject { ["kind"] = "trace_started" });
            Sample();
            return Response(null, request.PageSize);
        }

        public static JObject Read(VsTraceRequest request)
        {
            Require(request.TraceId);
            return Response(request.Cursor, request.PageSize);
        }

        public static JObject Stop(VsTraceRequest request)
        {
            Require(request.TraceId);
            StopObserving("requested");
            return Response(null, request.PageSize);
        }

        private static void Require(string id)
        {
            if (_buffer == null || _buffer.Id != id)
                throw new VsGraphException("stale_trace", "Trace is unavailable. Start a new trace for the current Play session.");
        }

        private static JObject Response(string cursor, int pageSize)
        {
            var result = _buffer.Read(cursor, pageSize);
            result["active"] = _active;
            result["stop_reason"] = _stopReason;
            result["play_session_id"] = _request.Runtime.SessionId;
            result["instance_id"] = _request.Runtime.InstanceId.Value;
            result["parent_element_guids"] = _request.Runtime.PageRequest.ParentGuids.DeepClone();
            result["sample_interval_seconds"] = SampleInterval;
            result["full_execution_trace"] = false;
            result["limitations"] = new JArray("debug_data_is_sampled_not_execution_history", "baseline_may_predate_trace",
                "custom_events_are_owner_scoped_not_graph_scoped", "event_receipt_does_not_prove_handler_execution",
                "earlier_handler_exception_can_prevent_event_observation",
                "missing_event_does_not_prove_waiting", "last_node_does_not_prove_wait_location", "console_use_read_console");
            var response = VsResponse.Success(result);
            response["data"]["complete"] = false;
            response["data"]["next_cursor"] = result["cursor"].DeepClone();
            var warnings = (JArray)response["data"]["warnings"];
            warnings.Add("partial_observation");
            if ((long)result["lost_records"] > 0) warnings.Add("trace_records_lost");
            return response;
        }

        private static void Tick()
        {
            if (!_active) return;
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Reset();
                return;
            }
            if (EditorApplication.isUpdating || EditorApplication.timeSinceStartup < _nextSample) return;
            _nextSample = EditorApplication.timeSinceStartup + SampleInterval;
            Sample();
        }

        private static void Sample()
        {
            try
            {
                if (_owner == null) { StopObserving("owner_destroyed"); return; }
                foreach (var record in VsRuntimeReader.Status(_request.Runtime).Records)
                {
                    var serialized = record.ToString(Formatting.None);
                    if (serialized != _status)
                    {
                        _status = serialized;
                        Add(new JObject { ["kind"] = "machine_state", ["baseline"] = _baseline, ["state"] = record.DeepClone() });
                    }
                }
                var debug = VsTraceDebugReader.Capture((IMachine)_owner, _request.Runtime.PageRequest.ParentGuids);
                var debugStatus = debug["available"] + ":" + debug["reason"] + ":" + debug["truncated"];
                if (debugStatus != _debugStatus)
                {
                    _debugStatus = debugStatus;
                    Add(new JObject { ["kind"] = "debug_coverage", ["available"] = debug["available"].DeepClone(),
                        ["reason"] = debug["reason"]?.DeepClone(), ["truncated"] = debug["truncated"].DeepClone() });
                }
                var next = new Dictionary<string, string>();
                foreach (JObject record in debug["records"])
                {
                    var key = record["path"].ToString(Formatting.None) + ":" + record["element_guid"];
                    var serialized = record.ToString(Formatting.None);
                    next[key] = serialized;
                    if (!_previous.TryGetValue(key, out var before) || before != serialized)
                    {
                        record["baseline"] = _baseline;
                        Add(record);
                    }
                }
                _previous = next;
                _baseline = false;
            }
            catch (Exception exception)
            {
                Add(new JObject { ["kind"] = "observer_error", ["exception_type"] = exception.GetType().FullName });
                StopObserving("inspection_failed");
            }
        }

        private static void OnCustomEvent(CustomEventArgs args)
        {
            if (!_active) return;
            try
            {
                if (_request.EventNames.Count != 0 && !_request.EventNames.Contains(args.name)) return;
                var name = args.name;
                Add(new JObject { ["kind"] = "custom_event", ["event_name"] = name == null || name.Length <= 256 ? name : name.Substring(0, 256),
                    ["event_name_truncated"] = name != null && name.Length > 256, ["argument_count"] = args.arguments?.Length ?? 0 });
            }
            catch (Exception) { StopObserving("event_observer_failed"); }
        }

        private static void Add(JObject record)
        {
            record["observed_frame"] = Time.frameCount;
            record["observed_time"] = EditorApplication.timeSinceStartup;
            _buffer.Add(record);
        }

        private static void StopObserving(string reason)
        {
            if (!_active) return;
            _active = false;
            EventBus.Unregister(_hook, EventHandler);
            _hook = default;
            EditorApplication.update -= Tick;
            _stopReason = reason;
            Add(new JObject { ["kind"] = "trace_stopped", ["reason"] = reason });
            _owner = null;
            _previous.Clear();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state) => Reset();

        private static void Reset()
        {
            StopObserving("session_changed");
            _buffer = null;
            _request = null;
            _owner = null;
            _previous.Clear();
            _status = null;
            _debugStatus = null;
            _stopReason = null;
        }
    }
}
