using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using GroupBudget.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    // 内网演示阶段关闭邮箱确认，注册后可直接登录；上线时改为 true 并配置邮件
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

// 启动时自动应用迁移并写入演示用种子数据（部门、科目、角色、账号、批次）
await GroupBudget.Data.SeedData.InitializeAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
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

app.Run();
