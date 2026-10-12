using System;
using System.IO;
using System.Security.Cryptography;

namespace GameProtection.Content
{
    /// <summary>Encrypts and decrypts whole files and byte arrays in the container format. Pure C#, usable in editor and CI.</summary>
    public static class ContentEncryptor
    {
        const int ChunkSize = 1024 * 1024;

        public static byte[] Encrypt(byte[] plain, ContentKey key)
        {
            if (plain == null) throw new ArgumentNullException(nameof(plain));
            var nonce = NewNonce();
            var output = new byte[ContentFormat.HeaderSize + plain.Length];
            Buffer.BlockCopy(ContentFormat.WriteHeader(key.Id, nonce), 0, output, 0, ContentFormat.HeaderSize);
            Buffer.BlockCopy(plain, 0, output, ContentFormat.HeaderSize, plain.Length);
            using (var cipher = new ContentCipher(key, nonce))
                cipher.Apply(output, ContentFormat.HeaderSize, plain.Length, 0);
            return output;
        }

        public static byte[] Decrypt(byte[] container, ContentKey key)
        {
            if (!ContentFormat.TryReadHeader(container, out var header))
                throw new InvalidDataException("Not a Game Protection encrypted container.");
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
        /// </summary>
        public static bool EncryptFile(string path, ContentKey key)
        {
            if (ContentFormat.IsEncryptedFile(path)) return false;

            var temp = path + ".gpktmp";
            var nonce = NewNonce();
            using (var input = File.OpenRead(path))
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

        static byte[] NewNonce()
        {
            var nonce = new byte[ContentFormat.NonceSize];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
            return nonce;
        }
    }
}
