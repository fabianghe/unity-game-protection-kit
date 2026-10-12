using System;
using System.Security.Cryptography;

namespace GameProtection.Content
{
    /// <summary>A 256-bit content key plus a short public id derived from it (first 8 bytes of SHA-256).</summary>
    public sealed class ContentKey
    {
        public const int SizeBytes = 32;

        public byte[] Bytes { get; }
        public ulong Id { get; }

        ContentKey(byte[] bytes)
        {
            Bytes = bytes;
            Id = ComputeId(bytes);
        }

        public static ContentKey FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length != SizeBytes)
                throw new ArgumentException($"Content key must be {SizeBytes} bytes.", nameof(bytes));
            var copy = new byte[SizeBytes];
            Buffer.BlockCopy(bytes, 0, copy, 0, SizeBytes);
            return new ContentKey(copy);
        }

        public static ContentKey FromBase64(string base64) => FromBytes(Convert.FromBase64String((base64 ?? string.Empty).Trim()));

        public static ContentKey Generate()
        {
            var bytes = new byte[SizeBytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return new ContentKey(bytes);
        }

        public string ToBase64() => Convert.ToBase64String(Bytes);

        public static ulong ComputeId(byte[] key)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(key);
                ulong id = 0;
                for (var i = 0; i < 8; i++) id = (id << 8) | hash[i];
                return id;
            }
        }

        /// <summary>Short printable form of the key id, for logs and the editor.</summary>
        public static string FormatId(ulong id) => id.ToString("x16");
    }

    public sealed class ContentKeyMismatchException : Exception
    {
        public ulong ExpectedKeyId { get; }
        public ulong ActualKeyId { get; }

        public ContentKeyMismatchException(ulong expected, ulong actual)
            : base($"Content was encrypted with key {ContentKey.FormatId(expected)} but key {ContentKey.FormatId(actual)} was provided.")
        {
            ExpectedKeyId = expected;
            ActualKeyId = actual;
        }
    }
}
