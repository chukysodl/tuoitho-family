using System.Security.Cryptography;

namespace TuoiTho.Core.Security;

public sealed record ParentPasswordRecord(
    int Version,
    int Iterations,
    string SaltBase64,
    string HashBase64)
{
    public const int CurrentVersion = 1;
    public const int DefaultIterations = 210_000;
}

public static class ParentPasswordHasher
{
    private const int SaltLength = 32;
    private const int HashLength = 32;

    public static ParentPasswordRecord Create(string password, int iterations = ParentPasswordRecord.DefaultIterations)
    {
        ValidatePassword(password);
        if (iterations < 100_000) throw new ArgumentOutOfRangeException(nameof(iterations));

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashLength);

        return new ParentPasswordRecord(
            ParentPasswordRecord.CurrentVersion,
            iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, ParentPasswordRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Version != ParentPasswordRecord.CurrentVersion ||
            record.Iterations < 100_000 ||
            string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(record.SaltBase64);
            var expected = Convert.FromBase64String(record.HashBase64);
            if (salt.Length != SaltLength || expected.Length != HashLength) return false;

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                record.Iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new ArgumentException("Mật khẩu phụ huynh phải có ít nhất 6 ký tự.", nameof(password));

        if (password.Length > 256)
            throw new ArgumentException("Mật khẩu phụ huynh quá dài.", nameof(password));
    }
}
