using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WorkReport.Data;
using WorkReport.Data.Models;

namespace WorkReport.Services;

public class AuthService(IDbContextFactory<AppDbContext> dbFactory, ILogger<AuthService> logger)
{
    /// <summary>验证用户名密码，成功返回 User，失败返回 null。</summary>
    public async Task<User?> ValidateAsync(string username, string password)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null) return null;
            return VerifyPassword(password, user.PasswordHash) ? user : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "登录验证失败 Username={Username}", username);
            return null;
        }
    }

    /// <summary>注册新用户。用户名已存在返回 null，成功返回 User。</summary>
    public async Task<User?> RegisterAsync(string username, string password)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            if (await db.Users.AnyAsync(u => u.Username == username))
                return null;

            var user = new User
            {
                Username = username,
                PasswordHash = HashPassword(password),
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "注册失败 Username={Username}", username);
            return null;
        }
    }

    /// <summary>检查是否存在任何用户（用于判断是否需要种子管理员）。</summary>
    public async Task<bool> AnyUserAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.AnyAsync();
    }

    /// <summary>获取当前登录用户。</summary>
    public async Task<User?> GetUserAsync(string username)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    }

    /// <summary>更新用户 Profile 信息。</summary>
    public async Task<bool> UpdateProfileAsync(string username, string? displayName, string? department, string? avatarBase64)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null) return false;
            user.DisplayName = displayName;
            user.Department = department;
            user.AvatarBase64 = avatarBase64;
            await db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新 Profile 失败 Username={Username}", username);
            return false;
        }
    }

    /// <summary>修改密码。成功返回 true，旧密码错误返回 false。</summary>
    public async Task<(bool ok, string message)> ChangePasswordAsync(string username, string oldPassword, string newPassword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
                return (false, "新密码至少 4 个字符");

            await using var db = await dbFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null) return (false, "用户不存在");

            if (!VerifyPassword(oldPassword, user.PasswordHash))
                return (false, "旧密码错误");

            user.PasswordHash = HashPassword(newPassword);
            user.MustChangePassword = false; // 清除强制修改密码标记
            await db.SaveChangesAsync();
            return (true, "密码修改成功");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "修改密码失败 Username={Username}", username);
            return (false, "修改失败，请重试");
        }
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var hash = Convert.FromBase64String(parts[1]);
        var test = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, test);
    }
}
