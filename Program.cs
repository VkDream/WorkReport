using ClosedXML.Excel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkReport.Components;
using WorkReport.Data;
using WorkReport.Data.Models;
using WorkReport.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=workreport.db"));
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SystemStatusService>();
builder.Services.AddHostedService<DatabaseBackupService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? "WorkReportAuth";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// 启动时自动应用未执行的迁移（首次运行会创建 workreport.db）
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var startupLogger = loggerFactory.CreateLogger("Startup");
using (var db = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
{
    db.Database.Migrate();
}

// 首次运行时自动创建管理员账号
{
    await using var db = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    if (!await db.Users.AnyAsync())
    {
        var authService = new AuthService(
            app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            loggerFactory.CreateLogger<AuthService>());
        var admin = await authService.RegisterAsync("admin", "admin123");
        if (admin is not null)
        {
            // 标记必须修改默认密码
            var adminUser = await db.Users.FirstOrDefaultAsync(u => u.Username == "admin");
            if (adminUser is not null)
            {
                adminUser.MustChangePassword = true;
                await db.SaveChangesAsync();
            }
            startupLogger.LogInformation("已创建默认管理员账号（首次登录需修改密码）");
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// 仅在配置启用时才使用 HTTPS 重定向（开发环境默认启用，发布后可关闭）
if (builder.Configuration.GetValue<bool>("EnableHttpsRedirect"))
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// 强制修改默认密码中间件
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    // 放行改密页、登出、登录、注册、静态资源、Blazor框架资源
    if (path.StartsWith("/profile") || path.StartsWith("/api/logout") ||
        path.StartsWith("/api/login") || path.StartsWith("/login") ||
        path.StartsWith("/register") || path.StartsWith("/api/register") ||
        path.StartsWith("/_blazor") || path.StartsWith("/_framework") ||
        path == "/favicon.png" ||
        path.StartsWith("/app.css") || path.StartsWith("/bootstrap") ||
        path.StartsWith("/WorkReport.styles.css") || path.StartsWith("/chart.js"))
    {
        await next(ctx);
        return;
    }

    if (ctx.User?.Identity?.IsAuthenticated == true)
    {
        var username = ctx.User.Identity.Name;
        if (username is not null)
        {
            try
            {
                var dbFactory = ctx.RequestServices.GetRequiredService<IDbContextFactory<AppDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
                if (user?.MustChangePassword == true)
                {
                    ctx.Response.Redirect("/profile?mustChangePwd=1");
                    return;
                }
            }
            catch { /* 非致命 */ }
        }
    }

    await next(ctx);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

// ── 登录 API（用户名 + 密码）──
app.MapPost("/api/login", async (HttpContext ctx, AuthService auth) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var username = form["username"].FirstOrDefault();
        var password = form["password"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return Results.Json(new { ok = false, message = "请输入用户名和密码" });
        }

        var user = await auth.ValidateAsync(username.Trim(), password);
        if (user is null)
        {
            return Results.Json(new { ok = false, message = "用户名或密码错误" });
        }

        var claims = new[] { new Claim(ClaimTypes.Name, user.Username) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

        return Results.Json(new { ok = true, mustChangePwd = user.MustChangePassword });
    }
    catch
    {
        return Results.Json(new { ok = false, message = "请求格式错误" });
    }
});

// ── 表单登录（浏览器 POST，重定向）──
app.MapPost("/api/login-form", async (HttpContext ctx, AuthService auth) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var username = form["username"].FirstOrDefault()?.Trim();
        var password = form["password"].FirstOrDefault();
        var returnUrl = form["returnUrl"].FirstOrDefault() ?? "/";
        // 防止开放跳转：只允许以单个 / 开头的本站相对路径
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//") || returnUrl.StartsWith("/\\"))
            returnUrl = "/";

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ctx.Response.Redirect($"/login?error={Uri.EscapeDataString("请输入用户名和密码")}");
            return;
        }

        var user = await auth.ValidateAsync(username, password);
        if (user is null)
        {
            ctx.Response.Redirect($"/login?error={Uri.EscapeDataString("用户名或密码错误")}");
            return;
        }

        var claims = new[] { new Claim(ClaimTypes.Name, user.Username) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

        // 首次登录必须修改默认密码
        if (user.MustChangePassword)
        {
            ctx.Response.Redirect("/profile?mustChangePwd=1");
            return;
        }

        // 仅允许相对路径跳转，防止开放重定向
        ctx.Response.Redirect(returnUrl.StartsWith('/') ? returnUrl : "/");
    }
    catch
    {
        ctx.Response.Redirect("/login?error=请求格式错误");
    }
});

// ── 注册 API ──
app.MapPost("/api/register", async (HttpContext ctx, AuthService auth) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var username = form["username"].FirstOrDefault()?.Trim();
        var password = form["password"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(username) || username.Length < 2)
        {
            return Results.Json(new { ok = false, message = "用户名至少 2 个字符" });
        }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            return Results.Json(new { ok = false, message = "密码至少 4 个字符" });
        }

        var user = await auth.RegisterAsync(username, password);
        if (user is null)
        {
            return Results.Json(new { ok = false, message = "注册失败，用户名可能已存在" });
        }

        // 注册成功自动登录
        var claims = new[] { new Claim(ClaimTypes.Name, user.Username) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

        return Results.Json(new { ok = true });
    }
    catch
    {
        return Results.Json(new { ok = false, message = "请求格式错误" });
    }
});

