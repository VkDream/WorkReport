using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkReport.Data;
using WorkReport.Data.Models;

namespace WorkReport.Services;

public class TaskService(IDbContextFactory<AppDbContext> dbFactory, ILogger<TaskService> logger)
{
    public const string HistoryPeriod = "历史项目";

    /// <summary>计算指定日期所属的半月期，格式 yyyy-MM-dd~MM-dd。</summary>
    public static string GetPeriod(DateTime date)
    {
        if (date.Day <= 15)
        {
            return $"{date:yyyy-MM}-01~{date:MM}-15";
        }
        var lastDay = DateTime.DaysInMonth(date.Year, date.Month);
        return $"{date:yyyy-MM}-16~{date:MM}-{lastDay}";
    }

    public static string GetCurrentPeriod() => GetPeriod(DateTime.Today);

    /// <summary>获取各半月期的统计概览（用于趋势图）。</summary>
    public async Task<List<PeriodStats>> GetStatsAsync()
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var periods = await db.Tasks.Where(t => !t.IsDeleted).Select(t => t.Period).Distinct().OrderBy(p => p).ToListAsync();
            var stats = new List<PeriodStats>();
            foreach (var p in periods)
            {
                var tasks = await db.Tasks.Where(t => t.Period == p && !t.IsDeleted).ToListAsync();
                stats.Add(new PeriodStats
                {
                    Period = p,
                    Total = tasks.Count,
                    Ok = tasks.Count(t => t.Result is "完成"),
                    Ng = tasks.Count(t => t.Result is "正在进行中"),
                    Pending = tasks.Count(t => t.Result is "待测" or "待确认"),
                    Empty = tasks.Count(t => string.IsNullOrEmpty(t.Result))
                });
            }
            return stats;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取统计概览失败");
            throw;
        }
    }

    /// <summary>Result 字段对应的状态颜色类。兼容旧值（OK/PASS/NKG）和新值（完成/正在进行中）。</summary>
    public static string GetResultCss(string? result) => result switch
    {
        null or "" => "result-empty",
        "完成" => "result-ok",
        "正在进行中" => "result-ng",
        "待测" or "待确认" => "result-pending",
        _ => "result-pending",
    };

    /// <summary>所有出现过的半月期，按时间倒序。</summary>
    public async Task<List<string>> GetPeriodsAsync()
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Tasks
                .Where(t => !t.IsDeleted)
                .Select(t => t.Period)
                .Distinct()
                .OrderByDescending(p => p)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取半月期列表失败");
            throw;
        }
    }

    public async Task<List<WorkTask>> GetTasksByPeriodAsync(string period)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Tasks
                .Where(t => t.Period == period && !t.IsDeleted)
                .OrderBy(t => t.Id)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取半月期 {Period} 任务列表失败", period);
            throw;
        }
    }

    /// <summary>历史查询：按半月期和关键字（匹配工作说明或备注）过滤。</summary>
    public async Task<List<WorkTask>> SearchAsync(string? period = null, string? keyword = null, int page = 1, int pageSize = 20)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            IQueryable<WorkTask> query = db.Tasks.Where(t => !t.IsDeleted);
            if (!string.IsNullOrWhiteSpace(period))
            {
                query = query.Where(t => t.Period == period);
            }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(t => t.Title.Contains(keyword) || (t.Note != null && t.Note.Contains(keyword)));
            }
            return await query
                .OrderByDescending(t => t.Period)
                .ThenBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "搜索任务失败 Period={Period} Keyword={Keyword}", period, keyword);
            throw;
        }
    }

    /// <summary>历史查询总数（与 SearchAsync 使用相同过滤条件）。</summary>
    public async Task<int> SearchCountAsync(string? period = null, string? keyword = null)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            IQueryable<WorkTask> query = db.Tasks.Where(t => !t.IsDeleted);
            if (!string.IsNullOrWhiteSpace(period))
            {
                query = query.Where(t => t.Period == period);
            }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(t => t.Title.Contains(keyword) || (t.Note != null && t.Note.Contains(keyword)));
            }
            return await query.CountAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "搜索任务计数失败 Period={Period} Keyword={Keyword}", period, keyword);
            throw;
        }
    }

    public async Task<WorkTask?> GetTaskAsync(int id)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取任务 {Id} 失败", id);
            throw;
        }
    }

    public async Task AddAsync(WorkTask task)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            task.CreatedAt = DateTime.Now;
            task.UpdatedAt = DateTime.Now;
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "新增任务失败 Title={Title}", task.Title);
            throw;
        }
    }

    public async Task UpdateAsync(WorkTask task)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            task.UpdatedAt = DateTime.Now;
            db.Tasks.Update(task);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新任务 {Id} 失败", task.Id);
            throw;
        }
    }

    public async Task DeleteAsync(int id)
    {
        await SoftDeleteAsync(id);
    }

    /// <summary>软删除：标记 IsDeleted=true。</summary>
    public async Task SoftDeleteAsync(int id)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Tasks.Where(t => t.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsDeleted, true)
                    .SetProperty(t => t.DeletedAt, DateTime.Now)
                    .SetProperty(t => t.UpdatedAt, DateTime.Now));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "软删除任务 {Id} 失败", id);
            throw;
        }
    }

    /// <summary>恢复软删除的任务。</summary>
    public async Task RestoreAsync(int id)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Tasks.Where(t => t.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsDeleted, false)
                    .SetProperty(t => t.DeletedAt, (DateTime?)null)
                    .SetProperty(t => t.UpdatedAt, DateTime.Now));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "恢复任务 {Id} 失败", id);
            throw;
        }
    }

    /// <summary>物理删除（不可恢复）。</summary>
    public async Task HardDeleteAsync(int id)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync();

            // 彻底删除任务前，把关联附件的 TaskId 置空，附件变公共文档
            await db.Documents.Where(d => d.TaskId == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.TaskId, (int?)null)
                    .SetProperty(d => d.UpdatedAt, DateTime.Now));

            await db.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync();

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "物理删除任务 {Id} 失败", id);
            throw;
        }
    }

    /// <summary>物理删除所有已在回收站中的任务（不可恢复）。</summary>
    public async Task<int> HardDeleteAllDeletedAsync()
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var taskIds = await db.Tasks
                .Where(t => t.IsDeleted)
                .Select(t => t.Id)
                .ToListAsync();

            if (taskIds.Count == 0)
            {
                return 0;
            }

            await using var tx = await db.Database.BeginTransactionAsync();

            await db.Documents
                .Where(d => d.TaskId.HasValue && taskIds.Contains(d.TaskId.Value))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.TaskId, (int?)null)
                    .SetProperty(d => d.UpdatedAt, DateTime.Now));

            var deleted = await db.Tasks
                .Where(t => taskIds.Contains(t.Id))
                .ExecuteDeleteAsync();

            await tx.CommitAsync();
            return deleted;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "清空回收站失败");
            throw;
        }
    }

    /// <summary>获取所有已删除任务。</summary>
    public async Task<List<WorkTask>> GetDeletedTasksAsync()
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Tasks
                .Where(t => t.IsDeleted)
                .OrderByDescending(t => t.DeletedAt)
                .ThenBy(t => t.Id)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取已删除任务失败");
            throw;
        }
    }

    /// <summary>已完成汇总：按 Title 合并去重。showAll=false 只查完成，period 可选。</summary>
    public async Task<List<CompletedGroup>> GetCompletedGroupedAsync(bool showAll, string? period = null)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            IQueryable<WorkTask> query = db.Tasks.Where(t => !t.IsDeleted);
            if (!showAll)
                query = query.Where(t => t.Result == "完成");
            if (!string.IsNullOrWhiteSpace(period))
                query = query.Where(t => t.Period == period);

            var tasks = await query.OrderByDescending(t => t.Period).ThenBy(t => t.Id).ToListAsync();

            return tasks
                .GroupBy(t => t.Title.Trim())
                .Select(g => new CompletedGroup
                {
                    PrimaryTaskId = g.First().Id,
                    Title = g.Key,
                    TotalCount = g.Count(),
                    PrimaryType = g.First().Type,
                    PrimaryResult = g.First().Result,
                    LatestPeriod = g.Max(t => t.Period) ?? ""
                })
                .OrderByDescending(g => g.LatestPeriod)
                .ThenBy(g => g.Title)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取已完成汇总失败");
            throw;
        }
    }

    /// <summary>将上期未完成的任务复制到当前期，返回复制条数。同标题已存在则跳过。</summary>
    public async Task<int> CopyUnfinishedFromPreviousAsync(string currentPeriod)
    {
        try
        {
            if (currentPeriod == HistoryPeriod) return 0;

            await using var db = await dbFactory.CreateDbContextAsync();

            // 找上一期未完成：从最近的非当前期开始，逐个往前找有未完成任务的历史期
            var prevPeriods = await db.Tasks
                .Where(t => t.Period != currentPeriod && t.Period != HistoryPeriod && !t.IsDeleted)
                .Select(t => t.Period)
                .Distinct()
                .OrderByDescending(p => p)
                .ToListAsync();

            List<WorkTask> unfinished = new();
            string? fromPeriod = null;
            foreach (var pp in prevPeriods)
            {
                unfinished = await db.Tasks
                    .Where(t => t.Period == pp && (t.Result == null || t.Result != "完成") && !t.IsDeleted)
                    .ToListAsync();
                if (unfinished.Count > 0)
                {
                    fromPeriod = pp;
                    break;
                }
            }

            if (fromPeriod is null || unfinished.Count == 0) return 0;

            // 当前期已有标题（去重用）
            var existingTitles = await db.Tasks
                .Where(t => t.Period == currentPeriod && !t.IsDeleted)
                .Select(t => t.Title)
                .ToListAsync();
            var existingSet = new HashSet<string>(
                existingTitles.Select(x => x.Trim()),
                StringComparer.OrdinalIgnoreCase);

            int copied = 0;
            var now = DateTime.Now;
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                foreach (var src in unfinished)
                {
                    if (existingSet.Contains(src.Title.Trim())) continue;
                    existingSet.Add(src.Title.Trim());

                    db.Tasks.Add(new WorkTask
                    {
                        Period = currentPeriod,
                        Type = src.Type,
                        Title = src.Title,
                        Result = src.Result,
                        ResolveDate = null,  // 新期重置解决时间
                        Note = src.Note,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    copied++;
                }

                await db.SaveChangesAsync();

                // 软删除所有源任务（无论是否复制成功）
                var sourceIds = unfinished.Select(t => t.Id).ToList();
                await db.Tasks.Where(t => sourceIds.Contains(t.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.IsDeleted, true)
                        .SetProperty(t => t.DeletedAt, DateTime.Now)
                        .SetProperty(t => t.UpdatedAt, DateTime.Now));

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
            logger.LogInformation("从 {PrevPeriod} 复制了 {Count} 条未完成任务到 {CurrentPeriod}，原任务已软删除", fromPeriod, copied, currentPeriod);
            return copied;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "复制未完成任务失败 CurrentPeriod={Period}", currentPeriod);
            throw;
        }
    }
}

public class PeriodStats
{
    public string Period { get; init; } = "";
    public int Total { get; init; }
    public int Ok { get; init; }
    public int Ng { get; init; }
    public int Pending { get; init; }
    public int Empty { get; init; }
}

public class CompletedGroup
{
    public int PrimaryTaskId { get; init; }
    public string Title { get; init; } = "";
    public int TotalCount { get; set; }
    public string PrimaryType { get; set; } = "";
    public string? PrimaryResult { get; set; }
    public string LatestPeriod { get; set; } = "";
}
