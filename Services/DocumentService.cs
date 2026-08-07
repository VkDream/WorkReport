using Microsoft.EntityFrameworkCore;
using WorkReport.Data;
using WorkReport.Data.Models;

namespace WorkReport.Services;

public class DocumentService(IDbContextFactory<AppDbContext> dbFactory, ILogger<DocumentService> logger)
{
    public const long MaxFileSize = 20 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".png",
        ".jpg",
        ".jpeg",
        ".txt",
    };

    public static string AllowedExtensionText => string.Join(", ", AllowedExtensions.OrderBy(x => x));

    public async Task<List<DocumentListItem>> SearchAsync(string? category, string? keyword)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            IQueryable<DocumentFile> query = db.Documents.Where(d => !d.IsDeleted);

            if (!string.IsNullOrWhiteSpace(category))
            {
                var normalizedCategory = NormalizeCategory(category);
                query = query.Where(d => d.Category == normalizedCategory);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var normalizedKeyword = keyword.Trim();
                query = query.Where(d =>
                    d.Title.Contains(normalizedKeyword) ||
                    d.OriginalFileName.Contains(normalizedKeyword) ||
                    (d.Note != null && d.Note.Contains(normalizedKeyword)));
            }

            return await query
                .OrderBy(d => d.Category)
                .ThenByDescending(d => d.CreatedAt)
                .Select(d => new DocumentListItem
                {
                    Id = d.Id,
                    Category = d.Category,
                    Title = d.Title,
                    OriginalFileName = d.OriginalFileName,
                    ContentType = d.ContentType,
                    FileSize = d.FileSize,
                    Note = d.Note,
                    CreatedAt = d.CreatedAt,
                    UpdatedAt = d.UpdatedAt,
                    TaskId = d.TaskId,
                    TaskTitle = d.Task != null ? d.Task.Title : null,
                    TaskPeriod = d.Task != null ? d.Task.Period : null,
                    TaskIsDeleted = d.Task != null ? d.Task.IsDeleted : null,
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "查询文档失败 Category={Category} Keyword={Keyword}", category, keyword);
            throw;
        }
    }

    public async Task<List<DocumentListItem>> GetByTaskAsync(int taskId)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Documents
                .Where(d => d.TaskId == taskId && !d.IsDeleted)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DocumentListItem
                {
                    Id = d.Id,
                    Category = d.Category,
                    Title = d.Title,
                    OriginalFileName = d.OriginalFileName,
                    ContentType = d.ContentType,
                    FileSize = d.FileSize,
                    Note = d.Note,
                    CreatedAt = d.CreatedAt,
                    UpdatedAt = d.UpdatedAt,
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "查询任务附件失败 TaskId={TaskId}", taskId);
            throw;
        }
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Documents
                .Where(d => !d.IsDeleted)
                .Select(d => d.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "查询文档分类失败");
            throw;
        }
    }

    public async Task UploadAsync(DocumentUpload upload, Stream source)
    {
        await UploadCoreAsync(upload, source, taskId: null);
    }

    /// <summary>从任务编辑页上传附件。</summary>
    public async Task UploadForTaskAsync(int taskId, DocumentUpload upload, Stream source)
    {
        await UploadCoreAsync(upload, source, taskId);
    }

    private async Task UploadCoreAsync(DocumentUpload upload, Stream source, int? taskId)
    {
        try
        {
            ValidateUpload(upload);

            await using var db = await dbFactory.CreateDbContextAsync();
            if (taskId.HasValue && !await db.Tasks.AnyAsync(t => t.Id == taskId.Value && !t.IsDeleted))
            {
                throw new InvalidOperationException("任务不存在或已删除");
            }

            await using var ms = new MemoryStream();
            await source.CopyToAsync(ms);
            if (ms.Length != upload.FileSize)
            {
                throw new InvalidOperationException("文件读取不完整，请重新上传");
            }

            var now = DateTime.Now;
            var document = new DocumentFile
            {
                Category = NormalizeCategory(upload.Category),
                Title = upload.Title.Trim(),
                OriginalFileName = Path.GetFileName(upload.OriginalFileName),
                ContentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType.Trim(),
                FileSize = upload.FileSize,
                Content = ms.ToArray(),
                Note = string.IsNullOrWhiteSpace(upload.Note) ? null : upload.Note.Trim(),
                TaskId = taskId,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Documents.Add(document);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "上传文档失败 FileName={FileName}", upload.OriginalFileName);
            throw;
        }
    }

    public async Task<DocumentDownload?> GetDownloadAsync(int id)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Documents
                .Where(d => d.Id == id && !d.IsDeleted)
                .Select(d => new DocumentDownload
                {
                    OriginalFileName = d.OriginalFileName,
                    ContentType = d.ContentType,
                    Content = d.Content,
                })
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "下载文档失败 Id={Id}", id);
            throw;
        }
    }

    public async Task SoftDeleteAsync(int id)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Documents.Where(d => d.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.IsDeleted, true)
                    .SetProperty(d => d.DeletedAt, DateTime.Now)
                    .SetProperty(d => d.UpdatedAt, DateTime.Now));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "删除文档失败 Id={Id}", id);
            throw;
        }
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
        return $"{bytes / 1024d / 1024d:0.#} MB";
    }

    public static string ResolveInlineContentType(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return contentType.Trim();
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".txt" => "text/plain; charset=utf-8",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };
    }

    public static bool CanOpenInBrowser(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".txt")
            return true;

        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        var normalized = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalized == "application/pdf" ||
               normalized.StartsWith("image/") ||
               normalized == "text/plain";
    }

    private static void ValidateUpload(DocumentUpload upload)
    {
        if (string.IsNullOrWhiteSpace(upload.Title))
            throw new InvalidOperationException("请填写文档标题");

        if (string.IsNullOrWhiteSpace(upload.OriginalFileName))
            throw new InvalidOperationException("请选择文件");

        if (upload.FileSize <= 0)
            throw new InvalidOperationException("文件为空，请重新选择");

        if (upload.FileSize > MaxFileSize)
            throw new InvalidOperationException($"文件不能超过 {FormatFileSize(MaxFileSize)}");

        var ext = Path.GetExtension(upload.OriginalFileName);
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"不支持的文件类型：{ext}。支持：{AllowedExtensionText}");
    }

    private static string NormalizeCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? "未分类" : category.Trim();
    }
}

public class DocumentUpload
{
    public string? Category { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Note { get; init; }
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public long FileSize { get; init; }
}

public class DocumentListItem
{
    public int Id { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string? Note { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int? TaskId { get; init; }
    public string? TaskTitle { get; init; }
    public string? TaskPeriod { get; init; }
    public bool? TaskIsDeleted { get; init; }
}

public class DocumentDownload
{
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public byte[] Content { get; init; } = [];
}
