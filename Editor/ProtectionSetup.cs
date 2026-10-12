using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GameProtection.Editor
{
    /// <summary>One-click setup: creates both settings assets, pre-filled from Player Settings, and opens the settings page.</summary>
    public static class ProtectionSetup
    {
        public const string Root = "Assets/GameProtection";
        public const string ConfigFolder = Root + "/Resources";
        public const string EditorFolder = Root + "/Editor";
        public const string GeneratedResourcesFolder = Root + "/Generated/Resources";
        public const string SettingsPath = "Project/Game Protection";
        public const string DocumentationUrl = "https://github.com/OWNER/unity-game-protection-kit#readme";

        [MenuItem("Tools/Game Protection/Setup", priority = 0)]
        public static void SetupFromMenu()
        {
            Run();
            SettingsService.OpenProjectSettings(SettingsPath);
        }

        [MenuItem("Tools/Game Protection/Settings and Validation", priority = 1)]
        static void OpenSettings() => SettingsService.OpenProjectSettings(SettingsPath);

        [MenuItem("Tools/Game Protection/Documentation", priority = 20)]
        static void OpenDocs() => Application.OpenURL(DocumentationUrl);

        public static void Run()
        {
            EnsureConfig();
            EnsureGuardSettings();
            AssetDatabase.SaveAssets();
        }

        /// <summary>The config the runtime will actually load (Resources/IntegrityConfig), or null.</summary>
        public static IntegrityConfig FindRuntimeConfig() => Resources.Load<IntegrityConfig>(IntegrityConfig.ResourceName);

        /// <summary>True if an IntegrityConfig exists somewhere the runtime can't find it.</summary>
        public static bool HasMisplacedConfig() =>
            FindRuntimeConfig() == null && AssetDatabase.FindAssets("t:" + nameof(IntegrityConfig)).Length > 0;

        public static IntegrityConfig EnsureConfig()
        {
            var existing = FindRuntimeConfig();
            if (existing != null) return existing;

            EnsureFolder(ConfigFolder);
            var config = ScriptableObject.CreateInstance<IntegrityConfig>();
            SyncIdentity(config);
            AssetDatabase.CreateAsset(config, $"{ConfigFolder}/{IntegrityConfig.ResourceName}.asset");
            Debug.Log($"[GameProtection] Created {ConfigFolder}/{IntegrityConfig.ResourceName}.asset (pre-filled from Player Settings).");
            return config;
        }

        public static ReleaseGuardSettings EnsureGuardSettings()
        {
            var existing = ReleaseGuardSettings.Find();
            if (existing != null)
            {
                EnsureOwnership(existing);
                return existing;
            }

            EnsureFolder(EditorFolder);
            var settings = ScriptableObject.CreateInstance<ReleaseGuardSettings>();
            EnsureOwnership(settings);
            AssetDatabase.CreateAsset(settings, $"{EditorFolder}/ReleaseGuardSettings.asset");
            Debug.Log($"[GameProtection] Created {EditorFolder}/ReleaseGuardSettings.asset.");
            return settings;
        }

        /// <summary>Copies product name, company name and mobile identifiers from Player Settings into the config.</summary>
        public static void SyncIdentity(IntegrityConfig config)
        {
            if (config == null) return;
            Undo.RecordObject(config, "Sync Game Protection identity");

            config.productName = PlayerSettings.productName;
            config.companyName = PlayerSettings.companyName;
            config.applicationIdentifiers = (config.applicationIdentifiers ?? new string[0])
                .Concat(new[]
                {
                    PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                    PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS),
                })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToArray();

            EditorUtility.SetDirty(config);
        }

        /// <summary>Generates the permanent ownership token once. Never overwrites an existing one.</summary>
        public static void EnsureOwnership(ReleaseGuardSettings settings)
        {
            if (settings == null) return;
            var changed = false;

            if (string.IsNullOrWhiteSpace(settings.ownershipToken))
            {
                settings.ownershipToken = ReleaseGuardSettings.NewToken();
                changed = true;
                Debug.Log($"[GameProtection] Ownership token created: {settings.ownershipToken}. Save it in your evidence folder.");
            }

            if (string.IsNullOrWhiteSpace(settings.ownershipStatement))
            {
                settings.ownershipStatement =
                    $"{PlayerSettings.productName} is an original work by {PlayerSettings.companyName}. All rights reserved.";
                changed = true;
            }

            if (changed) EditorUtility.SetDirty(settings);
        }

        public static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }
    }
}
