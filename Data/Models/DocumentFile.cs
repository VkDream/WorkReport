using System.ComponentModel.DataAnnotations;

namespace WorkReport.Data.Models;

public class DocumentFile
{
    public int Id { get; set; }

    [Required]
    [MaxLength(80)]
    public string Category { get; set; } = "未分类";

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long FileSize { get; set; }

    public byte[] Content { get; set; } = [];

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>关联的任务 ID（null 表示公共文档）。</summary>
    public int? TaskId { get; set; }

    /// <summary>导航属性：关联的任务。</summary>
    public WorkTask? Task { get; set; }
}
