using System;
using System.IO;

namespace GameProtection.Content
{
    /// <summary>
    /// Encrypted container layout (32-byte header, then AES-256-CTR ciphertext of the original bytes):
    ///   0  magic "GPKC"
    ///   4  version (1)
    ///   5  reserved (3)
    ///   8  key id (8, big-endian) — lets the loader fail fast with a clear error on the wrong key
    ///  16  nonce (16)
    /// CTR mode keeps the payload the same length as the plaintext and makes random access possible,
    /// which is what AssetBundle.LoadFromStream needs.
    /// </summary>
    public static class ContentFormat
    {
        public const int HeaderSize = 32;
        public const int NonceSize = 16;
        public const byte Version = 1;
        public static readonly byte[] Magic = { (byte)'G', (byte)'P', (byte)'K', (byte)'C' };

        public struct Header
        {
            public byte Version;
            public ulong KeyId;
            public byte[] Nonce;
        }

        public static bool HasMagic(byte[] bytes, int offset = 0)
        {
            if (bytes == null || bytes.Length < offset + Magic.Length) return false;
            for (var i = 0; i < Magic.Length; i++)
                if (bytes[offset + i] != Magic[i]) return false;
            return true;
        }

        /// <summary>Reads the header at the stream's current position (normally 0). Returns false when not our format.</summary>
        public static bool TryReadHeader(Stream stream, out Header header)
        {
            header = default;
            var bytes = new byte[HeaderSize];
            if (ReadFully(stream, bytes) < HeaderSize || !HasMagic(bytes)) return false;

            header.Version = bytes[4];
            ulong keyId = 0;
            for (var i = 8; i < 16; i++) keyId = (keyId << 8) | bytes[i];
            header.KeyId = keyId;
            header.Nonce = new byte[NonceSize];
            Buffer.BlockCopy(bytes, 16, header.Nonce, 0, NonceSize);
            return true;
        }

        public static bool TryReadHeader(byte[] bytes, out Header header)
        {
            header = default;
            if (bytes == null || bytes.Length < HeaderSize) return false;
            using (var ms = new MemoryStream(bytes, 0, HeaderSize, false))
                return TryReadHeader(ms, out header);
        }

        public static byte[] WriteHeader(ulong keyId, byte[] nonce)
        {
            if (nonce == null || nonce.Length != NonceSize) throw new ArgumentException("Nonce must be 16 bytes.", nameof(nonce));
            var bytes = new byte[HeaderSize];
            Buffer.BlockCopy(Magic, 0, bytes, 0, Magic.Length);
            bytes[4] = Version;
            for (var i = 0; i < 8; i++) bytes[8 + i] = (byte)(keyId >> (8 * (7 - i)));
            Buffer.BlockCopy(nonce, 0, bytes, 16, NonceSize);
            return bytes;
        }

        /// <summary>True when the file starts with our magic. Cheap: reads 4 bytes.</summary>
        public static bool IsEncryptedFile(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var magic = new byte[Magic.Length];
                    return ReadFully(stream, magic) == magic.Length && HasMagic(magic);
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static int ReadFully(Stream stream, byte[] buffer) => ReadFully(stream, buffer, 0, buffer.Length);

        internal static int ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            var total = 0;
            while (total < count)
            {
                var read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }
    }
}
