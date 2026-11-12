using System;
using System.IO;
using System.Linq;
using GameProtection.Content;
using NUnit.Framework;

namespace GameProtection.Tests
{
    public sealed class ContentLockTests
    {
        static byte[] Random(int size, int seed = 7)
        {
            var bytes = new byte[size];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(70000)]
        public void ByteArray_RoundTrip(int size)
        {
            var key = ContentKey.Generate();
            var plain = Random(size);
            var container = ContentEncryptor.Encrypt(plain, key);

            Assert.AreEqual(size + ContentFormat.HeaderSize, container.Length);
            Assert.IsTrue(ContentFormat.HasMagic(container));
            CollectionAssert.AreEqual(plain, ContentEncryptor.Decrypt(container, key));
        }

        [Test]
        public void Stream_SeeksAndReadsCorrectly()
        {
            var key = ContentKey.Generate();
            var plain = Random(100000);
            var container = ContentEncryptor.Encrypt(plain, key);
            var rnd = new Random(3);

            using (var stream = DecryptingStream.Open(new MemoryStream(container), key))
            {
                Assert.AreEqual(plain.Length, stream.Length);
                for (var i = 0; i < 100; i++)
                {
                    var pos = rnd.Next(plain.Length);
                    var len = rnd.Next(1, Math.Min(3000, plain.Length - pos) + 1);
                    stream.Seek(pos, SeekOrigin.Begin);
                    var buffer = new byte[len];
                    Assert.AreEqual(len, stream.Read(buffer, 0, len));
                    CollectionAssert.AreEqual(plain.Skip(pos).Take(len).ToArray(), buffer);
                }
            }
        }

        [Test]
        public void WrongKey_FailsFastWithClearError()
        {
            var container = ContentEncryptor.Encrypt(Random(64), ContentKey.Generate());
            Assert.Throws<ContentKeyMismatchException>(() => ContentEncryptor.Decrypt(container, ContentKey.Generate()));
            Assert.Throws<ContentKeyMismatchException>(() => DecryptingStream.Open(new MemoryStream(container), ContentKey.Generate()));
        }

        [Test]
        public void PlainData_IsRejectedNotMisread()
        {
            Assert.Throws<InvalidDataException>(() => DecryptingStream.Open(new MemoryStream(new byte[100]), ContentKey.Generate()));
            Assert.IsFalse(ContentFormat.TryReadHeader(new byte[100], out _));
        }