// ── 表单注册（浏览器 POST，重定向）──
app.MapPost("/api/register-form", async (HttpContext ctx, AuthService auth) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var username = form["username"].FirstOrDefault()?.Trim();
        var password = form["password"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(username) || username.Length < 2)
        {
            ctx.Response.Redirect($"/register?error={Uri.EscapeDataString("用户名至少 2 个字符")}");
            return;
        }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            ctx.Response.Redirect($"/register?error={Uri.EscapeDataString("密码至少 4 个字符")}");
            return;
        }

        var user = await auth.RegisterAsync(username, password);
        if (user is null)
        {
            ctx.Response.Redirect($"/register?error={Uri.EscapeDataString("注册失败，用户名可能已存在")}");
            return;
        }

        var claims = new[] { new Claim(ClaimTypes.Name, user.Username) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

        ctx.Response.Redirect("/");
    }
    catch
    {
        ctx.Response.Redirect("/register?error=请求格式错误");
    }
});

app.MapGet("/api/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/login");
});

// ── Dashboard 统计 API（需要认证）──
app.MapGet("/api/stats", async (IDbContextFactory<AppDbContext> dbFactory) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var periods = await db.Tasks.Where(t => !t.IsDeleted).Select(t => t.Period).Distinct().OrderBy(p => p).ToListAsync();
    var result = new List<object>();
    foreach (var p in periods)
    {
        var tasks = await db.Tasks.Where(t => t.Period == p && !t.IsDeleted).ToListAsync();
        result.Add(new
        {
            period = p,
            total = tasks.Count,
            ok = tasks.Count(t => t.Result is "完成"),
            ng = tasks.Count(t => t.Result is "正在进行中"),
            pending = tasks.Count(t => t.Result is "待测" or "待确认"),
            empty = tasks.Count(t => string.IsNullOrEmpty(t.Result))
        });
    }
    return Results.Json(result);
}).RequireAuthorization();

