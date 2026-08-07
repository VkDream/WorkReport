using System.ComponentModel.DataAnnotations;

namespace WorkReport.Data.Models;

public class User
{
    public int Id { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>显示名称（可自定义）</summary>
    public string? DisplayName { get; set; }

    /// <summary>所属部门</summary>
    public string? Department { get; set; }

    /// <summary>头像 Base64 编码（data:image/... 格式）</summary>
    public string? AvatarBase64 { get; set; }

    /// <summary>是否必须修改密码（首次登录默认账号时为 true）</summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
