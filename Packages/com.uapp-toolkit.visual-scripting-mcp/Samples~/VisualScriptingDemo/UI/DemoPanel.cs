using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

namespace UAppToolKit.VisualScriptingMcp.Samples
{
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(100)]
    public sealed class DemoPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            public string Label;
            public string Source;
            public GameObject Target;
            public string EventName;
        }

        private sealed class RowView
        {
            public Button Button;
            public Label Count;
            public Label Result;
        }

        [SerializeField] private string heading = "Visual Scripting Demo";
        [SerializeField] private string subtitle = "Run a graph and inspect the result written by its owner.";
        [SerializeField] private Row[] rows = Array.Empty<Row>();

        private VisualElement shell;
        private VisualElement stations;
        private RowView[] views;
        private IVisualElementScheduledItem refresh;
        private bool started;

        private void Start()
        {
            started = true;
            Build();
        }

        private void OnEnable()
        {
            if (started)
                Build();
        }

        private void OnDisable()
        {
            refresh?.Pause();
            refresh = null;
            shell?.UnregisterCallback<GeometryChangedEvent>(Resize);
            stations?.Clear();
            shell = null;
            stations = null;
            views = null;
        }

        public bool Trigger(int rowIndex)
        {
            if (!Application.isPlaying || rowIndex < 0 || rowIndex >= rows.Length || !CanTrigger(rows[rowIndex]))
                return false;

            var row = rows[rowIndex];
            CustomEvent.Trigger(row.Target, row.EventName);
            if (views != null)
                UpdateResults();
            return true;
        }

        private void Build()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            shell = root?.Q<VisualElement>("demoShell");
            stations = shell?.Q<VisualElement>("stations");
            if (stations == null)
            {
                Debug.LogError("Assign DemoPanel.uxml to the sample UI Document.", this);
                return;
            }

            shell.Q<Label>("heading").text = heading;
            shell.Q<Label>("subtitle").text = subtitle;
            shell.RegisterCallback<GeometryChangedEvent>(Resize);
            views = new RowView[rows.Length];
            for (var i = 0; i < rows.Length; i++)
                views[i] = AddStation(i, rows[i]);

            UpdateResults();
            refresh = shell.schedule.Execute(UpdateResults).Every(100);
        }

        private RowView AddStation(int index, Row row)
        {
            var slot = new VisualElement();
            slot.AddToClassList("station-slot");
            stations.Add(slot);

            var card = new VisualElement();
            card.AddToClassList("station-card");
            slot.Add(card);

            AddLabel(card, "station-name", row?.Label ?? "Unassigned graph");
            AddLabel(card, "station-source", row?.Source ?? string.Empty);

            var result = AddLabel(card, "station-result", string.Empty);
            var actions = new VisualElement();
            actions.AddToClassList("station-actions");
            card.Add(actions);

            var count = AddLabel(actions, "station-count", string.Empty);
            var button = new Button(() => Trigger(index))
            {
                name = $"runGraph{index}",
                text = "Run graph",
                tooltip = row?.EventName
            };
            button.AddToClassList("station-run");
            actions.Add(button);
            return new RowView { Button = button, Count = count, Result = result };
        }

        private static Label AddLabel(VisualElement parent, string className, string value)
        {
            var label = new Label(value);
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        private static bool CanTrigger(Row row) => row?.Target != null
            && row.Target.activeInHierarchy
            && !string.IsNullOrWhiteSpace(row.EventName);

        private void UpdateResults()
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                var view = views[i];
                var ready = CanTrigger(row);
                view.Button.SetEnabled(ready);

                if (row?.Target == null || !row.Target.TryGetComponent<Variables>(out var variables))
                {
                    view.Count.text = string.Empty;
                    view.Result.text = "Object Variables are unavailable.";
                    view.Result.EnableInClassList("station-result--ran", false);
                    continue;
                }

                var declarations = variables.declarations;
                var runCount = declarations.IsDefined("RunCount") && declarations.Get("RunCount") is int count ? count : 0;
                var result = declarations.IsDefined("Result") ? declarations.Get("Result") as string : null;
                view.Count.text = runCount == 1 ? "1 run" : $"{runCount} runs";
                view.Result.text = string.IsNullOrEmpty(result) ? "Waiting for a graph result." : result;
                view.Result.EnableInClassList("station-result--ran", runCount > 0);
            }
        }

        private void Resize(GeometryChangedEvent evt)
        {
            stations.EnableInClassList("stations--two-columns", evt.newRect.width < 920f);
            stations.EnableInClassList("stations--one-column", evt.newRect.width < 620f);
        }
    }
}
