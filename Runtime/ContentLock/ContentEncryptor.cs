using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameProtection.Content
{
    /// <summary>Encrypts and decrypts whole files and byte arrays in the container format. Pure C#, usable in editor and CI.</summary>
    public static class ContentEncryptor
    {
        const int ChunkSize = 1024 * 1024;

        public static byte[] Encrypt(byte[] plain, ContentKey key)
        {
            if (plain == null) throw new ArgumentNullException(nameof(plain));
            if (key == null) throw new ArgumentNullException(nameof(key));

            byte[] nonce;
            using (var mac = NonceMac(key)) nonce = ToNonce(mac.ComputeHash(plain));

            var output = new byte[ContentFormat.HeaderSize + plain.Length];
            Buffer.BlockCopy(ContentFormat.WriteHeader(key.Id, nonce), 0, output, 0, ContentFormat.HeaderSize);
            Buffer.BlockCopy(plain, 0, output, ContentFormat.HeaderSize, plain.Length);
            using (var cipher = new ContentCipher(key, nonce))
                cipher.Apply(output, ContentFormat.HeaderSize, plain.Length, 0);
            return output;
        }

        public static byte[] Decrypt(byte[] container, ContentKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!ContentFormat.TryReadHeader(container, out var header))
                throw new InvalidDataException("Not a Game Protection encrypted container.");
            if (header.Version != ContentFormat.Version)
                throw new InvalidDataException("Unsupported container version " + header.Version + ".");
            if (header.KeyId != key.Id) throw new ContentKeyMismatchException(header.KeyId, key.Id);

            var plain = new byte[container.Length - ContentFormat.HeaderSize];
            Buffer.BlockCopy(container, ContentFormat.HeaderSize, plain, 0, plain.Length);
            using (var cipher = new ContentCipher(key, header.Nonce))
                cipher.Apply(plain, 0, plain.Length, 0);
            return plain;
        }

        /// <summary>
        /// Encrypts a file in place (via a temp file, so a crash never leaves a half-written bundle).
        /// Returns false when the file is already encrypted, which makes repeated build hooks safe.
        /// The same file and key always produce the same bytes, so a bundle that didn't change between two builds
        /// doesn't change on disk either and store patches (Steam, CDNs) stay small.
        /// </summary>
        public static bool EncryptFile(string path, ContentKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (ContentFormat.IsEncryptedFile(path)) return false;

            var temp = path + ".gpktmp";
            using (var input = File.OpenRead(path))
            {
                byte[] nonce;
                using (var mac = NonceMac(key)) nonce = ToNonce(mac.ComputeHash(input));
                input.Position = 0;

                using (var output = File.Create(temp))
                using (var cipher = new ContentCipher(key, nonce))
                {
                    var header = ContentFormat.WriteHeader(key.Id, nonce);
                    output.Write(header, 0, header.Length);

                    var buffer = new byte[ChunkSize];
                    long position = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cipher.Apply(buffer, 0, read, position);
                        output.Write(buffer, 0, read);
                        position += read;
                    }
                }
            }

            File.Delete(path);
            File.Move(temp, path);
            return true;
        }

        /// <summary>Decrypts a file in place. Returns false when the file isn't encrypted.</summary>
        public static bool DecryptFile(string path, ContentKey key)
        {
            if (!ContentFormat.IsEncryptedFile(path)) return false;

            var temp = path + ".gpktmp";
            using (var input = DecryptingStream.Open(File.OpenRead(path), key))
            using (var output = File.Create(temp))
                input.CopyTo(output, ChunkSize);

            File.Delete(path);
            File.Move(temp, path);
            return true;
        }

        // The nonce is a keyed hash of the plaintext instead of a random number. CTR mode only needs the nonce to be
        // different for different data, and this gives that plus repeatable output: random nonces would rewrite every
        // bundle on every build, and players would re-download the whole game with each patch. Without the key the
        // nonce says nothing about the content.
        static HMACSHA256 NonceMac(ContentKey key)
        {
            // Derived key, so the AES key itself is only ever used for AES.
            var label = Encoding.ASCII.GetBytes("GameProtection.Content.nonce.v1");
            var material = new byte[label.Length + key.Bytes.Length];
            Buffer.BlockCopy(label, 0, material, 0, label.Length);
            Buffer.BlockCopy(key.Bytes, 0, material, label.Length, key.Bytes.Length);
            using (var sha = SHA256.Create()) return new HMACSHA256(sha.ComputeHash(material));
        }

        static byte[] ToNonce(byte[] hash)
        {
            var nonce = new byte[ContentFormat.NonceSize];
            Buffer.BlockCopy(hash, 0, nonce, 0, nonce.Length);
            return nonce;
        }
    }
}