// ── 导出 Excel（需要认证）──
app.MapGet("/api/export", async (
    IDbContextFactory<AppDbContext> dbFactory,
    HttpContext ctx,
    string? period,
    string? keyword) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    IQueryable<WorkTask> query = db.Tasks.Where(t => !t.IsDeleted);
    if (!string.IsNullOrWhiteSpace(period))
        query = query.Where(t => t.Period == period);
    if (!string.IsNullOrWhiteSpace(keyword))
        query = query.Where(t => t.Title.Contains(keyword) || (t.Note != null && t.Note.Contains(keyword)));

    var tasks = await query.OrderByDescending(t => t.Period).ThenBy(t => t.Id).ToListAsync();
    var username = ctx.User.Identity?.Name ?? "用户";
    User? currentUser = null;
    {
        await using var db2 = await dbFactory.CreateDbContextAsync();
        currentUser = await db2.Users.FirstOrDefaultAsync(u => u.Username == username);
    }
    var userName = (string.IsNullOrWhiteSpace(currentUser?.DisplayName) ? username : currentUser.DisplayName) ?? username;
    var periodText = string.IsNullOrWhiteSpace(period) ? TaskService.GetCurrentPeriod() : period;

    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("工作汇报");

    ws.ShowGridLines = false;
    ws.Style.Font.FontName = "Microsoft YaHei";
    ws.Style.Font.FontSize = 12;

    // 列宽
    ws.Column(1).Width = 5.71;   // ¤
    ws.Column(2).Width = 6.71;   // #
    ws.Column(3).Width = 14.71;  // 类型
    ws.Column(4).Width = 38.71;  // 工作说明
    ws.Column(5).Width = 16.71;  // 完成效果
    ws.Column(6).Width = 16.71;  // 解决时间
    ws.Column(7).Width = 40.71;  // 备注说明

    var grey = XLColor.FromHtml("#94A3B8");
    var dark = XLColor.FromHtml("#1E293B");
    var mid = XLColor.FromHtml("#64748B");
    var headerText = XLColor.FromHtml("#475569");
    var headerFill = XLColor.FromHtml("#F1F5F9");
    var blue = XLColor.FromHtml("#2563EB");
    var purple = XLColor.FromHtml("#7C3AED");
    var thinGrey = XLColor.FromHtml("#E2E8F0");

    int row = 1;

    // ── R1 标题 ──
    ws.Range(row, 1, row, 7).Merge();
    var tc = ws.Cell(row, 1);
    tc.Value = "工作汇报半月总结";
    tc.Style.Font.Bold = true;
    tc.Style.Font.FontSize = 22;
    tc.Style.Font.FontColor = dark;
    tc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    tc.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    ws.Row(row).Height = 56;
    row++;

    // ── R2 汇报人 ──
    ws.Range(row, 1, row, 7).Merge();
    var sc = ws.Cell(row, 1);
    sc.Value = $"汇报人：{userName}（{periodText}）";
    sc.Style.Font.FontSize = 14;
    sc.Style.Font.FontColor = mid;
    sc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    sc.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    ws.Row(row).Height = 40;
    row++;

    // R3 空行 (skip in the numbering)
    row++;

    // ── R4 表头 ──
    var headers = new[] {
        ("",  XLAlignmentHorizontalValues.Center),
        ("#", XLAlignmentHorizontalValues.Center),
        ("类型", XLAlignmentHorizontalValues.Left),
        ("工作说明", XLAlignmentHorizontalValues.Left),
        ("完成效果", XLAlignmentHorizontalValues.Left),
        ("解决时间", XLAlignmentHorizontalValues.Left),
        ("备注说明", XLAlignmentHorizontalValues.Left)
    };
    for (int c = 0; c < headers.Length; c++)
    {
        var h = ws.Cell(row, c + 1);
        h.Value = headers[c].Item1;
        h.Style.Font.Bold = true;
        h.Style.Font.FontSize = 12;
        h.Style.Font.FontColor = headerText;
        h.Style.Fill.BackgroundColor = headerFill;
        h.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        h.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        h.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        h.Style.Border.BottomBorderColor = blue;
    }
    ws.Row(row).Height = 36;
    row++;

    // ── 分区 ──
    var sections = new[] { (TaskTypes.Key, "  █ 重点工作"), (TaskTypes.Daily, "  █ 日常工作") };
    var sectionColors = new[] { blue, purple };
    int seq = 1;
    const int minRowsPerSection = 3;

    for (int si = 0; si < sections.Length; si++)
    {
        var sectionTasks = tasks.Where(t => t.Type == sections[si].Item1).ToList();
        var sectionColor = sectionColors[si];
        var displayCount = Math.Max(sectionTasks.Count, minRowsPerSection);

        // ── 分区标题行 ──
        ws.Range(row, 1, row, 7).Merge();
        var secH = ws.Cell(row, 1);
        secH.Value = $"{sections[si].Item2}（{sectionTasks.Count} 项）";
        secH.Style.Font.Bold = true;
        secH.Style.Font.FontSize = 14;
        secH.Style.Font.FontColor = XLColor.White;
        secH.Style.Fill.BackgroundColor = sectionColor;
        secH.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        secH.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(row).Height = 38;
        row++;

        for (int i = 0; i < displayCount; i++)
        {
            var t = i < sectionTasks.Count ? sectionTasks[i] : null;

            // A: ¤ marker
            var ca = ws.Cell(row, 1);
            ca.Value = "¤";
            ca.Style.Font.FontName = "Wingdings";
            ca.Style.Font.FontSize = 14;
            ca.Style.Font.FontColor = sectionColor;
            ca.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ca.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // B: seq #
            var cb = ws.Cell(row, 2);
            if (t != null)
                cb.Value = seq++;
            else
                cb.Value = "";
            cb.Style.Font.FontColor = grey;
            cb.Style.Font.FontSize = 12;
            cb.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cb.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // C: 类型
            var cc = ws.Cell(row, 3);
            cc.Value = sections[si].Item1;
            cc.Style.Font.FontSize = 12;
            cc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cc.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // D: 工作说明
            ws.Cell(row, 4).Value = t?.Title ?? "";
            ws.Cell(row, 4).Style.Font.FontSize = 12;
            ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // E: 完成效果
            var ce = ws.Cell(row, 5);
            ce.Value = t?.Result ?? "";
            ce.Style.Font.FontSize = 12;
            ce.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ce.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            // color by result
            var resultColor = (t?.Result) switch
            {
                "完成" => XLColor.FromHtml("#065F46"),
                "正在进行中" => XLColor.FromHtml("#92400E"),
                "待测" or "待确认" => XLColor.FromHtml("#475569"),
                _ => grey
            };
            ce.Style.Font.FontColor = resultColor;

            // F: 解决时间
            ws.Cell(row, 6).Value = t?.ResolveDate ?? "";
            ws.Cell(row, 6).Style.Font.FontSize = 12;
            ws.Cell(row, 6).Style.Font.FontColor = t != null ? dark : grey;
            ws.Cell(row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // G: 备注说明
            ws.Cell(row, 7).Value = t?.Note ?? "";
            ws.Cell(row, 7).Style.Font.FontSize = 12;
            ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 7).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // 下边框 (col B-G)
            for (int c = 2; c <= 7; c++)
            {
                ws.Cell(row, c).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, c).Style.Border.BottomBorderColor = thinGrey;
            }

            ws.Row(row).Height = 30;
            row++;
        }

        row++; // 分区间空行
    }

    using var ms = new MemoryStream();
    wb.SaveAs(ms);
    ms.Position = 0;

    var fileName = $"工作汇报_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
    return Results.File(ms.ToArray(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        fileName);
}).RequireAuthorization();

