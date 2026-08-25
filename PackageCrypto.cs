using System;
using System.IO;
using System.Security.Cryptography;

namespace ProfessionalPowerCopyCatalogModern
{
    // Mirrors AdminPowerCopyConsole.PackageCrypto. Container layout:
    //   [4 bytes] "PCPK" magic
    //   [1 byte]  version = 1
    //   [16 bytes] AES IV
    //   [rest]   AES-256-CBC ciphertext (PKCS7 padding)
    // Key is 32 bytes represented as 64-char hex (matches server storage).
    public static class PackageCrypto
    {
        private static readonly byte[] Magic = { (byte)'P', (byte)'C', (byte)'P', (byte)'K' };
        private const int IvLength = 16;
        private const int KeyBytes = 32;

        public static bool IsEncryptedContainer(byte[] data)
        {
            if (data == null || data.Length < Magic.Length + 1 + IvLength) return false;
            for (int i = 0; i < Magic.Length; i++)
                if (data[i] != Magic[i]) return false;
            return true;
        }

        public static byte[] Decrypt(byte[] container, string keyHex)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (container.Length < Magic.Length + 1 + IvLength)
                throw new InvalidDataException("Invalid encrypted package (too short).");
            for (int i = 0; i < Magic.Length; i++)
                if (container[i] != Magic[i])
                    throw new InvalidDataException("Invalid encrypted package (bad magic).");
            if (container[Magic.Length] != 1)
                throw new InvalidDataException("Unsupported encrypted package version.");

            byte[] key = HexToBytes(keyHex);
            if (key.Length != KeyBytes) throw new ArgumentException("Package key must be 32 bytes.");

            int ivOffset = Magic.Length + 1;
            byte[] iv = new byte[IvLength];
            Array.Copy(container, ivOffset, iv, 0, IvLength);
            byte[] cipher = new byte[container.Length - ivOffset - IvLength];
            Array.Copy(container, ivOffset + IvLength, cipher, 0, cipher.Length);

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform dec = aes.CreateDecryptor())
                    return dec.TransformFinalBlock(cipher, 0, cipher.Length);
            }
        }

        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new byte[0];
            if (hex.Length % 2 != 0) throw new ArgumentException("Invalid hex length.");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }
}
