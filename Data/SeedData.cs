using GroupBudget.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Data;

/// <summary>
/// 启动时自动建库并写入演示数据。所有金额均为示例值，不含任何真实财务数据。
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var userManager = sp.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();

        // Postgres 演示库直接按当前模型建表（迁移文件是 SQLite 专用类型，不能复用）；
        // 本地 SQLite 仍走迁移，保留历史演进的完整性。
        if (db.Database.IsNpgsql())
            await db.Database.EnsureCreatedAsync();
        else
            await db.Database.MigrateAsync();

        foreach (var role in new[] { AppRoles.Admin, AppRoles.Reviewer, AppRoles.Filler })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // 12 个部门（示例名称，可自行改成集团实际部门）
        string[] departments =
        {
            "投资一部", "投资二部", "投资三部", "行政管理部", "工程部", "财务部",
            "人事部", "法务部", "审计部", "营销部", "信息部", "总裁办"
        };

        if (!await db.Organizations.AnyAsync())
        {
            for (var i = 0; i < departments.Length; i++)
            {
                db.Organizations.Add(new Organization
                {
                    Code = $"D{i + 1:00}",
                    Name = departments[i]
                });
            }
            await db.SaveChangesAsync();
        }
        else
        {
            // 已有数据时补齐缺失部门（例如后来新增的「总裁办」），无需重建数据库
            var existingNames = await db.Organizations.Select(x => x.Name).ToListAsync();
            var nextCode = existingNames.Count;
            foreach (var name in departments.Where(x => !existingNames.Contains(x)))
            {
                nextCode++;
                db.Organizations.Add(new Organization { Code = $"D{nextCode:00}", Name = name });
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
        }

        // 科目字典按「表1-部门支出预算总表」的 5 个费用类别与一级预算科目建立，顺序与原表一致。
        // 只建一级科目：二级明细（如「市内交通费」下的私车公用费）在本系统中不参与填报，避免父子重复计数。
        (string Code, string Name, string Category)[] subjects =
        {
            ("101", "培训费", "日常管理费"),
            ("102", "会议费", "日常管理费"),
            ("103", "差旅费", "日常管理费"),
            ("104", "国际交流费", "日常管理费"),
            ("105", "文具印刷费", "日常管理费"),
            ("106", "快递费", "日常管理费"),
            ("107", "低值耗材费", "日常管理费"),
            ("108", "订阅费", "日常管理费"),
            ("109", "其他办公费", "日常管理费"),
            ("110", "市内交通费", "日常管理费"),
            ("111", "车辆运行维护费", "日常管理费"),
            ("112", "业务招待费", "日常管理费"),
            ("113", "招聘费", "日常管理费"),
            ("114", "咨询费", "日常管理费"),
            ("115", "中介服务费", "日常管理费"),
            ("116", "诉讼费", "日常管理费"),
            ("117", "服装费", "日常管理费"),
            ("118", "劳保用品", "日常管理费"),
            ("119", "住房补贴", "日常管理费"),
            ("120", "话费补贴", "日常管理费"),
            ("121", "节日福利费", "日常管理费"),
            ("122", "团建活动费", "日常管理费"),
            ("123", "加班餐费", "日常管理费"),
            ("124", "其他员工福利费", "日常管理费"),
            ("125", "劳务费", "日常管理费"),
            ("201", "工资", "人员费用"),
            ("202", "社保和公积金", "人员费用"),
            ("203", "福利费", "人员费用"),
            ("301", "水费", "设施运营费"),
            ("302", "电费", "设施运营费"),
            ("303", "燃气费", "设施运营费"),
            ("304", "取暖费", "设施运营费"),
            ("305", "清洁用品费", "设施运营费"),
            ("306", "通讯费", "设施运营费"),
            ("307", "物业费", "设施运营费"),
            ("308", "保洁费", "设施运营费"),
            ("309", "保安费", "设施运营费"),
            ("310", "零星维修费", "设施运营费"),
            ("311", "专项维修改造费", "设施运营费"),
            ("312", "维保（护）费", "设施运营费"),
            ("313", "绿化养护费", "设施运营费"),
            ("314", "保险费", "设施运营费"),
            ("315", "房租费", "设施运营费"),
            ("401", "食材采购", "食堂专项费"),
            ("402", "食堂耗材", "食堂专项费"),
            ("501", "广告费", "营销费用"),
            ("502", "业务宣传费", "营销费用"),
            ("503", "展会费", "营销费用"),
            ("504", "佣金手续费", "营销费用"),
            ("505", "渠道建设费", "营销费用"),
            ("506", "平台服务费", "营销费用"),
            ("507", "运输装卸费", "营销费用"),
            ("508", "仓储费", "营销费用"),
            ("509", "其他销售费用", "营销费用")
        };

        if (!await db.Subjects.AnyAsync())
        {
            for (var i = 0; i < subjects.Length; i++)
            {
                db.Subjects.Add(new Subject
                {
                    Code = subjects[i].Code,
                    Name = subjects[i].Name,
                    Category = subjects[i].Category,
                    IsLeaf = true,
                    IsRequired = false,
                    SortOrder = i + 1
                });
            }
            await db.SaveChangesAsync();
        }

        await EnsureSubjectDepartmentScopesAsync(db);

        var fillerOrg = await db.Organizations.FirstOrDefaultAsync(x => x.Name == "行政管理部")
            ?? await db.Organizations.FirstAsync();

        await EnsureUserAsync(userManager, db, "admin@group.local", "Admin@123", AppRoles.Admin, null);
        await EnsureUserAsync(userManager, db, "reviewer@group.local", "Review@123", AppRoles.Reviewer, null);
        await EnsureUserAsync(userManager, db, "filler@group.local", "Fill@123", AppRoles.Filler, fillerOrg.Id);

        if (!await db.BudgetBatches.AnyAsync())
        {
            var batch = new BudgetBatch
            {
                Name = "2027 年度预算编制（基准年 2026）",
                PlanYear = 2027,
                BaselineYear = 2026,
                Status = BatchStatus.Published,
                PublishedAt = DateTime.Now,
                PublishedBy = "系统初始化",
                CurrentBaselineVersion = 1,
                DeviationThreshold = 0.20m
            };
            db.BudgetBatches.Add(batch);
            await db.SaveChangesAsync();

            var orgs = await db.Organizations.OrderBy(x => x.Id).ToListAsync();
            var subs = await db.Subjects.OrderBy(x => x.SortOrder).ToListAsync();
            var scopeRows = await db.SubjectDepartmentScopes.ToListAsync();
            var scopeByOrg = scopeRows
                .GroupBy(x => x.OrganizationId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.SubjectId).ToHashSet());

            // 只有这几个科目带「项目支出」示例值，其余科目的项目支出为 0，因此不生成项目支出行
            var projectDemo = new Dictionary<string, decimal>
            {
                ["咨询费"] = 5000m,
                ["专项维修改造费"] = 20000m,
                ["广告费"] = 15000m
            };

            foreach (var org in orgs)
            {
                // 只给本部门适用的科目建基准与明细（日常管理费全部部门、人员费用仅人事部、设施与食堂仅行政管理部、营销仅营销部）
                var applicable = scopeByOrg.TryGetValue(org.Id, out var allowed)
                    ? subs.Where(s => allowed.Contains(s.Id)).ToList()
                    : new List<Subject>();

                for (var i = 0; i < applicable.Count; i++)
                {
                    var subject = applicable[i];
                    // 纯示例金额：决算 11000 起逐行递增，预算为决算的 1.1 倍
                    var actual = 11000m + i * 1000m;
                    var budget = decimal.Round(actual * 1.1m, 2);
                    var projectActual = projectDemo.TryGetValue(subject.Name, out var pa) ? pa : 0m;
                    var projectBudget = decimal.Round(projectActual * 1.2m, 2);

                    db.BaselineRecords.Add(new BaselineRecord
                    {
                        BudgetBatchId = batch.Id,
                        OrganizationId = org.Id,
                        SubjectId = subject.Id,
                        BudgetBaseline = budget,
                        ActualBaseline = actual,
                        BudgetBaselineProject = projectBudget,
                        ActualBaselineProject = projectActual,
                        Version = 1,
                        IsLocked = true,
                        UpdatedBy = "系统初始化"
                    });

                    db.BaselineSnapshots.Add(new BaselineSnapshot
                    {
                        BudgetBatchId = batch.Id,
                        Version = 1,
                        OrganizationId = org.Id,
                        SubjectId = subject.Id,
                        BudgetBaseline = budget,
                        ActualBaseline = actual,
                        BudgetBaselineProject = projectBudget,
                        ActualBaselineProject = projectActual,
                        Operator = "系统初始化"
                    });
                }

                var form = new FilingForm
                {
                    FormNumber = $"BG{batch.PlanYear}-{org.Code}",
                    BudgetBatchId = batch.Id,
                    OrganizationId = org.Id,
                    Status = FormStatus.Draft,
                    VersionNumber = 1
                };
                db.FilingForms.Add(form);
                await db.SaveChangesAsync();

                for (var i = 0; i < applicable.Count; i++)
                {
                    var subject = applicable[i];
                    var actual = 11000m + i * 1000m;
                    var budget = decimal.Round(actual * 1.1m, 2);
                    var projectActual = projectDemo.TryGetValue(subject.Name, out var pa) ? pa : 0m;
                    var projectBudget = decimal.Round(projectActual * 1.2m, 2);

                    db.ReportLines.Add(new ReportLine
                    {
                        FilingFormId = form.Id,
                        SubjectId = subject.Id,
                        ExpenseType = "基本支出",
                        Budget2026 = budget,
                        Actual2026 = actual,
                        // 默认与决算持平，方便直接提交体验流程；改动超阈值才要求填原因
                        Budget2027 = actual,
                        BaselineVersion = 1
                    });

                    if (projectActual > 0m)
                    {
                        db.ReportLines.Add(new ReportLine
                        {
                            FilingFormId = form.Id,
                            SubjectId = subject.Id,
                            ExpenseType = "项目支出",
                            Budget2026 = projectBudget,
                            Actual2026 = projectActual,
                            Budget2027 = projectActual,
                            BaselineVersion = 1
                        });
                    }
                }
                await db.SaveChangesAsync();
            }
        }

        // 已有数据的库也可能新增部门（如「总裁办」），补齐其基准行、单据与明细行
        await EnsureBatchCoverageAsync(db);
    }

    /// <summary>
    /// 幂等补齐：确保每个部门在当前批次都有基准记录、一张填报单，以及适用科目（基本/项目）的明细行。
    /// 新增部门或调整科目范围后无需重建数据库，重启即生效；已有数据不会被覆盖。
    /// </summary>
    private static async Task EnsureBatchCoverageAsync(ApplicationDbContext db)
    {
        var batch = await db.BudgetBatches.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (batch is null) return;

        var organizations = await db.Organizations.OrderBy(x => x.Id).ToListAsync();
        var subjects = await db.Subjects.OrderBy(x => x.SortOrder).ToListAsync();
        var scopes = await db.SubjectDepartmentScopes.ToListAsync();
        var allowedByOrg = scopes
            .GroupBy(x => x.OrganizationId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.SubjectId).ToHashSet());

        var baselineKeys = (await db.BaselineRecords
                .Where(x => x.BudgetBatchId == batch.Id && x.Version == batch.CurrentBaselineVersion)
                .Select(x => new { x.OrganizationId, x.SubjectId })
                .ToListAsync())
            .Select(x => (x.OrganizationId, x.SubjectId))
            .ToHashSet();

        var forms = await db.FilingForms
            .Include(x => x.Lines)
            .Where(x => x.BudgetBatchId == batch.Id)
            .ToListAsync();
        var formByOrg = forms.GroupBy(x => x.OrganizationId).ToDictionary(x => x.Key, x => x.First());

        var changed = false;
        foreach (var org in organizations)
        {
            var subjectIds = allowedByOrg.TryGetValue(org.Id, out var set) ? set : new HashSet<int>();
            var applicable = subjects.Where(x => subjectIds.Contains(x.Id)).ToList();

            foreach (var subject in applicable)
            {
                if (baselineKeys.Contains((org.Id, subject.Id))) continue;
                db.BaselineRecords.Add(new BaselineRecord
                {
                    BudgetBatchId = batch.Id,
                    OrganizationId = org.Id,
                    SubjectId = subject.Id,
                    BudgetBaseline = 0m,
                    ActualBaseline = 0m,
                    BudgetBaselineProject = 0m,
                    ActualBaselineProject = 0m,
                    Version = batch.CurrentBaselineVersion,
                    IsLocked = true,
                    UpdatedBy = "系统初始化"
                });
                baselineKeys.Add((org.Id, subject.Id));
                changed = true;
            }

            if (!formByOrg.TryGetValue(org.Id, out var form))
            {
                form = new FilingForm
                {
                    FormNumber = $"BG{batch.PlanYear}-{org.Code}",
                    BudgetBatchId = batch.Id,
                    OrganizationId = org.Id,
                    Status = FormStatus.Draft,
                    VersionNumber = 1
                };
                db.FilingForms.Add(form);
                await db.SaveChangesAsync();
                formByOrg[org.Id] = form;
                changed = true;
            }

            var lineKeys = form.Lines.Select(x => (x.SubjectId, x.ExpenseType)).ToHashSet();
            foreach (var subject in applicable)
            {
                foreach (var expenseType in new[] { "基本支出", "项目支出" })
                {
                    if (lineKeys.Contains((subject.Id, expenseType))) continue;
                    form.Lines.Add(new ReportLine
                    {
                        SubjectId = subject.Id,
                        ExpenseType = expenseType,
                        Budget2026 = 0m,
                        Actual2026 = 0m,
                        Budget2027 = 0m,
                        BaselineVersion = batch.CurrentBaselineVersion
                    });
                    changed = true;
                }
            }
        }

        if (changed) await db.SaveChangesAsync();
    }

    private static async Task EnsureSubjectDepartmentScopesAsync(ApplicationDbContext db)
    {
        var organizations = await db.Organizations.OrderBy(x => x.Id).ToListAsync();
        var subjects = await db.Subjects.OrderBy(x => x.SortOrder).ToListAsync();

        // 人员费用（工资、社保和公积金、福利费）：仅人事部；设施运营费：行政管理部与总裁办；食堂专项费：仅行政管理部；营销费用：仅营销部
        var personnelDepartments = organizations.Where(x => x.Name is "人事部" or "人事部门").ToList();
        var facilityDepartments = organizations.Where(x => x.Name is "行政管理部" or "总裁办").ToList();
        var canteenDepartments = organizations.Where(x => x.Name == "行政管理部").ToList();
        var marketingDepartments = organizations.Where(x => x.Name == "营销部").ToList();

        // 幂等：只补缺失的「科目 × 部门」组合，已存在的不重复插入
        var existing = (await db.SubjectDepartmentScopes
                .Select(x => new { x.SubjectId, x.OrganizationId })
                .ToListAsync())
            .Select(x => (x.SubjectId, x.OrganizationId))
            .ToHashSet();

        var pending = new List<SubjectDepartmentScope>();
        foreach (var subject in subjects)
        {
            IEnumerable<Organization> applicableOrganizations = subject.Category switch
            {
                "日常管理费" => organizations,
                "人员费用" => personnelDepartments,
                "设施运营费" => facilityDepartments,
                "食堂专项费" => canteenDepartments,
                "营销费用" => marketingDepartments,
                _ => Array.Empty<Organization>()
            };

            foreach (var organization in applicableOrganizations)
            {
                if (existing.Contains((subject.Id, organization.Id))) continue;
                pending.Add(new SubjectDepartmentScope
                {
                    SubjectId = subject.Id,
                    OrganizationId = organization.Id
                });
            }
        }

        if (pending.Count == 0) return;
        db.SubjectDepartmentScopes.AddRange(pending);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureUserAsync(
        UserManager<IdentityUser> userManager,
        ApplicationDbContext db,
        string email,
        string password,
        string role,
        int? organizationId)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            await userManager.CreateAsync(user, password);
        }

        if (!await userManager.IsInRoleAsync(user, role))
            await userManager.AddToRoleAsync(user, role);

        if (organizationId.HasValue &&
            !await db.UserOrganizations.AnyAsync(x => x.UserId == user.Id))
        {
            db.UserOrganizations.Add(new UserOrganization
            {
                UserId = user.Id,
                OrganizationId = organizationId.Value
            });
            await db.SaveChangesAsync();
        }
    }
}
