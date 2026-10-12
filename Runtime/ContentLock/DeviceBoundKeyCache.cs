using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GameProtection.Content
{
    /// <summary>
    /// Stores fetched keys on disk, encrypted with a secret derived from this device and this install, so the game
    /// works offline after the first successful fetch and a copied cache file is useless on another machine.
    /// </summary>
    public sealed class DeviceBoundKeyCache
    {
        const string FileName = "gpk-content.cache";
        const string InstallSaltPref = "GameProtection.InstallSalt";

        readonly string _path;

        public DeviceBoundKeyCache() : this(Path.Combine(Application.persistentDataPath, FileName)) { }

        public DeviceBoundKeyCache(string path) => _path = path;

        public bool TryLoad(out KeyRing keys, out DateTime fetchedAtUtc)
        {
            keys = null;
            fetchedAtUtc = DateTime.MinValue;
            try
            {
                if (!File.Exists(_path)) return false;
                var plain = ContentEncryptor.Decrypt(File.ReadAllBytes(_path), DeviceKey());
                if (plain.Length < 8) return false;

                fetchedAtUtc = DateTime.FromBinary(BitConverter.ToInt64(plain, 0));
                var blob = new byte[plain.Length - 8];
                Buffer.BlockCopy(plain, 8, blob, 0, blob.Length);
                keys = KeyRing.FromBlob(blob);
                return keys.Count > 0;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is ContentKeyMismatchException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Save(KeyRing keys, DateTime fetchedAtUtc)
        {
            try
            {
                var blob = keys.ToBlob();
                var plain = new byte[8 + blob.Length];
                Buffer.BlockCopy(BitConverter.GetBytes(fetchedAtUtc.ToBinary()), 0, plain, 0, 8);
                Buffer.BlockCopy(blob, 0, plain, 8, blob.Length);

                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllBytes(_path, ContentEncryptor.Encrypt(plain, DeviceKey()));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[GameProtection] Could not write the content key cache: " + e.Message);
            }
        }

        public void Clear()
        {
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (IOException) { }
        }

        static ContentKey DeviceKey()
        {
            var salt = PlayerPrefs.GetString(InstallSaltPref, string.Empty);
            if (string.IsNullOrEmpty(salt))
            {
                salt = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(InstallSaltPref, salt);
                PlayerPrefs.Save();
            }

            using (var sha = SHA256.Create())
            {
                var material = "GameProtection.DeviceKey.v1|" + SystemInfo.deviceUniqueIdentifier + "|" + salt + "|" + Application.identifier;
                return ContentKey.FromBytes(sha.ComputeHash(Encoding.UTF8.GetBytes(material)));
            }
        }
    }
}