app.MapGet("/api/documents/download/{id:int}", async (int id, DocumentService documents) =>
{
    var file = await documents.GetDownloadAsync(id);
    if (file is null)
    {
        return Results.NotFound("文档不存在");
    }

    return Results.File(file.Content, file.ContentType, file.OriginalFileName);
}).RequireAuthorization();

app.MapGet("/api/documents/open/{id:int}", async (int id, HttpContext ctx, DocumentService documents) =>
{
    var file = await documents.GetDownloadAsync(id);
    if (file is null)
    {
        return Results.NotFound("Document not found");
    }

    ctx.Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(file.OriginalFileName)}";
    return Results.File(
        file.Content,
        DocumentService.ResolveInlineContentType(file.ContentType, file.OriginalFileName),
        enableRangeProcessing: true);
}).RequireAuthorization();

// ── 数据库备份下载（需要认证）──
app.MapGet("/api/backup/download", async (HttpContext ctx, IDbContextFactory<AppDbContext> dbFactory) =>
{
    // 解析数据库路径
    var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var env = ctx.RequestServices.GetRequiredService<IHostEnvironment>();
    var dbPath = SystemStatusService.ResolveDbPath(cfg, env.ContentRootPath);

    if (!File.Exists(dbPath))
        return Results.NotFound("数据库文件不存在");

    // 先把 WAL 内容合并到主库文件
    try
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)");
    }
    catch { /* 非致命 */ }

    using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var bytes = new byte[fs.Length];
    fs.ReadExactly(bytes);
    var fileName = $"workreport_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db";
    return Results.File(bytes, "application/octet-stream", fileName);
}).RequireAuthorization();

