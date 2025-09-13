using System.Security.Cryptography;
using System.Text;

namespace Appostolic.Api.App.Notifications;

public interface IFieldCipher
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

public sealed class NullFieldCipher : IFieldCipher
{
    public string Encrypt(string plaintext) => plaintext;
    public string Decrypt(string ciphertext) => ciphertext;
}

// AES-GCM with prefix header to identify encrypted payloads; payload format (base64url): nonce(12) | tag(16) | ciphertext(N)
public sealed class AesGcmFieldCipher : IFieldCipher
{
    private static readonly string Prefix = "enc:v1:";
    private readonly byte[] _key;

    public AesGcmFieldCipher(byte[] key)
    {
        if (key is null || (key.Length != 16 && key.Length != 24 && key.Length != 32))
            throw new ArgumentException("AES-GCM key must be 16/24/32 bytes", nameof(key));
        _key = key;
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        using var aes = new AesGcm(_key);
        Span<byte> nonce = stackalloc byte[12];
        RandomNumberGenerator.Fill(nonce);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        Span<byte> tag = stackalloc byte[16];
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce.ToArray(), 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag.ToArray(), 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        var b64 = Base64UrlEncode(payload);
        return Prefix + b64;
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal)) return ciphertext;
        var b64 = ciphertext.Substring(Prefix.Length);
        var payload = Base64UrlDecode(b64);
        if (payload.Length < 12 + 16)
            throw new CryptographicException("Invalid encrypted payload length");
        var nonce = payload.AsSpan(0, 12).ToArray();
        var tag = payload.AsSpan(12, 16).ToArray();
        var cipher = payload.AsSpan(28).ToArray();
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        string padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
