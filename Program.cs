using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using GroupBudget.Data;
using Npgsql;

// Npgsql 6+ 默认拒绝把 DateTime.Now（Kind=Unspecified）写入 timestamptz。
// 本项目大量使用 DateTime.Now，演示阶段沿用旧行为，避免逐处改成 UTC。
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// 容器平台（Vercel）通过 PORT 注入监听端口，必须绑定 0.0.0.0
var platformPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(platformPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{platformPort}");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// 优先使用托管 Postgres（Vercel / Neon 注入 DATABASE_URL），否则退回本地 SQLite
// 不同集成注入的变量名略有差异，按常见顺序兜底
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
    ?? Environment.GetEnvironmentVariable("POSTGRES_PRISMA_URL");
var usePostgres = !string.IsNullOrWhiteSpace(databaseUrl);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (usePostgres)
        options.UseNpgsql(BuildConnectionString(databaseUrl!));
    else
        options.UseSqlite(connectionString);
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// 密钥环存库：容器重启或多实例时，登录 Cookie 与防伪令牌仍然有效
builder.Services.AddDataProtection()
    .SetApplicationName("GroupBudget")
    .PersistKeysToDbContext<ApplicationDbContext>();

// Vercel 在边缘终止 TLS，容器内看到的是 http；靠转发头还原真实协议
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    // 演示阶段关闭邮箱确认，注册后可直接登录；正式上线改为 true 并配置邮件
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequiredLength = 6;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<GroupBudget.Services.ActualExcelImportService>();
builder.Services.AddScoped<GroupBudget.Services.ExecutionImportService>();
builder.Services.AddScoped<GroupBudget.Services.MappingExcelImportService>();

var app = builder.Build();

// 启动时自动建库并写入演示用种子数据（部门、科目、角色、账号、批次）
await GroupBudget.Data.SeedData.InitializeAsync(app.Services);

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    // 不启用 UseHttpsRedirection：TLS 由 Vercel 边缘负责，容器内再跳转会造成重定向循环
}

app.UseStaticFiles();
app.UseRouting();

// 关键：模板默认缺少这两行中的 Authentication，不补上角色与登录判断全部失效
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    database = usePostgres ? "postgres" : "sqlite"
}));

app.Run();

// 把 postgres://user:pass@host/db?sslmode=require 转成 Npgsql 连接串
// 注意：不能用 Uri 解析——密码里出现 # ? / 会被当成片段或路径截断，导致连错库或认证失败。
static string BuildConnectionString(string raw)
{
    if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return raw;
    }

    var body = raw[(raw.IndexOf("://", StringComparison.Ordinal) + 3)..];
    var slash = body.IndexOf('/');
    var authority = slash < 0 ? body : body[..slash];
    var rest = slash < 0 ? string.Empty : body[(slash + 1)..];

    var username = "postgres";
    var password = string.Empty;
    var at = authority.LastIndexOf('@');
    if (at >= 0)
    {
        var userInfo = authority[..at];
        authority = authority[(at + 1)..];
        var colon = userInfo.IndexOf(':');
        username = colon < 0 ? userInfo : userInfo[..colon];
        password = colon < 0 ? string.Empty : userInfo[(colon + 1)..];
    }

    var queryIndex = rest.IndexOf('?');
    var database = (queryIndex < 0 ? rest : rest[..queryIndex]);
    var query = queryIndex < 0 ? string.Empty : rest[(queryIndex + 1)..];

    var port = 5432;
    var hostPort = authority.LastIndexOf(':');
    if (hostPort > 0 && int.TryParse(authority[(hostPort + 1)..], out var parsedPort))
    {
        port = parsedPort;
        authority = authority[..hostPort];
    }

    var cs = new NpgsqlConnectionStringBuilder
    {
        Host = authority,
        Port = port,
        Username = Uri.UnescapeDataString(username),
        Password = Uri.UnescapeDataString(password),
        Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database,
        SslMode = SslMode.Require,
        Pooling = true,
        MaxPoolSize = 10
    };

    foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var kv = pair.Split('=', 2);
        if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
        {
            cs.SslMode = kv[1].ToLowerInvariant() switch
            {
                "disable" => SslMode.Disable,
                "allow" => SslMode.Allow,
                "prefer" => SslMode.Prefer,
                "verify-ca" => SslMode.VerifyCA,
                "verify-full" => SslMode.VerifyFull,
                _ => SslMode.Require
            };
        }
    }

    return cs.ConnectionString;
}
