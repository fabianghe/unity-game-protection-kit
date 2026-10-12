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
    }
}
