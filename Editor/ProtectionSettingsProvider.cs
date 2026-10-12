using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameProtection.Editor
{
    /// <summary>Project Settings > Game Protection: setup, validation with one-click fixes, and both settings assets inline.</summary>
    static class ProtectionSettingsProvider
    {
        static List<ValidationIssue> _issues = new List<ValidationIssue>();
        static UnityEditor.Editor _configEditor;
        static UnityEditor.Editor _guardEditor;
        static bool _showConfig = true;
        static bool _showGuard = true;
        static Vector2 _scroll;

        [SettingsProvider]
        public static SettingsProvider Create() => new SettingsProvider(ProtectionSetup.SettingsPath, SettingsScope.Project)
        {
            label = "Game Protection",
            activateHandler = (_, __) => Refresh(),
            guiHandler = _ => Draw(),
            keywords = new HashSet<string> { "protection", "piracy", "obfuscation", "demo", "integrity", "il2cpp", "steam", "tamper" },
        };

        static void Refresh() =>
            _issues = ProtectionValidator.Validate(EditorUserBuildSettings.activeBuildTarget);

        static void Draw()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Detects re-uploaded, re-signed and rehosted copies of your game, and stops demo builds from leaking " +
                "full-game content. Nothing here is DRM: legit players are never blocked, and no data leaves the game " +
                "unless you plug in your own reporter.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run setup")) { ProtectionSetup.Run(); Refresh(); }
                if (GUILayout.Button("Re-validate")) Refresh();
                if (GUILayout.Button("Documentation")) Application.OpenURL(ProtectionSetup.DocumentationUrl);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Validation for {EditorUserBuildSettings.activeBuildTarget}", EditorStyles.boldLabel);
            if (_issues.Count == 0) EditorGUILayout.HelpBox("All good.", MessageType.Info);

            foreach (var issue in _issues.ToArray())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox(issue.Message, ToMessageType(issue.Severity));
                    if (issue.Fix != null && GUILayout.Button(issue.FixLabel ?? "Fix", GUILayout.Width(150), GUILayout.ExpandHeight(true)))
                    {
                        issue.Fix();
                        Refresh();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            EditorGUILayout.Space();
            var config = ProtectionSetup.FindRuntimeConfig();
            if (config != null && (_showConfig = EditorGUILayout.Foldout(_showConfig, "Runtime config (ships with the game)", true)))
                DrawInspector(config, ref _configEditor);

            var guard = ReleaseGuardSettings.Find();
            if (guard != null && (_showGuard = EditorGUILayout.Foldout(_showGuard, "Release guards (editor only)", true)))
                DrawInspector(guard, ref _guardEditor);

            EditorGUILayout.EndScrollView();
        }

        static void DrawInspector(Object target, ref UnityEditor.Editor cached)
        {
            UnityEditor.Editor.CreateCachedEditor(target, null, ref cached);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                cached.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck()) Refresh();
            }
        }

        static MessageType ToMessageType(IssueSeverity severity)
        {
            switch (severity)
            {
                case IssueSeverity.Error: return MessageType.Error;
                case IssueSeverity.Warning: return MessageType.Warning;
                default: return MessageType.Info;
            }
        }
    }
}
