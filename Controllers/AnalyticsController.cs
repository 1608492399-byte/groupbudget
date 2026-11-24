using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>
/// 管理员专用：预算执行率与增减比例的可视化分析页。
/// 数据口径：执行率分母为 2026 预算（基本+项目），分子为 2026 决算（基本+项目，实际为 1–10 月发生数）；
/// 增减率比较基数为 2026 决算，与填报页、汇总表完全一致，避免同一套数据两个口径。
/// </summary>
[Authorize(Roles = AppRoles.Admin)]
public class AnalyticsController : AppControllerBase
{
    public AnalyticsController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        : base(db, userManager)
    {
    }

    public async Task<IActionResult> Index(int? batchId = null)
    {
        var batch = batchId.HasValue
            ? await Db.BudgetBatches.FirstOrDefaultAsync(b => b.Id == batchId.Value)
            : await Db.BudgetBatches.OrderByDescending(b => b.Id).FirstOrDefaultAsync();

        var vm = new ExecutionAnalysisViewModel { Batch = batch };
        if (batch == null)
        {
            return View(vm);
        }

        var organizations = await Db.Organizations
            .OrderBy(o => o.Id)
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();

        var subjects = await Db.Subjects
            .Select(s => new { s.Id, s.Name, s.Category })
            .ToListAsync();
        var subjectMap = subjects.ToDictionary(s => s.Id);

        // SQLite 下 decimal 存 TEXT，聚合只能在内存里做
        var baselines = await Db.BaselineRecords
            .Where(b => b.BudgetBatchId == batch.Id && b.Version == batch.CurrentBaselineVersion)
            .Select(b => new
            {
                b.OrganizationId,
                b.SubjectId,
                b.BudgetBaseline,
                b.ActualBaseline,
                b.BudgetBaselineProject,
                b.ActualBaselineProject
            })
            .ToListAsync();

        var plans = await Db.ReportLines
            .Where(l => l.FilingForm!.BudgetBatchId == batch.Id)
            .Select(l => new { l.SubjectId, l.Budget2027, l.ExpenseType, l.FilingForm!.OrganizationId })
            .ToListAsync();

        // 集团口径的决算取「实际发生数」明细全量：基准表里人员费用已归口人事部，
        // 直接汇总基准会让集团合计少掉其他部门的人员费用（约 1,234 万），执行率严重偏低。
        var actualDetails = await Db.ActualDetails
            .Where(a => a.BudgetBatchId == batch.Id && a.SubjectId != null)
            .Select(a => new { a.SubjectId, a.ExpenseType, a.AccountAmount })
            .ToListAsync();

        const string ProjectType = "项目支出";

        vm.BudgetTotal = baselines.Sum(b => b.BudgetBaseline + b.BudgetBaselineProject);
        vm.ActualTotal = actualDetails.Sum(a => a.AccountAmount);
        vm.DeptActualTotal = baselines.Sum(b => b.ActualBaseline + b.ActualBaselineProject);
        vm.PlanTotal = plans.Sum(p => p.Budget2027);

        // 基本支出／项目支出各自单列：项目预算不再混进基本口径里一起算执行率。
        vm.BasicBudget = baselines.Sum(b => b.BudgetBaseline);
        vm.ProjectBudget = baselines.Sum(b => b.BudgetBaselineProject);
        vm.BasicActual = actualDetails.Where(a => a.ExpenseType != ProjectType).Sum(a => a.AccountAmount);
        vm.ProjectActual = actualDetails.Where(a => a.ExpenseType == ProjectType).Sum(a => a.AccountAmount);
        vm.BasicPlan = plans.Where(p => p.ExpenseType != ProjectType).Sum(p => p.Budget2027);
        vm.ProjectPlan = plans.Where(p => p.ExpenseType == ProjectType).Sum(p => p.Budget2027);

        // —— 按部门 ——
        var deptRows = new List<ExecutionRow>();
        foreach (var org in organizations)
        {
            var budget = baselines.Where(b => b.OrganizationId == org.Id)
                .Sum(b => b.BudgetBaseline + b.BudgetBaselineProject);
            var actual = baselines.Where(b => b.OrganizationId == org.Id)
                .Sum(b => b.ActualBaseline + b.ActualBaselineProject);
            var plan = plans.Where(p => p.OrganizationId == org.Id).Sum(p => p.Budget2027);

            if (budget == 0m && actual == 0m && plan == 0m) continue;

            var row = new ExecutionRow
            {
                Name = org.Name,
                Budget = budget,
                Actual = actual,
                Plan = plan,
                ExecRate = ExecutionAnalysisViewModel.Ratio(actual, budget)
            };

            // 本年预算为 0 的部门（如总裁办）执行率没有分母、增减比例恒为 −100%，
            // 放进图表只会拉成一根空白条，因此只保留在明细表。
            if (budget == 0m) vm.ChartExcludedDepartments.Add(row);
            else deptRows.Add(row);
        }

        vm.ChartExcludedDepartments = vm.ChartExcludedDepartments
            .OrderByDescending(d => d.Actual)
            .ToList();

        vm.Departments = deptRows.OrderByDescending(d => d.Budget).ThenByDescending(d => d.Actual).ToList();

        // —— 按费用类别 ——
        var catRows = new List<ExecutionRow>();
        foreach (var group in baselines.GroupBy(b => subjectMap.TryGetValue(b.SubjectId, out var s) ? s.Category : null))
        {
            var category = group.Key;
            if (string.IsNullOrEmpty(category)) continue;

            var budget = group.Sum(b => b.BudgetBaseline + b.BudgetBaselineProject);

            var subjectIds = group.Select(b => b.SubjectId).Distinct().ToHashSet();
            var actual = actualDetails.Where(a => subjectIds.Contains(a.SubjectId!.Value)).Sum(a => a.AccountAmount);
            var plan = plans.Where(p => subjectIds.Contains(p.SubjectId)).Sum(p => p.Budget2027);

            catRows.Add(new ExecutionRow
            {
                Name = category,
                Budget = budget,
                Actual = actual,
                Plan = plan,
                ExecRate = ExecutionAnalysisViewModel.Ratio(actual, budget)
            });
        }

        vm.Categories = catRows.OrderByDescending(c => c.Budget).ToList();

        // —— 按科目：增减额绝对值前 10 ——
        var subjectRows = new List<ExecutionRow>();
        foreach (var subject in subjects)
        {
            var budget = baselines.Where(b => b.SubjectId == subject.Id)
                .Sum(b => b.BudgetBaseline + b.BudgetBaselineProject);
            var actual = actualDetails.Where(a => a.SubjectId == subject.Id).Sum(a => a.AccountAmount);
            var plan = plans.Where(p => p.SubjectId == subject.Id).Sum(p => p.Budget2027);

            if (budget == 0m && actual == 0m && plan == 0m) continue;

            subjectRows.Add(new ExecutionRow
            {
                Name = subject.Name,
                Note = subject.Category,
                Budget = budget,
                Actual = actual,
                Plan = plan,
                ExecRate = ExecutionAnalysisViewModel.Ratio(actual, budget)
            });
        }

        vm.OverBudgetSubjects = subjectRows.Count(s => s.ExecRate > 1m);

        vm.TopSubjects = subjectRows
            .Where(s => s.ChangeAmount != 0m)
            .OrderByDescending(s => Math.Abs(s.ChangeAmount))
            .Take(10)
            .ToList();

        return View(vm);
    }
}
