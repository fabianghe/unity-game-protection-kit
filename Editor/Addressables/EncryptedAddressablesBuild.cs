using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameProtection.Addressables;
using GameProtection.Content;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.ResourceManagement.Util;

namespace GameProtection.Editor.Addressables
{
    /// <summary>
    /// Encrypts the bundles of every group that uses the Encrypted AssetBundle Provider, right after each Addressables
    /// content build (local and remote build paths). Nothing else in your Addressables workflow changes.
    /// </summary>
    [InitializeOnLoad]
    public static class EncryptedAddressablesBuild
    {
        static EncryptedAddressablesBuild()
        {
            BuildScript.buildCompleted += OnBuildCompleted;
            ContentLockEditor.ExtraRules.Add(Validate);
        }

        static void OnBuildCompleted(AddressableAssetBuildResult result)
        {
            if (result == null || !string.IsNullOrEmpty(result.Error)) return;
            var config = ContentLockEditor.Config;
            if (config == null || !config.enabled) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var files = BundlesToEncrypt(result, settings).Distinct().Where(File.Exists).ToList();
            if (files.Count == 0) return;

            var key = ContentLockKeyStore.EnsureRing().Current;
            var total = files.Count(file => ContentEncryptor.EncryptFile(file, key));
            Debug.Log($"[GameProtection] Content Lock: encrypted {total} Addressables bundle(s) with key {ContentKey.FormatId(key.Id)}.");
        }

        /// <summary>
        /// The bundle files that belong to groups on the encrypted provider, and only those. Groups normally share one
        /// build folder, so encrypting "every bundle in the folder" would also lock the bundles of groups that load
        /// through the stock provider (demo groups, and the shared shader bundle of the default group), and the stock
        /// provider can't open them.
        /// </summary>
        static IEnumerable<string> BundlesToEncrypt(AddressableAssetBuildResult result, AddressableAssetSettings settings)
        {
            // Entering Play Mode also raises buildCompleted; it builds no bundles.
            if (result is AddressablesPlayModeBuildResult) return Enumerable.Empty<string>();

            if (result is AddressablesPlayerBuildResult player && player.AssetBundleBuildResults != null && player.AssetBundleBuildResults.Count > 0)
                return player.AssetBundleBuildResults
                    .Where(b => b != null && b.SourceAssetGroup != null && !string.IsNullOrEmpty(b.FilePath) && UsesEncryptedProvider(b.SourceAssetGroup))
                    .Select(b => b.FilePath);

            // A build script that doesn't report its bundles: fall back to whole folders, but only folders that hold
            // nothing except encrypted groups.
            string BuildFolder(AddressableAssetGroup group) => group.GetSchema<BundledAssetGroupSchema>().BuildPath.GetValue(settings);

            var plainFolders = new HashSet<string>(settings.groups
                .Where(g => g != null && g.GetSchema<BundledAssetGroupSchema>() != null && !UsesEncryptedProvider(g))
                .Select(BuildFolder));
            var files = new List<string>();
            foreach (var folder in EncryptedGroups(settings).Select(BuildFolder).Distinct().Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
            {
                if (plainFolders.Contains(folder))
                    Debug.LogWarning($"[GameProtection] Content Lock: {folder} is shared by encrypted and unencrypted Addressables groups and this " +
                                     "build script doesn't report which bundle belongs to which group, so nothing in it was encrypted. " +
                                     "Give encrypted groups their own Build Path.");
                else
                    files.AddRange(Directory.GetFiles(folder, "*.bundle", SearchOption.AllDirectories));
            }
            return files;
        }

        public static IEnumerable<AddressableAssetGroup> EncryptedGroups(AddressableAssetSettings settings) =>
            settings.groups.Where(g => g != null && UsesEncryptedProvider(g));

        static bool UsesEncryptedProvider(AddressableAssetGroup group)
        {
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            return schema != null && schema.AssetBundleProviderType.Value == typeof(EncryptedAssetBundleProvider);
        }

        [MenuItem("Tools/Game Protection/Content Lock/Use Encrypted Provider For All Groups", priority = 31)]
        public static void UseEncryptedProviderForAllGroups()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                EditorUtility.DisplayDialog("Game Protection", "No Addressables settings found in this project.", "OK");
                return;
            }

            var changed = 0;
            foreach (var group in settings.groups.Where(g => g != null))
            {
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema == null || UsesEncryptedProvider(group)) continue;

                schema.AssetBundleProviderType = new SerializedType { Value = typeof(EncryptedAssetBundleProvider) };
                schema.UseAssetBundleCache = false; // the Unity bundle cache can't see through the container; our loader never uses it
                EditorUtility.SetDirty(schema);
                EditorUtility.SetDirty(group);
                changed++;
            }
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupSchemaModified, null, true, true);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameProtection] {changed} Addressables group(s) now use the Encrypted AssetBundle Provider. Rebuild Addressables content.");
        }

        static IEnumerable<ValidationIssue> Validate(BuildTarget target, bool isDevelopmentBuild)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) yield break;

            var bundledGroups = settings.groups.Count(g => g != null && g.GetSchema<BundledAssetGroupSchema>() != null);
            var encrypted = EncryptedGroups(settings).Count();
            if (bundledGroups > 0 && encrypted == 0)
                yield return new ValidationIssue(IssueSeverity.Warning,
                    "Content Lock is on, but no Addressables group uses the Encrypted AssetBundle Provider, so nothing gets encrypted.",
                    "Use it for all groups", UseEncryptedProviderForAllGroups);
            else if (encrypted > 0 && encrypted < bundledGroups)
                yield return new ValidationIssue(IssueSeverity.Info,
                    $"{encrypted} of {bundledGroups} Addressables groups are encrypted.");
        }
    }
}
