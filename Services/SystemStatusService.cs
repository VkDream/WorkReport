using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkReport.Data;

namespace WorkReport.Services;

public class SystemStatusService
{
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHttpContextAccessor _http;

    /// <summary>程序启动时间（由 Program.cs 设置）。</summary>
    public static DateTime StartupTime { get; set; } = DateTime.MinValue;

    /// <summary>从连接串解析数据库文件绝对路径。</summary>
    public static string ResolveDbPath(IConfiguration config, string contentRootPath)
    {
        var connStr = config.GetConnectionString("Default") ?? "Data Source=workreport.db";
        var builder = new SqliteConnectionStringBuilder(connStr);
        var dbPath = builder.DataSource;
        if (!Path.IsPathRooted(dbPath))
            dbPath = Path.GetFullPath(Path.Combine(contentRootPath, dbPath));
        return dbPath;
    }

    public SystemStatusService(IConfiguration config, IHostEnvironment env,
        IDbContextFactory<AppDbContext> dbFactory, IHttpContextAccessor http)
    {
        _config = config;
        _env = env;
        _dbFactory = dbFactory;
        _http = http;
    }

    public async Task<SystemStatusInfo> GetSystemStatusAsync()
    {
        var dbPath = ResolveDbPath(_config, _env.ContentRootPath);

        var dbExists = File.Exists(dbPath);
        long dbSize = 0;
        if (dbExists)
        {
            try { dbSize = new FileInfo(dbPath).Length; }
            catch { }
        }

        int taskCount = 0;
        int userCount = 0;
        string? dbReadError = null;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            taskCount = await db.Tasks.CountAsync();
            userCount = await db.Users.CountAsync();
        }
        catch (Exception ex)
        {
            dbReadError = ex.Message;
        }

        // WAL / SHM 状态
        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";
        bool walExists = File.Exists(walPath);
        long walSize = 0;
        if (walExists) { try { walSize = new FileInfo(walPath).Length; } catch { } }
        bool shmExists = File.Exists(shmPath);
        long shmSize = 0;
        if (shmExists) { try { shmSize = new FileInfo(shmPath).Length; } catch { } }

        string? lastBackupName = null;
        string? lastBackupTime = null;
        try
        {
            var backupDir = Path.Combine(AppContext.BaseDirectory, "backups");
            if (Directory.Exists(backupDir))
            {
                var files = Directory.GetFiles(backupDir, "workreport_*.db");
                if (files.Length > 0)
                {
                    var latest = files
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTime)
                        .First();
                    lastBackupName = latest.Name;
                    lastBackupTime = latest.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
            }
        }
        catch { }

        var listeningUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "（未配置）";
        var currentUsername = _http.HttpContext?.User?.Identity?.Name;

        return new SystemStatusInfo
        {
            DbAbsolutePath = dbPath,
            DbExists = dbExists,
            DbSizeBytes = dbSize,
            DbReadError = dbReadError,
            TaskCount = taskCount,
            UserCount = userCount,
            WalExists = walExists,
            WalSizeBytes = walSize,
            ShmExists = shmExists,
            ShmSizeBytes = shmSize,
            ContentRootPath = _env.ContentRootPath,
            AppBaseDirectory = AppContext.BaseDirectory,
            EnvironmentName = _env.EnvironmentName,
            ProcessId = Environment.ProcessId,
            ListeningUrls = listeningUrls,
            CurrentUsername = currentUsername,
            StartupTime = StartupTime == DateTime.MinValue ? null : StartupTime.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            LastBackupName = lastBackupName,
            LastBackupTime = lastBackupTime,
        };
    }
}

public class SystemStatusInfo
{
    public string DbAbsolutePath { get; set; } = "";
    public bool DbExists { get; set; }
    public long DbSizeBytes { get; set; }
    public string? DbReadError { get; set; }
    public int TaskCount { get; set; }
    public int UserCount { get; set; }
    public bool WalExists { get; set; }
    public long WalSizeBytes { get; set; }
    public bool ShmExists { get; set; }
    public long ShmSizeBytes { get; set; }
    public string ContentRootPath { get; set; } = "";
    public string AppBaseDirectory { get; set; } = "";
    public string EnvironmentName { get; set; } = "";
    public int ProcessId { get; set; }
    public string ListeningUrls { get; set; } = "";
    public string? CurrentUsername { get; set; }
    public string? StartupTime { get; set; }
    public string? LastBackupName { get; set; }
    public string? LastBackupTime { get; set; }
}
