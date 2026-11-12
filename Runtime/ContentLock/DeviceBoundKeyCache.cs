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
    ///
    /// File layout: encrypted container (see ContentFormat) followed by a 32-byte HMAC-SHA256 of that container.
    /// The container's plaintext is: fetch time (8 bytes, UTC ticks), server TTL (8 bytes, seconds), then the keys.
    /// The HMAC is what makes a damaged file read as "no cache" instead of as a ring of wrong keys.
    /// </summary>
    public sealed class DeviceBoundKeyCache
    {
        const string FileName = "gpk-content.cache";
        const string InstallSaltPref = "GameProtection.InstallSalt";
        const int TagSize = 32;
        const int PayloadHeaderSize = 16;

        readonly string _path;
        readonly Func<string> _secret;

        public DeviceBoundKeyCache() : this(Path.Combine(Application.persistentDataPath, FileName)) { }

        public DeviceBoundKeyCache(string path) : this(path, DeviceSecret) { }

        /// <summary>Test seam: a fixed secret instead of the device identity.</summary>
        internal DeviceBoundKeyCache(string path, Func<string> secret)
        {
            _path = path;
            _secret = secret;
        }

        public bool TryLoad(out KeyRing keys, out DateTime fetchedAtUtc) => TryLoad(out keys, out fetchedAtUtc, out _);

        /// <param name="serverTtl">How long the server said the keys stay valid. Zero when it didn't say.</param>
        public bool TryLoad(out KeyRing keys, out DateTime fetchedAtUtc, out TimeSpan serverTtl)
        {
            keys = null;
            fetchedAtUtc = DateTime.MinValue;
            serverTtl = TimeSpan.Zero;
            try
            {
                if (!File.Exists(_path)) return false;
                var file = File.ReadAllBytes(_path);
                var secret = _secret();

                if (TryReadCurrent(file, secret, out keys, out fetchedAtUtc, out serverTtl)) return true;

                // Written by 0.3.0 and older. Read it once and rewrite it, so an update never sends an offline player
                // back to the key server.
                if (TryReadLegacy(file, secret, out keys, out fetchedAtUtc))
                {
                    Save(keys, fetchedAtUtc, TimeSpan.Zero);
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                // Whatever went wrong (locked file, damaged data, storage error), the answer is the same: there is
                // no usable cache and the keys are fetched again. Reading it must never take the game down.
                keys = null;
                return false;
            }
        }

        public void Save(KeyRing keys, DateTime fetchedAtUtc) => Save(keys, fetchedAtUtc, TimeSpan.Zero);

        public void Save(KeyRing keys, DateTime fetchedAtUtc, TimeSpan serverTtl)
        {
            try
            {
                var blob = keys.ToBlob();
                var plain = new byte[PayloadHeaderSize + blob.Length];
                WriteInt64(plain, 0, fetchedAtUtc.ToUniversalTime().Ticks);
                WriteInt64(plain, 8, Math.Max(0L, (long)serverTtl.TotalSeconds));
                Buffer.BlockCopy(blob, 0, plain, PayloadHeaderSize, blob.Length);

                var secret = _secret();
                var container = ContentEncryptor.Encrypt(plain, DeriveKey("enc", secret));
                byte[] tag;
                using (var mac = new HMACSHA256(DeriveKey("mac", secret).Bytes)) tag = mac.ComputeHash(container);

                var file = new byte[container.Length + tag.Length];
                Buffer.BlockCopy(container, 0, file, 0, container.Length);
                Buffer.BlockCopy(tag, 0, file, container.Length, tag.Length);

                // Write next to the target and swap, so a crash mid-write can't leave half a cache behind.
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var temp = _path + ".tmp";
                File.WriteAllBytes(temp, file);
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(temp, _path);
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
            catch (UnauthorizedAccessException) { }
        }

        static bool TryReadCurrent(byte[] file, string secret, out KeyRing keys, out DateTime fetchedAtUtc, out TimeSpan serverTtl)
        {
            keys = null;
            fetchedAtUtc = DateTime.MinValue;
            serverTtl = TimeSpan.Zero;

            var containerLength = file.Length - TagSize;
            if (containerLength < ContentFormat.HeaderSize + PayloadHeaderSize) return false;

            byte[] expected;
            using (var mac = new HMACSHA256(DeriveKey("mac", secret).Bytes)) expected = mac.ComputeHash(file, 0, containerLength);
            var difference = 0;
            for (var i = 0; i < TagSize; i++) difference |= expected[i] ^ file[containerLength + i];
            if (difference != 0) return false;

            var container = new byte[containerLength];
            Buffer.BlockCopy(file, 0, container, 0, containerLength);
            var plain = ContentEncryptor.Decrypt(container, DeriveKey("enc", secret));

            var ticks = ReadInt64(plain, 0);
            var ttlSeconds = ReadInt64(plain, 8);
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
            if (ttlSeconds < 0 || ttlSeconds > TimeSpan.MaxValue.TotalSeconds) return false;

            fetchedAtUtc = new DateTime(ticks, DateTimeKind.Utc);
            serverTtl = TimeSpan.FromSeconds(ttlSeconds);
            keys = ReadKeys(plain, PayloadHeaderSize);
            return keys.Count > 0;
        }

        static bool TryReadLegacy(byte[] file, string secret, out KeyRing keys, out DateTime fetchedAtUtc)
        {
            keys = null;
            fetchedAtUtc = DateTime.MinValue;

            ContentKey legacyKey;
            using (var sha = SHA256.Create()) legacyKey = ContentKey.FromBytes(sha.ComputeHash(Encoding.UTF8.GetBytes(secret)));
            if (!ContentFormat.TryReadHeader(file, out var header) || header.KeyId != legacyKey.Id) return false;

            var plain = ContentEncryptor.Decrypt(file, legacyKey);
            if (plain.Length < 8) return false;
            fetchedAtUtc = DateTime.FromBinary(BitConverter.ToInt64(plain, 0)).ToUniversalTime();
            keys = ReadKeys(plain, 8);
            return keys.Count > 0;
        }

        static KeyRing ReadKeys(byte[] plain, int offset)
        {
            var blob = new byte[plain.Length - offset];
            Buffer.BlockCopy(plain, offset, blob, 0, blob.Length);
            return KeyRing.FromBlob(blob);
        }

        static ContentKey DeriveKey(string purpose, string secret)
        {
            using (var sha = SHA256.Create())
                return ContentKey.FromBytes(sha.ComputeHash(Encoding.UTF8.GetBytes("GameProtection.KeyCache.v2|" + purpose + "|" + secret)));
        }

        static string DeviceSecret()
        {
            var salt = PlayerPrefs.GetString(InstallSaltPref, string.Empty);
            if (string.IsNullOrEmpty(salt))
            {
                salt = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(InstallSaltPref, salt);
                PlayerPrefs.Save();
            }
            return "GameProtection.DeviceKey.v1|" + SystemInfo.deviceUniqueIdentifier + "|" + salt + "|" + Application.identifier;
        }

        static void WriteInt64(byte[] buffer, int offset, long value)
        {
            for (var i = 0; i < 8; i++) buffer[offset + i] = (byte)(value >> (8 * i));
        }

        static long ReadInt64(byte[] buffer, int offset)
        {
            long value = 0;
            for (var i = 0; i < 8; i++) value |= (long)buffer[offset + i] << (8 * i);
            return value;
        }
    }
}
