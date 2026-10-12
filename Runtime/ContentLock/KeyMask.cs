using System;
using System.Security.Cryptography;
using System.Text;

namespace GameProtection.Content
{
    /// <summary>
    /// XOR mask for key blobs embedded in a build (Local mode), derived from the product and company name.
    /// Symmetric: the same call masks and unmasks. Pure C# so the editor, the runtime and tests share one implementation.
    /// </summary>
    public static class KeyMask
    {
        const string Domain = "GameProtection.Content.v1|";

        public static byte[] Apply(byte[] blob, string productName, string companyName)
        {
            if (blob == null) return null;
            var output = new byte[blob.Length];
            using (var sha = SHA256.Create())
            {
                var seed = Encoding.UTF8.GetBytes(Domain + productName + "|" + companyName);
                var block = sha.ComputeHash(seed);
                for (var i = 0; i < blob.Length; i++)
                {
                    if (i > 0 && i % block.Length == 0) block = sha.ComputeHash(Concat(block, seed));
                    output[i] = (byte)(blob[i] ^ block[i % block.Length]);
                }
            }
            return output;
        }

        static byte[] Concat(byte[] a, byte[] b)
        {
            var result = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, result, 0, a.Length);
            Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
            return result;
        }
    }
}
