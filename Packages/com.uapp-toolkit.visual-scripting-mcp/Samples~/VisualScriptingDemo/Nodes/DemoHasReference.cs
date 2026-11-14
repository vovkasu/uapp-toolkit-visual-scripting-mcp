using Unity.VisualScripting;
using UnityEngine;

namespace UAppToolKit.VisualScriptingMcp.Samples
{
    [UnitTitle("Demo Has Reference")]
    [UnitCategory("UAppToolKit/Demo")]
    public sealed class DemoHasReference : Unit
    {
        [DoNotSerialize] public ValueInput Target { get; private set; }
        [DoNotSerialize] public ValueOutput IsAssigned { get; private set; }

        protected override void Definition()
        {
            Target = ValueInput<Object>("target", null).AllowsNull();
            IsAssigned = ValueOutput<bool>("isAssigned", flow => flow.GetValue<Object>(Target) != null);
            Requirement(Target, IsAssigned);
        }
    }
}
