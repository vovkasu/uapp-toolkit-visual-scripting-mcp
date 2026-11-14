using System;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    public static class VsGetSettings
    {
        public static JObject HandleCommand(JObject parameters) => VsSettingsCommands.Execute(parameters, false);
    }

    public static class VsUpdateSettings
    {
        public static JObject HandleCommand(JObject parameters) => VsSettingsCommands.Execute(parameters, true);
    }

    internal static class VsSettingsCommands
    {
        public static JObject Execute(JObject input, bool update)
        {
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new VsGraphException("editor_busy", "Settings require idle Edit Mode.");
                if (!update)
                {
                    if (input != null && input.Count != 0) throw new ArgumentException("vs_get_settings takes no parameters.");
                    return VsResponse.Success(VsSettingsService.Read());
                }
                return VsResponse.Success(VsSettingsService.Update(VsSettingsPlan.Parse(input)));
            }
            catch (VsGraphException exception) { return VsResponse.Failure(exception.Code, exception.Message); }
            catch (ArgumentException exception) { return VsResponse.Failure("invalid_arguments", exception.Message); }
            catch (Exception exception) { return VsResponse.Failure("settings_failed", "Settings operation failed: " + exception.GetType().Name); }
        }
    }
}
