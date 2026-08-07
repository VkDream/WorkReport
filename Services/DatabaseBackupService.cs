using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkReport.Data;

namespace WorkReport.Services;

public class DatabaseBackupService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _env;

    public DatabaseBackupService(IConfiguration config, ILogger<DatabaseBackupService> logger, IServiceProvider services, IHostEnvironment env)
    {
        _config = config;
        _logger = logger;
        _services = services;
        _env = env;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dbPath = SystemStatusService.ResolveDbPath(_config, _env.ContentRootPath);

                if (!File.Exists(dbPath))
                {
                    _logger.LogWarning("数据库文件不存在，跳过备份: {Path}", dbPath);
                }
                else
                {
                    // 把 WAL 内容合并到主库文件
                    try
                    {
                        using var scope = _services.CreateScope();
                        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                        await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)", stoppingToken);
                    }
                    catch (Exception walEx)
                    {
                        _logger.LogWarning(walEx, "WAL checkpoint 失败，继续备份");
                    }

                    var backupDir = Path.Combine(AppContext.BaseDirectory, "backups");
                    Directory.CreateDirectory(backupDir);

                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var backupPath = Path.Combine(backupDir, $"workreport_{timestamp}.db");

                    File.Copy(dbPath, backupPath, overwrite: false);
                    _logger.LogInformation("数据库备份完成: {Path}", backupPath);

                    CleanOldBackups(backupDir, keepCount: 30);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库备份失败");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private static void CleanOldBackups(string backupDir, int keepCount)
    {
        var files = Directory.GetFiles(backupDir, "workreport_*.db")
            .OrderByDescending(f => f)
            .ToList();

        foreach (var file in files.Skip(keepCount))
        {
            try { File.Delete(file); }
            catch { }
        }
    }
}
