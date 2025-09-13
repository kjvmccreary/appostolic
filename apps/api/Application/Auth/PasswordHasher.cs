using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Appostolic.Api.Application.Auth;

public interface IPasswordHasher
{
    (byte[] hash, byte[] salt, int iterations) HashPassword(string password, int? iterations = null);
    bool Verify(string password, byte[] hash, byte[] salt, int iterations);
}

public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 32; // 256-bit
    private const int KeySize = 32;  // 256-bit
    private const int DefaultTimeCost = 3;    // iterations
    private const int MemorySizeKb = 64 * 1024; // 64 MiB
    private const int Parallelism = 2;
    private readonly string _pepper;

    public Argon2PasswordHasher(IConfiguration configuration)
    {
        _pepper = configuration["Auth:PasswordPepper"] ?? string.Empty; // optional; recommended in prod
    }

    public (byte[] hash, byte[] salt, int iterations) HashPassword(string password, int? iterations = null)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password is required", nameof(password));
        int timeCost = iterations.GetValueOrDefault(DefaultTimeCost);
        Span<byte> salt = stackalloc byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        var argon = new Argon2id(Encoding.UTF8.GetBytes(password + _pepper))
        {
            Salt = salt.ToArray(),
            Iterations = timeCost,
            DegreeOfParallelism = Parallelism,
            MemorySize = MemorySizeKb
        };
        byte[] hash = argon.GetBytes(KeySize);
        return (hash, salt.ToArray(), timeCost);
    }

    public bool Verify(string password, byte[] hash, byte[] salt, int iterations)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;
        var argon = new Argon2id(Encoding.UTF8.GetBytes(password + _pepper))
        {
            Salt = salt,
            Iterations = iterations <= 0 ? DefaultTimeCost : iterations,
            DegreeOfParallelism = Parallelism,
            MemorySize = MemorySizeKb
        };
        var computed = argon.GetBytes(hash.Length);
        return CryptographicOperations.FixedTimeEquals(hash, computed);
    }
}
