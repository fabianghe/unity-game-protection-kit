using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameProtection.Content;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GameProtection.Editor
{
    /// <summary>Editor side of Content Lock: config, key embedding for Local mode, folder encryption, validation.</summary>
    public static class ContentLockEditor
    {
        static readonly string EmbeddedKeysPath = $"{ProtectionSetup.GeneratedResourcesFolder}/{LocalKeyProvider.ResourceName}.bytes";
        static readonly string[] SkipExtensions = { ".manifest", ".meta", ".txt", ".json", ".hash", ".gpktmp" };

        /// <summary>Optional editor modules (Addressables) add their validation rules here.</summary>
        public static readonly List<Func<BuildTarget, bool, IEnumerable<ValidationIssue>>> ExtraRules =
            new List<Func<BuildTarget, bool, IEnumerable<ValidationIssue>>>();

        public static ContentLockConfig Config => Resources.Load<ContentLockConfig>(ContentLockConfig.ResourceName);

        [InitializeOnLoadMethod]
        static void HookEditorKeys() => LocalKeyProvider.EditorKeySource = ContentLockKeyStore.LoadRing;

        public static ContentLockConfig EnsureConfig()
        {
            var existing = Config;
            if (existing != null) return existing;

            ProtectionSetup.EnsureFolder(ProtectionSetup.ConfigFolder);
            var config = ScriptableObject.CreateInstance<ContentLockConfig>();
            AssetDatabase.CreateAsset(config, $"{ProtectionSetup.ConfigFolder}/{ContentLockConfig.ResourceName}.asset");
            return config;
        }

        // ---------- build hooks (called by ReleaseBuildGuards) ----------

        /// <summary>Local mode: writes the masked key blob into Resources for this build. Any other mode: makes sure none ships.</summary>
        public static void OnPreprocessBuild(bool isDemoBuild)
        {
            var config = Config;
            var embed = config != null && config.enabled && config.mode == ContentKeyMode.Local;
            if (embed && isDemoBuild)
            {
                // A free demo must never carry the keys to paid content: a thief would lift them from the demo.
                Debug.Log("[GameProtection] Demo build: content keys are not embedded. Keep demo groups on the stock provider.");
                embed = false;
            }
            if (!embed)
            {
                RemoveEmbeddedKeys();
                return;
            }

            var ring = ContentLockKeyStore.EnsureRing();
            var blob = KeyMask.Apply(ring.ToBlob(), PlayerSettings.productName, PlayerSettings.companyName);
            ProtectionSetup.EnsureGeneratedFolder();
            File.WriteAllBytes(EmbeddedKeysPath, blob);
            AssetDatabase.ImportAsset(EmbeddedKeysPath, ImportAssetOptions.ForceSynchronousImport);
        }

        public static void OnPostprocessBuild() => RemoveEmbeddedKeys();

        internal static void RemoveEmbeddedKeys()
        {
            if (File.Exists(EmbeddedKeysPath)) AssetDatabase.DeleteAsset(EmbeddedKeysPath);
        }

        // ---------- plain AssetBundles ----------

        [MenuItem("Tools/Game Protection/Content Lock/Encrypt Bundles In Folder...", priority = 30)]
        static void EncryptFolderFromMenu()
        {
            var folder = EditorUtility.OpenFolderPanel("Folder with built AssetBundles", "", "");
            if (string.IsNullOrEmpty(folder)) return;

            var count = EncryptFolder(folder, ContentLockKeyStore.EnsureRing().Current);
            EditorUtility.DisplayDialog("Game Protection", $"Encrypted {count} file(s) in\n{folder}\n\nAlready-encrypted files were skipped.", "OK");
        }

        /// <summary>Encrypts every bundle file in a folder in place. Skips manifests/metadata and files already encrypted.</summary>
        public static int EncryptFolder(string folder, ContentKey key, bool recursive = false)
        {
            var count = 0;
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.GetFiles(folder, "*", option))
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (SkipExtensions.Contains(extension) || Path.GetFileName(file).StartsWith(".")) continue;
                if (ContentEncryptor.EncryptFile(file, key)) count++;
            }
            Debug.Log($"[GameProtection] Encrypted {count} bundle(s) in {folder}");
            return count;
        }

        // ---------- validation ----------

        public static IEnumerable<ValidationIssue> Validate(BuildTarget target, bool isDevelopmentBuild)
        {
            var config = Config;
            if (config == null)
            {
                yield return new ValidationIssue(IssueSeverity.Info, "Content Lock is not set up (assets stay unencrypted).",
                    "Create config", () => { EnsureConfig(); AssetDatabase.SaveAssets(); });
                yield break;
            }
            if (!config.enabled)
            {
                yield return new ValidationIssue(IssueSeverity.Info,
                    "Content Lock is off: shipped assets can be extracted with one-click rippers. Enable it to encrypt bundles.");
                yield break;
            }

            if (ContentLockKeyStore.LoadRing().Count == 0)
                yield return new ValidationIssue(IssueSeverity.Error, "Content Lock is on but there is no content key.",
                    "Create key", () => ContentLockKeyStore.EnsureRing());

            if (config.mode == ContentKeyMode.Remote)
            {
                if (!Uri.TryCreate(config.keyServerUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    yield return new ValidationIssue(IssueSeverity.Error, "Remote mode needs an https key server URL in ContentLockConfig.");

                if (!string.IsNullOrEmpty(config.devSharedSecret) && !isDevelopmentBuild)
                    yield return new ValidationIssue(IssueSeverity.Error,
                        "Dev shared secret is set. A release build would ship it, and anyone who extracts it can fetch your keys.",
                        "Clear secret", () => { config.devSharedSecret = string.Empty; EditorUtility.SetDirty(config); AssetDatabase.SaveAssets(); });

                var hasProofModule = HasProofModuleFor(target);
                if (!hasProofModule && string.IsNullOrEmpty(config.devSharedSecret))
                    yield return new ValidationIssue(IssueSeverity.Warning,
                        "No entitlement proof provider for this platform (Steam module on desktop, Play Integrity sample on Android). " +
                        "Players would be unable to fetch keys unless you register your own IEntitlementProofProvider.");
            }
            else
            {
                yield return new ValidationIssue(IssueSeverity.Info,
                    "Local mode: keys ship inside the build (masked). Stops asset rippers, not a determined attacker. Remote mode keeps keys off the device.");
            }

            foreach (var rule in ExtraRules)
                foreach (var issue in rule(target, isDevelopmentBuild))
                    yield return issue;
        }

        static bool HasProofModuleFor(BuildTarget target)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToList();
            if (BuildPipeline.GetBuildTargetGroup(target) == BuildTargetGroup.Standalone) return assemblies.Contains("GameProtection.Steam");
            if (target == BuildTarget.Android) return TypeExists("PlayIntegrityProofProvider");
            return false;
        }

        static bool TypeExists(string typeName) =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => { try { return a.GetType(typeName) != null; } catch { return false; } });
    }
}
