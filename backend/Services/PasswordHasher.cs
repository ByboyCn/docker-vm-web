using System.Security.Cryptography;
using System.Text;

namespace DockerVm.Services;

/// <summary>
/// PBKDF2-SHA256 密码哈希。100000 次迭代,16 字节 salt,32 字节输出。
/// </summary>
public static class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Alg = HashAlgorithmName.SHA256;

    /// <summary>生成哈希与 salt(均返回 Base64)。</summary>
    public static (string hash, string salt) Hash(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltBytes);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), saltBytes, Iterations, Alg, HashBytes);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    /// <summary>用常量时间比较验证密码。</summary>
    public static bool Verify(string password, string storedHash, string storedSalt)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
        {
            return false;
        }

        byte[] saltBytes;
        byte[] expected;
        try
        {
            saltBytes = Convert.FromBase64String(storedSalt);
            expected = Convert.FromBase64String(storedHash);
        }
        catch
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), saltBytes, Iterations, Alg, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