// ── 数据库备份上传恢复（需要认证）──
app.MapPost("/api/backup/restore", async (HttpContext ctx, ILoggerFactory loggerFactory, IDbContextFactory<AppDbContext> dbFactory) =>
{
    var logger = loggerFactory.CreateLogger("Backup");
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            ctx.Response.Redirect("/profile?error=请选择要恢复的备份文件");
            return;
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".db")
        {
            ctx.Response.Redirect("/profile?error=仅支持 .db 数据库文件");
            return;
        }

        // 先备份当前数据库
        var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var env = ctx.RequestServices.GetRequiredService<IHostEnvironment>();
        var dbPath = SystemStatusService.ResolveDbPath(cfg, env.ContentRootPath);

        // 把 WAL 内容合并到主库再备份
        if (File.Exists(dbPath))
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)");
            }
            catch (Exception walEx)
            {
                logger.LogWarning(walEx, "WAL checkpoint 失败，继续备份");
            }
        }

        var backupPath = dbPath + ".before_restore";
        if (File.Exists(dbPath))
        {
            using var src = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var dst = new FileStream(backupPath, FileMode.Create, FileAccess.Write);
            src.CopyTo(dst);
        }

        logger.LogInformation("恢复前已备份当前数据库到 {Path}", backupPath);

        // 删除旧 WAL/SHM，防止恢复后 SQLite 异常
        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";
        try { if (File.Exists(walPath)) File.Delete(walPath); } catch { }
        try { if (File.Exists(shmPath)) File.Delete(shmPath); } catch { }

        // 写入上传的文件
        await using var stream = new FileStream(dbPath, FileMode.Create, FileAccess.Write);
        await file.CopyToAsync(stream);

        logger.LogInformation("数据库已从备份恢复，文件名: {FileName}", file.FileName);
        ctx.Response.Redirect("/login?msg=数据库恢复成功，请重新登录");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "数据库备份恢复失败");
        ctx.Response.Redirect($"/profile?error={Uri.EscapeDataString("恢复失败：" + ex.Message)}");
    }
}).DisableAntiforgery().RequireAuthorization();

app.MapPost("/api/tasks/delete", async (IDbContextFactory<AppDbContext> dbFactory, HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    if (!int.TryParse(form["id"].FirstOrDefault(), out var id) || id <= 0)
        return Results.Json(new { ok = false, message = "无效的任务 ID" });

    var logger = ctx.RequestServices.GetRequiredService<ILogger<TaskService>>();
    var svc = new TaskService(dbFactory, logger);
    await svc.SoftDeleteAsync(id);
    return Results.Json(new { ok = true, message = "已删除" });
}).RequireAuthorization();

app.MapPost("/api/tasks/delete-form", async (
    IDbContextFactory<AppDbContext> dbFactory,
    HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    if (!int.TryParse(form["id"].FirstOrDefault(), out var id) || id <= 0)
    {
        ctx.Response.Redirect($"/?error={Uri.EscapeDataString("无效的任务 ID")}");
        return;
    }

    var logger = ctx.RequestServices.GetRequiredService<ILogger<TaskService>>();
    var svc = new TaskService(dbFactory, logger);
    await svc.SoftDeleteAsync(id);

    ctx.Response.Redirect("/");
}).DisableAntiforgery().RequireAuthorization();

app.MapPost("/api/tasks/restore-form", async (
    IDbContextFactory<AppDbContext> dbFactory,
    HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    if (!int.TryParse(form["id"].FirstOrDefault(), out var id) || id <= 0)
    {
        ctx.Response.Redirect($"/recycle-bin?error={Uri.EscapeDataString("无效的任务 ID")}");
        return;
    }

    var logger = ctx.RequestServices.GetRequiredService<ILogger<TaskService>>();
    var svc = new TaskService(dbFactory, logger);
    await svc.RestoreAsync(id);

    ctx.Response.Redirect("/recycle-bin");
}).DisableAntiforgery().RequireAuthorization();

app.MapPost("/api/tasks/hard-delete-form", async (
    IDbContextFactory<AppDbContext> dbFactory,
    HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    if (!int.TryParse(form["id"].FirstOrDefault(), out var id) || id <= 0)
    {
        ctx.Response.Redirect($"/recycle-bin?error={Uri.EscapeDataString("无效的任务 ID")}");
        return;
    }

    var logger = ctx.RequestServices.GetRequiredService<ILogger<TaskService>>();
    var svc = new TaskService(dbFactory, logger);
    await svc.HardDeleteAsync(id);

    ctx.Response.Redirect("/recycle-bin");
}).DisableAntiforgery().RequireAuthorization();

app.MapPost("/api/tasks/hard-delete-all-form", async (
    IDbContextFactory<AppDbContext> dbFactory,
    HttpContext ctx) =>
{
    var logger = ctx.RequestServices.GetRequiredService<ILogger<TaskService>>();
    var svc = new TaskService(dbFactory, logger);
    await svc.HardDeleteAllDeletedAsync();

    ctx.Response.Redirect("/recycle-bin");
}).DisableAntiforgery().RequireAuthorization();

SystemStatusService.StartupTime = DateTime.UtcNow;

app.Run();
