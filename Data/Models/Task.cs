using System.ComponentModel.DataAnnotations;

namespace WorkReport.Data.Models;

/// <summary>
/// 工作任务（Tasks 表）。类名用 WorkTask 以避免与 System.Threading.Tasks.Task 冲突。
/// </summary>
public class WorkTask
{
    public int Id { get; set; }

    /// <summary>所属半月期，格式 yyyy-MM-dd~MM-dd，如 2026-06-01~06-15</summary>
    [Required]
    public string Period { get; set; } = string.Empty;

    /// <summary>类型：重点工作 / 日常工作</summary>
    [Required]
    public string Type { get; set; } = TaskTypes.Daily;

    /// <summary>工作说明</summary>
    [Required(ErrorMessage = "请填写工作说明")]
    public string Title { get; set; } = string.Empty;

    /// <summary>完成效果：完成 / 正在进行中 / 待测 / 待确认 / 空</summary>
    public string? Result { get; set; }

    /// <summary>解决时间（自由文本，可为空）</summary>
    public string? ResolveDate { get; set; }

    /// <summary>备注说明（可为空）</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>软删除标记</summary>
    public bool IsDeleted { get; set; }

    /// <summary>删除时间</summary>
    public DateTime? DeletedAt { get; set; }
}

public static class TaskTypes
{
    public const string Key = "重点工作";
    public const string Daily = "日常工作";

    public static readonly string[] All = [Key, Daily];
}

public static class TaskResults
{
    public static readonly string[] All = ["完成", "正在进行中", "待测", "待确认"];
}
