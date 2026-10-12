using System;
using System.Linq;
using GameProtection.Content;
using UnityEditor;
using UnityEngine;

namespace GameProtection.Editor
{
    /// <summary>
    /// The project's content keys, editor-only (lives in an Editor folder, never ships). The first key is current.
    /// Rotating adds a new current key and keeps the old ones so already-shipped content still decrypts.
    ///
    /// CI without the asset: set GAMEPROTECTION_CONTENT_KEYS to comma-separated base64 keys (current first).
    /// Keep a copy of the keys somewhere safe: without them, encrypted content you shipped can't be re-encrypted
    /// for a patch, and in Remote mode the server needs the same keys.
    /// </summary>
    public sealed class ContentLockKeyStore : ScriptableObject
    {
        public const string EnvironmentVariable = "GAMEPROTECTION_CONTENT_KEYS";
        public const string AssetPath = ProtectionSetup.EditorFolder + "/ContentLockKeys.asset";

        [Tooltip("Base64 content keys, current first. Edit only through the Game Protection settings page.")]
        public string[] keysBase64 = new string[0];

        public static ContentLockKeyStore Find()
        {
            var guid = AssetDatabase.FindAssets("t:" + nameof(ContentLockKeyStore)).FirstOrDefault();
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<ContentLockKeyStore>(AssetDatabase.GUIDToAssetPath(guid));
        }

        /// <summary>The key ring from the environment variable or the asset. Empty when neither exists.</summary>
        public static KeyRing LoadRing()
        {
            var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(env))
            {
                try { return KeyRing.FromBase64List(env.Split(',')); }
                catch (FormatException) { Debug.LogError($"[GameProtection] {EnvironmentVariable} contains invalid base64."); }
                catch (ArgumentException e) { Debug.LogError("[GameProtection] " + e.Message); }
            }

            var store = Find();
            if (store == null) return new KeyRing();
            try { return KeyRing.FromBase64List(store.keysBase64); }
            catch (FormatException) { Debug.LogError("[GameProtection] ContentLockKeys.asset contains an invalid key."); return new KeyRing(); }
            catch (ArgumentException e) { Debug.LogError("[GameProtection] " + e.Message); return new KeyRing(); }
        }

        /// <summary>Creates the store with a fresh key when none exists. Returns the ring.</summary>
        public static KeyRing EnsureRing()
        {
            var ring = LoadRing();
            if (ring.Count > 0) return ring;

            var store = Find();
            if (store == null)
            {
                ProtectionSetup.EnsureFolder(ProtectionSetup.EditorFolder);
                store = CreateInstance<ContentLockKeyStore>();
                AssetDatabase.CreateAsset(store, AssetPath);
            }

            var key = ContentKey.Generate();
            store.keysBase64 = new[] { key.ToBase64() };
            EditorUtility.SetDirty(store);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameProtection] Content key created ({ContentKey.FormatId(key.Id)}). Back up {AssetPath}.");
            return LoadRing();
        }

        /// <summary>Adds a new current key; previous keys remain for already-encrypted content.</summary>
        public static ContentKey Rotate()
        {
            EnsureRing();
            var store = Find();
            var key = ContentKey.Generate();
            store.keysBase64 = new[] { key.ToBase64() }.Concat(store.keysBase64 ?? new string[0]).ToArray();
            EditorUtility.SetDirty(store);
            AssetDatabase.SaveAssets();
            return key;
        }
    }
}
