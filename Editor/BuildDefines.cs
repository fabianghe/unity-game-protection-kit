using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace GameProtection.Editor
{
    static class BuildDefines
    {
        /// <summary>
        /// Player Settings defines for the target, plus the editor's active compilation defines when the target is the
        /// active one (that covers defines set on a Unity 6 Build Profile).
        /// </summary>
        public static bool IsDefined(BuildTarget target, string define)
        {
            if (string.IsNullOrWhiteSpace(define)) return false;
            var named = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));

            var fromPlayerSettings = PlayerSettings.GetScriptingDefineSymbols(named).Split(';').Select(d => d.Trim());
            var fromActive = target == EditorUserBuildSettings.activeBuildTarget
                ? EditorUserBuildSettings.activeScriptCompilationDefines
                : new string[0];

            return fromPlayerSettings.Concat(fromActive).Contains(define.Trim());
        }

        public static void Add(NamedBuildTarget target, string define)
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';')
                .Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
            if (defines.Contains(define)) return;
            defines.Add(define);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        }

        public static NamedBuildTarget Named(BuildTarget target) =>
            NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
    }
}