        [Test]
        public void EncryptFile_IsIdempotent()
        {
            var key = ContentKey.Generate();
            var path = Path.Combine(Path.GetTempPath(), "gpk_test_" + Guid.NewGuid().ToString("N") + ".bundle");
            var original = Random(50000);
            File.WriteAllBytes(path, original);
            try
            {
                Assert.IsTrue(ContentEncryptor.EncryptFile(path, key));
                Assert.IsFalse(ContentEncryptor.EncryptFile(path, key), "a second pass must not double-encrypt");
                Assert.IsTrue(ContentFormat.IsEncryptedFile(path));
                Assert.IsFalse(File.Exists(path + ".gpktmp"));

                using (var stream = DecryptingStream.Open(File.OpenRead(path), key))
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    CollectionAssert.AreEqual(original, memory.ToArray());
                }

                Assert.IsTrue(ContentEncryptor.DecryptFile(path, key));
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void KeyRing_BlobAndMask_RoundTrip()
        {
            var a = ContentKey.Generate();
            var b = ContentKey.Generate();
            var ring = new KeyRing(new[] { a, b });

            var restored = KeyRing.FromBlob(ring.ToBlob());
            Assert.AreEqual(a.Id, restored.Current.Id);
            Assert.IsTrue(restored.TryGet(b.Id, out _));

            var masked = KeyMask.Apply(ring.ToBlob(), "Game", "Studio");
            CollectionAssert.AreNotEqual(ring.ToBlob(), masked);
            CollectionAssert.AreEqual(ring.ToBlob(), KeyMask.Apply(masked, "Game", "Studio"));
            CollectionAssert.AreNotEqual(ring.ToBlob(), KeyMask.Apply(masked, "Stolen Game", "Studio"));
        }

        [Test]
        public void Base64_RoundTrip_KeepsId()
        {
            var key = ContentKey.Generate();
            Assert.AreEqual(key.Id, ContentKey.FromBase64(key.ToBase64()).Id);
            Assert.AreEqual(16, ContentKey.FormatId(key.Id).Length);
        }

        static byte[] Hex(string hex) =>
            Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();

        /// <summary>NIST SP 800-38A, F.5.5 (CTR-AES256.Encrypt). Proves the cipher is standard AES-CTR, counter carry included.</summary>
        [Test]
        public void Cipher_MatchesNistAes256CtrVector()
        {
            var key = ContentKey.FromBytes(Hex("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4"));
            var counter = Hex("f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");
            var plain = Hex("6bc1bee22e409f96e93d7e117393172a" + "ae2d8a571e03ac9c9eb76fac45af8e51" +
                            "30c81c46a35ce411e5fbc1191a0a52ef" + "f69f2445df4f9b17ad2b417be66c3710");
            var expected = Hex("601ec313775789a5b7a7f504bbf3d228" + "f443e3ca4d62b59aca84e990cacaf5c5" +
                               "2b0930daa23de94ce87017ba2d84988d" + "dfc9c58db67aada613c2dd08457941a6");

            using (var cipher = new ContentCipher(key, counter))
            {
                var whole = (byte[])plain.Clone();
                cipher.Apply(whole, 0, whole.Length, 0);
                CollectionAssert.AreEqual(expected, whole);

                // Random access: decrypting a slice from the middle must give the same bytes as the full pass.
                var slice = expected.Skip(21).Take(30).ToArray();
                cipher.Apply(slice, 0, slice.Length, 21);
                CollectionAssert.AreEqual(plain.Skip(21).Take(30).ToArray(), slice);
            }
        }

        [Test]
        public void Encrypt_IsRepeatable_ButNeverReusesANonceForDifferentData()
        {
            var key = ContentKey.Generate();
            var a = Random(5000, 1);
            var b = Random(5000, 2);

            CollectionAssert.AreEqual(ContentEncryptor.Encrypt(a, key), ContentEncryptor.Encrypt(a, key), "unchanged content must not change on disk");

            ContentFormat.TryReadHeader(ContentEncryptor.Encrypt(a, key), out var headerA);
            ContentFormat.TryReadHeader(ContentEncryptor.Encrypt(b, key), out var headerB);
            ContentFormat.TryReadHeader(ContentEncryptor.Encrypt(a, ContentKey.Generate()), out var headerOtherKey);
            CollectionAssert.AreNotEqual(headerA.Nonce, headerB.Nonce);
            CollectionAssert.AreNotEqual(headerA.Nonce, headerOtherKey.Nonce);
        }

        [Test]
        public void EncryptFile_MatchesInMemoryEncryption()
        {
            var key = ContentKey.Generate();
            var path = Path.Combine(Path.GetTempPath(), "gpk_test_" + Guid.NewGuid().ToString("N") + ".bundle");
            var original = Random(3 * 1024 * 1024 + 123); // spans several chunks
            File.WriteAllBytes(path, original);
            try
            {
                Assert.IsTrue(ContentEncryptor.EncryptFile(path, key));
                CollectionAssert.AreEqual(ContentEncryptor.Encrypt(original, key), File.ReadAllBytes(path));
                Assert.IsTrue(ContentFormat.TryReadFileHeader(path, out var header));
                Assert.AreEqual(key.Id, header.KeyId);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    public sealed class KeyCacheTests
    {
        string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "gpk_cache_" + Guid.NewGuid().ToString("N") + ".cache");

        [TearDown]
        public void TearDown()
        {
            File.Delete(_path);
            File.Delete(_path + ".tmp");
        }

        static DeviceBoundKeyCache Cache(string path, string secret = "device-a") => new DeviceBoundKeyCache(path, () => secret);

        [Test]
        public void RoundTrip_KeepsKeysTimeAndTtl()
        {
            var ring = new KeyRing(new[] { ContentKey.Generate(), ContentKey.Generate() });
            var at = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            Cache(_path).Save(ring, at, TimeSpan.FromDays(7));

            Assert.IsTrue(Cache(_path).TryLoad(out var loaded, out var loadedAt, out var ttl));
            Assert.AreEqual(2, loaded.Count);
            Assert.AreEqual(ring.Current.Id, loaded.Current.Id);
            Assert.AreEqual(at, loadedAt);
            Assert.AreEqual(DateTimeKind.Utc, loadedAt.Kind);
            Assert.AreEqual(TimeSpan.FromDays(7), ttl);
            Assert.IsFalse(File.Exists(_path + ".tmp"));
        }

        [Test]
        public void OtherDevice_CannotReadIt()
        {
            Cache(_path, "device-a").Save(new KeyRing(new[] { ContentKey.Generate() }), DateTime.UtcNow);
            Assert.IsFalse(Cache(_path, "device-b").TryLoad(out _, out _));
        }

        [Test]
        public void DamagedFile_ReadsAsNoCache_NotAsWrongKeys()
        {
            Cache(_path).Save(new KeyRing(new[] { ContentKey.Generate() }), DateTime.UtcNow);
            var intact = File.ReadAllBytes(_path);

            for (var i = 0; i < intact.Length; i += 7)
            {
                var damaged = (byte[])intact.Clone();
                damaged[i] ^= 0x01;
                File.WriteAllBytes(_path, damaged);
                Assert.IsFalse(Cache(_path).TryLoad(out _, out _), "flipped bit at byte " + i + " was accepted");
            }

            File.WriteAllBytes(_path, intact.Take(intact.Length / 2).ToArray());
            Assert.IsFalse(Cache(_path).TryLoad(out _, out _), "truncated file was accepted");

            File.WriteAllBytes(_path, new byte[0]);
            Assert.IsFalse(Cache(_path).TryLoad(out _, out _), "empty file was accepted");
        }

        [Test]
        public void MissingFile_IsNoCache()
        {
            Assert.IsFalse(Cache(_path).TryLoad(out _, out _));
        }

        /// <summary>The 0.3.0 format: container keyed with SHA-256(secret), plaintext = DateTime.ToBinary + keys, no MAC.</summary>
        [Test]
        public void LegacyFile_IsReadOnce_AndUpgraded()
        {
            var key = ContentKey.Generate();
            var at = new DateTime(2026, 10, 20, 8, 30, 0, DateTimeKind.Utc);
            var plain = BitConverter.GetBytes(at.ToBinary()).Concat(key.Bytes).ToArray();
            ContentKey legacyKey;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                legacyKey = ContentKey.FromBytes(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("device-a")));
            var legacyFile = ContentEncryptor.Encrypt(plain, legacyKey);
            File.WriteAllBytes(_path, legacyFile);

            Assert.IsTrue(Cache(_path).TryLoad(out var loaded, out var loadedAt, out var ttl));
            Assert.AreEqual(key.Id, loaded.Current.Id);
            Assert.AreEqual(at, loadedAt);
            Assert.AreEqual(TimeSpan.Zero, ttl);

            CollectionAssert.AreNotEqual(legacyFile, File.ReadAllBytes(_path), "the file should have been rewritten in the current format");
            Assert.IsTrue(Cache(_path).TryLoad(out loaded, out _));
            Assert.AreEqual(key.Id, loaded.Current.Id);
        }
    }
}
