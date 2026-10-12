using System;
using System.IO;

namespace GameProtection.Content
{
    /// <summary>
    /// Read-only, seekable view of the plaintext inside an encrypted container. Decrypts on the fly, so a large
    /// AssetBundle is never fully decrypted in memory. Hand it to AssetBundle.LoadFromStream(Async).
    /// </summary>
    public sealed class DecryptingStream : Stream
    {
        readonly Stream _source;
        readonly ContentCipher _cipher;
        readonly long _payloadStart;
        readonly long _length;
        readonly bool _ownsSource;
        long _position;

        /// <summary>Opens the plaintext view. The source must be positioned at the start of the container.</summary>
        public static DecryptingStream Open(Stream source, ContentKey key, bool ownsSource = true)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!source.CanRead || !source.CanSeek) throw new ArgumentException("Source stream must be readable and seekable.", nameof(source));

            var start = source.Position;
            if (!ContentFormat.TryReadHeader(source, out var header))
                throw new InvalidDataException("Not a Game Protection encrypted container.");
            if (header.Version != ContentFormat.Version)
                throw new InvalidDataException("Unsupported container version " + header.Version + ".");
            if (header.KeyId != key.Id)
                throw new ContentKeyMismatchException(header.KeyId, key.Id);

            return new DecryptingStream(source, new ContentCipher(key, header.Nonce), start + ContentFormat.HeaderSize, ownsSource);
        }

        DecryptingStream(Stream source, ContentCipher cipher, long payloadStart, bool ownsSource)
        {
            _source = source;
            _cipher = cipher;
            _payloadStart = payloadStart;
            _length = source.Length - payloadStart;
            _ownsSource = ownsSource;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();
            if (_position >= _length || count == 0) return 0;

            var toRead = (int)Math.Min(count, _length - _position);
            if (_source.Position != _payloadStart + _position) _source.Seek(_payloadStart + _position, SeekOrigin.Begin);

            var read = ContentFormat.ReadFully(_source, buffer, offset, toRead);
            if (read > 0) _cipher.Apply(buffer, offset, read, _position);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target;
            switch (origin)
            {
                case SeekOrigin.Begin: target = offset; break;
                case SeekOrigin.Current: target = _position + offset; break;
                case SeekOrigin.End: target = _length + offset; break;
                default: throw new ArgumentOutOfRangeException(nameof(origin));
            }
            if (target < 0) throw new IOException("Cannot seek before the beginning of the stream.");
            _position = target;
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cipher.Dispose();
                if (_ownsSource) _source.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
