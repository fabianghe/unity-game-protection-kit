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

            var folders = EncryptedGroups(settings)
                .Select(g => g.GetSchema<BundledAssetGroupSchema>().BuildPath.GetValue(settings))
                .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
                .Distinct()
                .ToList();
            if (folders.Count == 0) return;

            var key = ContentLockKeyStore.EnsureRing().Current;
            var total = 0;
            foreach (var folder in folders)
                total += Directory.GetFiles(folder, "*.bundle", SearchOption.AllDirectories).Count(file => ContentEncryptor.EncryptFile(file, key));

            Debug.Log($"[GameProtection] Content Lock: encrypted {total} Addressables bundle(s) with key {ContentKey.FormatId(key.Id)}.");
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
