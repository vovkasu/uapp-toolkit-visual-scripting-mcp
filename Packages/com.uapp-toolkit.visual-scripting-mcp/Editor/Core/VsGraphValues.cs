using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsGraphValues
    {
        private const int MaximumStringLength = 4096;

        public static JToken Read(object value, VsGraphSnapshot snapshot)
        {
            if (value == null) return JValue.CreateNull();
            if (value is string text)
            {
                if (text.Length <= MaximumStringLength) return new JValue(text);
                snapshot.Warn("long_value_truncated");
                return new JObject { ["preview"] = text.Substring(0, MaximumStringLength), ["length"] = text.Length, ["omitted"] = true };
            }
            if (value is Object obj)
            {
                if (obj == null) return new JObject { ["missing_unity_object"] = true };
                return new JObject { ["object_id"] = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString(),
                    ["name"] = obj.name, ["type"] = obj.GetType().FullName, ["asset_path"] = AssetDatabase.GetAssetPath(obj) };
            }
            if (value is Type type) return new JValue(type.AssemblyQualifiedName);
            if (value is IGraphElement element) return new JObject { ["element_guid"] = element.guid.ToString() };
            if (value is System.Collections.Generic.List<string> names)
            {
                const int maximumNames = 256;
                var values = new JArray();
                for (var index = 0; index < names.Count && index < maximumNames; index++) values.Add(Read(names[index], snapshot));
                if (names.Count > maximumNames) snapshot.Warn("list_value_truncated");
                return values;
            }
            if (value is Member member)
            {
                var parameters = new JArray();
                if (member.parameterTypes != null)
                    foreach (var parameter in member.parameterTypes) parameters.Add(parameter?.AssemblyQualifiedName);
                return new JObject { ["declaring_type"] = member.targetTypeName, ["name"] = member.name, ["parameter_types"] = parameters };
            }
            var valueType = value.GetType();
            if (valueType.IsEnum) return new JValue(value.ToString());
            if (valueType.IsPrimitive || value is decimal) return new JValue(value);
            if (value is Vector2 v2) return new JArray(v2.x, v2.y);
            if (value is Vector3 v3) return new JArray(v3.x, v3.y, v3.z);
            if (value is Vector4 v4) return new JArray(v4.x, v4.y, v4.z, v4.w);
            if (value is Color color) return new JArray(color.r, color.g, color.b, color.a);
            if (value is Rect rect) return new JArray(rect.x, rect.y, rect.width, rect.height);
            if (value is Quaternion rotation) return new JArray(rotation.x, rotation.y, rotation.z, rotation.w);
            if (value is Guid guid) return new JValue(guid.ToString());
            snapshot.Warn("opaque_values_present");
            return new JObject { ["type"] = valueType.FullName, ["omitted"] = true };
        }

        public static JObject Configuration(IGraphElement element, VsGraphSnapshot snapshot)
        {
            var result = new JObject();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var type = element.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags))
                {
                    if (!IsSerialized(field)) continue;
                    var value = field.GetValue(element);
                    if (value is IGraphNest) continue;
                    result[type.Name + "." + field.Name] = Read(value, snapshot);
                }
                foreach (var property in type.GetProperties(flags))
                {
                    if (!IsSerialized(property)) continue;
                    if (element is IUnit && property.Name == "defaultValues") continue;
                    if (property.Name == "guid" || (element is IUnit && property.Name == "position")) continue;
                    var field = type.GetField("<" + property.Name + ">k__BackingField", flags);
                    if (field == null) { snapshot.Warn("computed_serialized_properties_not_evaluated"); continue; }
                    var value = field.GetValue(element);
                    if (value is IGraphNest || value is IGraph) continue;
                    result[type.Name + "." + property.Name] = Read(value, snapshot);
                }
            }
            return result;
        }

        private static bool IsSerialized(MemberInfo member)
        {
            foreach (var attribute in member.GetCustomAttributesData())
                if (attribute.AttributeType == typeof(SerializeAttribute) || attribute.AttributeType == typeof(SerializeAsAttribute)) return true;
            return false;
        }
    }
}
