using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public class EmailEncryption
{
    // Define the password used for encryption/decryption
    private const string Password = "StrongPassword123"; // Same as SQL Server

    // Using a fixed salt for simplicity. In production, generate this securely.
    private static readonly byte[] Salt = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 }; // Example static salt

    private static readonly string EncryptionKey = "0123456789abcdef0123456789abcdef"; // 32 chars = 32 bytes
    private static readonly string IV = "abcdef9876543210"; // 16 chars = 16 bytes


    /// <summary>
    /// Encrypts the given email using AES encryption.
    /// </summary>
    /// <param name="email">The email to encrypt.</param>
    /// <returns>A byte array containing the IV and the encrypted email.</returns>
    public static string EncryptEmail(string email)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = DeriveKey(Password, Salt);
            aes.GenerateIV(); // Generate a new IV for this encryption

            using (var ms = new MemoryStream())
            {
                ms.Write(aes.IV, 0, aes.IV.Length); // Store IV at the beginning

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                using (var writer = new StreamWriter(cs, Encoding.UTF8))
                {
                    writer.Write(email);
                    writer.Flush();
                    cs.FlushFinalBlock();
                }

                return BitConverter.ToString(ms.ToArray()).Replace("-", ""); // Convert bytes to hex
            }
        }
    }


    //public static byte[] EncryptEmail(string email)
    //{
    //    using (Aes aes = Aes.Create())
    //    {
    //        // Derive the key from the password and salt
    //        aes.Key = DeriveKey(Password, Salt);
    //        aes.GenerateIV(); // Generate a new IV for this encryption

    //        using (var ms = new MemoryStream())
    //        {
    //            // Write the IV to the memory stream
    //            ms.Write(aes.IV, 0, aes.IV.Length);
    //            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
    //            {
    //                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
    //                {
    //                    using (var writer = new StreamWriter(cs, Encoding.UTF8)) // Ensure encoding is UTF-8
    //                    {
    //                        writer.Write(email);
    //                    }
    //                }
    //            }
    //            // Return the combined IV and encrypted email as a byte array
    //            return ms.ToArray();
    //        }
    //    }
    //}

    /// <summary>
    /// Decrypts the given encrypted email byte array back into a string.
    /// </summary>
    /// <param name="encryptedEmail">The byte array containing the IV and encrypted email.</param>
    /// <returns>The decrypted email as a string.</returns>
    //public static string DecryptEmail(byte[] encryptedEmail)
    //{
    //    using (Aes aes = Aes.Create())
    //    {
    //        // Extract the IV from the beginning of the encryptedEmail
    //        byte[] iv = new byte[aes.BlockSize / 8];
    //        Array.Copy(encryptedEmail, iv, iv.Length);

    //        // Derive the key for decryption
    //        aes.Key = DeriveKey(Password, Salt);
    //        aes.IV = iv;

    //        using (var ms = new MemoryStream(encryptedEmail, iv.Length, encryptedEmail.Length - iv.Length))
    //        {
    //            using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
    //            {
    //                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
    //                {
    //                    using (var reader = new StreamReader(cs))
    //                    {
    //                        return reader.ReadToEnd();
    //                    }
    //                }
    //            }
    //        }
    //    }
    //}

    public static string DecryptEmail(byte[] encryptedEmail)
    {
        try
        {
            using (Aes aes = Aes.Create())
            {
                // Extract the IV from the beginning of the encryptedEmail
                byte[] iv = new byte[aes.BlockSize / 8]; // 16 bytes for AES-128 or AES-256
                Array.Copy(encryptedEmail, iv, iv.Length);

                // Derive the key for decryption using the same method used during encryption
                aes.Key = DeriveKey(Password, Salt); // Derive the key from the password and salt
                aes.IV = iv; // Set the IV

                using (var ms = new MemoryStream(encryptedEmail, iv.Length, encryptedEmail.Length - iv.Length))
                {
                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                        {
                            using (var reader = new StreamReader(cs))
                            {
                                // Decrypt the email and return it as a string
                                return reader.ReadToEnd();
                            }
                        }
                    }
                }
            }
        }
        catch (CryptographicException ex)
        {
            Console.WriteLine($"Decryption Error: {ex.Message}");
            return "[DECRYPTION FAILED]"; // Return a meaningful message when decryption fails
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected Error: {ex.Message}");
            return "[ERROR]"; // Return an error message if something else goes wrong
        }
    }




    public static byte[] StringToByteArray(string hex)
    {
        if (hex.Length % 2 != 0)
            throw new ArgumentException("Invalid hex string length");

        return Enumerable.Range(0, hex.Length / 2)
                         .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16))
                         .ToArray();
    }

    /// <summary>
    /// Derives a key from the password and salt using PBKDF2.
    /// </summary>
    /// <param name="password">The password used for key derivation.</param>
    /// <param name="salt">The salt used for key derivation.</param>
    /// <returns>A 256-bit key for AES encryption.</returns>
    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using (var rfc2898 = new Rfc2898DeriveBytes(password, salt, 10000))
        {
            return rfc2898.GetBytes(32); // 32 bytes for AES-256
        }
    }

    /// <summary>
    /// Converts a byte array to a hexadecimal string.
    /// </summary>
    /// <param name="bytes">The byte array to convert.</param>
    /// <returns>A hexadecimal string representation of the byte array.</returns>
    public static string ByteArrayToHexString(byte[] bytes)
    {
        StringBuilder hexString = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            hexString.AppendFormat("{0:X2}", b);
        }
        return hexString.ToString();
    }
    /// <summary>
    /// Converts a hexadecimal string to a byte array.
    /// </summary>
    /// <param name="hex">The hexadecimal string to convert.</param>
    /// <returns>A byte array representation of the hexadecimal string.</returns>
    public static byte[] ConvertHexStringToByteArray(string hex)
    {
        int length = hex.Length;
        byte[] bytes = new byte[length / 2];

        for (int i = 0; i < length; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }

        return bytes;
    }
}