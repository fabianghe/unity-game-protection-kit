using System;
using System.Collections.Generic;
using System.Linq;

namespace GameProtection.Content
{
    /// <summary>
    /// All content keys a build may need. The first key is the current one (used for new encryption); older keys
    /// stay so content encrypted before a rotation still loads.
    /// </summary>
    public sealed class KeyRing
    {
        readonly List<ContentKey> _keys = new List<ContentKey>();
        readonly Dictionary<ulong, ContentKey> _byId = new Dictionary<ulong, ContentKey>();

        public IReadOnlyList<ContentKey> Keys => _keys;
        public ContentKey Current => _keys.Count > 0 ? _keys[0] : null;
        public int Count => _keys.Count;

        public KeyRing() { }

        public KeyRing(IEnumerable<ContentKey> keys)
        {
            foreach (var key in keys ?? Enumerable.Empty<ContentKey>()) Add(key);
        }

        public void Add(ContentKey key)
        {
            if (key == null || _byId.ContainsKey(key.Id)) return;
            _keys.Add(key);
            _byId[key.Id] = key;
        }

        public bool TryGet(ulong keyId, out ContentKey key) => _byId.TryGetValue(keyId, out key);

        /// <summary>Serializes as consecutive 32-byte keys, current first.</summary>
        public byte[] ToBlob()
        {
            var blob = new byte[_keys.Count * ContentKey.SizeBytes];
            for (var i = 0; i < _keys.Count; i++)
                Buffer.BlockCopy(_keys[i].Bytes, 0, blob, i * ContentKey.SizeBytes, ContentKey.SizeBytes);
            return blob;
        }

        public static KeyRing FromBlob(byte[] blob)
        {
            var ring = new KeyRing();
            if (blob == null) return ring;
            for (var offset = 0; offset + ContentKey.SizeBytes <= blob.Length; offset += ContentKey.SizeBytes)
            {
                var bytes = new byte[ContentKey.SizeBytes];
                Buffer.BlockCopy(blob, offset, bytes, 0, ContentKey.SizeBytes);
                ring.Add(ContentKey.FromBytes(bytes));
            }
            return ring;
        }

        public static KeyRing FromBase64List(IEnumerable<string> base64Keys) =>
            new KeyRing((base64Keys ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(ContentKey.FromBase64));
    }
}
