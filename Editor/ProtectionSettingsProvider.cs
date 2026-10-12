using System.Collections.Generic;
using System.Linq;
using GameProtection.Content;
using UnityEditor;
using UnityEngine;

namespace GameProtection.Editor
{
    /// <summary>Project Settings > Game Protection: setup, validation with one-click fixes, and all settings assets inline.</summary>
    static class ProtectionSettingsProvider
    {
        static List<ValidationIssue> _issues = new List<ValidationIssue>();
        static UnityEditor.Editor _configEditor;
        static UnityEditor.Editor _guardEditor;
        static UnityEditor.Editor _contentEditor;
        static bool _showConfig = true;
        static bool _showGuard = true;
        static bool _showContent = true;
        static Vector2 _scroll;

        [SettingsProvider]
        public static SettingsProvider Create() => new SettingsProvider(ProtectionSetup.SettingsPath, SettingsScope.Project)
        {
            label = "Game Protection",
            activateHandler = (_, __) => Refresh(),
            guiHandler = _ => Draw(),
            keywords = new HashSet<string> { "protection", "piracy", "obfuscation", "demo", "integrity", "il2cpp", "steam", "tamper", "encrypt", "content lock", "addressables" },
        };

        static void Refresh() =>
            _issues = ProtectionValidator.Validate(EditorUserBuildSettings.activeBuildTarget);

        static void Draw()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Detects re-uploaded, re-signed and rehosted copies, encrypts your content so rippers get nothing usable, " +
                "and stops demo builds from leaking full-game content. Nothing here is DRM: legit players are never blocked, " +
                "and no data leaves the game unless you plug in your own reporter.", MessageType.None);

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
            if (config != null && (_showConfig = EditorGUILayout.Foldout(_showConfig, "Detection (ships with the game)", true)))
                DrawInspector(config, ref _configEditor);

            var content = ContentLockEditor.Config;
            if (content != null && (_showContent = EditorGUILayout.Foldout(_showContent, "Content Lock (asset encryption)", true)))
            {
                DrawInspector(content, ref _contentEditor);
                DrawKeys();
            }

            var guard = ReleaseGuardSettings.Find();
            if (guard != null && (_showGuard = EditorGUILayout.Foldout(_showGuard, "Release guards (editor only)", true)))
                DrawInspector(guard, ref _guardEditor);

            EditorGUILayout.EndScrollView();
        }

        static void DrawKeys()
        {
            using (new EditorGUI.IndentLevelScope())
            {
                var ring = ContentLockKeyStore.LoadRing();
                EditorGUILayout.LabelField("Content keys", EditorStyles.boldLabel);
                if (ring.Count == 0)
                {
                    EditorGUILayout.HelpBox("No content key yet. One is created automatically at the first build or encryption.", MessageType.None);
                }
                else
                {
                    EditorGUILayout.LabelField("Current key id", ContentKey.FormatId(ring.Current.Id));
                    if (ring.Count > 1) EditorGUILayout.LabelField("Older keys kept", (ring.Count - 1).ToString());
                    EditorGUILayout.HelpBox(
                        "Back up Assets/GameProtection/Editor/ContentLockKeys.asset. Remote mode: the key server's CONTENT_KEYS " +
                        "must contain the same keys (use the copy button).", MessageType.None);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15f);
                    if (ring.Count == 0 && GUILayout.Button("Create key")) { ContentLockKeyStore.EnsureRing(); Refresh(); }
                    if (ring.Count > 0 && GUILayout.Button("Copy keys for server (JSON)"))
                    {
                        var entries = ring.Keys.Select(k => $"{{\"id\":\"{ContentKey.FormatId(k.Id)}\",\"key\":\"{k.ToBase64()}\"}}");
                        EditorGUIUtility.systemCopyBuffer = "[" + string.Join(",", entries) + "]";
                        Debug.Log("[GameProtection] Key ring copied to the clipboard. Set it as the CONTENT_KEYS secret on the key server.");
                    }
                    if (ring.Count > 0 && GUILayout.Button("Rotate key") && EditorUtility.DisplayDialog("Rotate content key?",
                            "A new key becomes current. Content you build from now on uses it; already-shipped content keeps loading " +
                            "with the old key, which is kept.\n\nRemote mode: update CONTENT_KEYS on the server before shipping.", "Rotate", "Cancel"))
                    {
                        ContentLockKeyStore.Rotate();
                        Refresh();
                    }
                }
            }
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
