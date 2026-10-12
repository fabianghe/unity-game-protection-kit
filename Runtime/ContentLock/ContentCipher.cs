using System;
using System.Security.Cryptography;

namespace GameProtection.Content
{
    /// <summary>
    /// AES-256 in CTR mode, built from the framework's AES-ECB primitive (CTR isn't offered directly by .NET Standard).
    /// Encryption and decryption are the same operation: XOR the data with the keystream for its position.
    /// Keystream block i = AES_k(nonce + i), with the nonce treated as a 128-bit big-endian counter.
    /// </summary>
    public sealed class ContentCipher : IDisposable
    {
        const int BlockSize = 16;
        const int BatchBlocks = 2048; // 32 KB of keystream per AES call

        readonly byte[] _nonce;
        readonly ICryptoTransform _ecb;
        readonly byte[] _counters = new byte[BatchBlocks * BlockSize];
        readonly byte[] _keystream = new byte[BatchBlocks * BlockSize];
        bool _disposed;

        public ContentCipher(ContentKey key, byte[] nonce)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (nonce == null || nonce.Length != BlockSize) throw new ArgumentException("Nonce must be 16 bytes.", nameof(nonce));

            _nonce = (byte[])nonce.Clone();
            var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.KeySize = 256;
            aes.Key = key.Bytes;
            _ecb = aes.CreateEncryptor(key.Bytes, new byte[BlockSize]);
            aes.Dispose();
        }

        /// <summary>XORs buffer[offset..offset+count) with the keystream that belongs at plaintext position `position`.</summary>
        public void Apply(byte[] buffer, int offset, int count, long position)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ContentCipher));
            if (count <= 0) return;

            var firstBlock = position / BlockSize;
            var intraOffset = (int)(position % BlockSize);
            var done = 0;

            while (done < count)
            {
                // Blocks needed to cover the remaining bytes, starting at the block that contains the current position.
                var remaining = count - done;
                var blocksNeeded = (intraOffset + remaining + BlockSize - 1) / BlockSize;
                var blocks = Math.Min(blocksNeeded, BatchBlocks);

                for (var b = 0; b < blocks; b++) FillCounter(firstBlock + b, b * BlockSize);
                _ecb.TransformBlock(_counters, 0, blocks * BlockSize, _keystream, 0);

                var available = blocks * BlockSize - intraOffset;
                var take = Math.Min(available, remaining);
                var ks = intraOffset;
                for (var i = 0; i < take; i++) buffer[offset + done + i] ^= _keystream[ks + i];

                done += take;
                firstBlock += blocks;
                intraOffset = 0;
            }
        }

        void FillCounter(long blockIndex, int dest)
        {
            Buffer.BlockCopy(_nonce, 0, _counters, dest, BlockSize);
            var carry = (ulong)blockIndex;
            for (var b = BlockSize - 1; b >= 0 && carry != 0; b--)
            {
                var sum = _counters[dest + b] + (carry & 0xFF);
                _counters[dest + b] = (byte)sum;
                carry = (carry >> 8) + (sum >> 8);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ecb.Dispose();
            Array.Clear(_keystream, 0, _keystream.Length);
        }
    }
}
