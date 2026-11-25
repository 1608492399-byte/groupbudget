using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.Services;
using GroupBudget.ViewModels;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>
/// 管理员专用：预算执行率与增减比例的可视化分析页。
/// 数据口径：执行率分母为 2026 预算（基本+项目），分子为 2026 决算（基本+项目，实际为 1–10 月发生数）；
/// 增减率比较基数为 2026 决算，与填报页、汇总表完全一致，避免同一套数据两个口径。
/// 本页不体现总裁办：其预算为 0、决算未单列，列入任何图表都会失真。
/// </summary>
[Authorize(Roles = AppRoles.Admin)]
public class AnalyticsController : AppControllerBase
{
    /// <summary>总裁办不进本页任何图表与表格。</summary>
    private const string ExcludedDepartmentKeyword = "总裁办";

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

        // 剔除总裁办：后续所有聚合都基于这份名单，图表、表格、合计口径一致。
        var visibleOrganizations = organizations
            .Where(o => !o.Name.Contains(ExcludedDepartmentKeyword, StringComparison.Ordinal))
            .ToList();
        vm.ExcludedDepartments = organizations
            .Where(o => o.Name.Contains(ExcludedDepartmentKeyword, StringComparison.Ordinal))
            .Select(o => o.Name)
            .ToList();

        var visibleIds = visibleOrganizations.Select(o => o.Id).ToHashSet();
        // 合计口径保持全集团（含被排除部门）：否则集团合计与 Excel 总额、汇总表都对不上。
        // 被排除的部门只是不作为独立行出现在图表与表格里。
        var excludedIds = organizations
            .Where(o => !visibleIds.Contains(o.Id))
            .Select(o => o.Id)
            .ToHashSet();

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
        // 明细先按 ActualCaliber 过滤（辞退福利等 431,332.78 元不计入），与源表 S98 逐分对齐。
        var rawDetails = await Db.ActualDetails
            .Where(a => a.BudgetBatchId == batch.Id && a.SubjectId != null)
            .Select(a => new
            {
                a.OrganizationId,
                a.SubjectId,
                a.ExpenseType,
                a.AccountAmount,
                a.AccountingSubjectName
            })
            .ToListAsync();

        var actualDetails = rawDetails
            .Where(a => !ActualCaliber.IsExcluded(
                subjectMap.GetValueOrDefault(a.SubjectId!.Value)?.Name,
                a.AccountingSubjectName,
                a.AccountAmount))
            .ToList();

        const string ProjectType = "项目支出";

        vm.BudgetTotal = baselines.Sum(b => b.BudgetBaseline + b.BudgetBaselineProject);
        vm.ActualTotal = actualDetails.Sum(a => a.AccountAmount);
        vm.DeptActualTotal = baselines.Sum(b => b.ActualBaseline + b.ActualBaselineProject);
        vm.PlanTotal = plans.Sum(p => p.Budget2027);
        // 被排除部门的决算取部门基准口径，与下面的部门表同源，否则脚注金额和部门表对不上
        vm.ExcludedActualTotal = baselines
            .Where(b => excludedIds.Contains(b.OrganizationId))
            .Sum(b => b.ActualBaseline + b.ActualBaselineProject);

        // 基本支出／项目支出各自单列：项目预算不再混进基本口径里一起算执行率。
        vm.BasicBudget = baselines.Sum(b => b.BudgetBaseline);
        vm.ProjectBudget = baselines.Sum(b => b.BudgetBaselineProject);
        vm.BasicActual = actualDetails.Where(a => a.ExpenseType != ProjectType).Sum(a => a.AccountAmount);
        vm.ProjectActual = actualDetails.Where(a => a.ExpenseType == ProjectType).Sum(a => a.AccountAmount);
        vm.BasicPlan = plans.Where(p => p.ExpenseType != ProjectType).Sum(p => p.Budget2027);
        vm.ProjectPlan = plans.Where(p => p.ExpenseType == ProjectType).Sum(p => p.Budget2027);

        // —— 按部门 ——
        foreach (var org in visibleOrganizations)
        {
            var budget = baselines.Where(b => b.OrganizationId == org.Id)
                .Sum(b => b.BudgetBaseline + b.BudgetBaselineProject);
            // 部门决算取基准表：人员费用已归口人事部，非人事部门不应包含人员实际发生数，
            // 否则会出现财务部 592% 这类失真执行率。集团合计另取明细全量。
            var actual = baselines.Where(b => b.OrganizationId == org.Id)
                .Sum(b => b.ActualBaseline + b.ActualBaselineProject);
            var plan = plans.Where(p => p.OrganizationId == org.Id).Sum(p => p.Budget2027);

            if (budget == 0m && actual == 0m && plan == 0m) continue;

            vm.Departments.Add(new ExecutionRow
            {
                Name = org.Name,
                Budget = budget,
                Actual = actual,
                Plan = plan,
                ExecRate = ExecutionAnalysisViewModel.Ratio(actual, budget)
            });

            vm.ChartDepartments.Add(new DepartmentChartData
            {
                Id = org.Id,
                Name = org.Name,
                Budget = budget,
                Actual = actual,
                Plan = plan
            });
        }

        vm.Departments = vm.Departments
            .OrderByDescending(d => d.Budget)
            .ThenByDescending(d => d.Actual)
            .ToList();

        // 柱状图只对照 2026 预算与 2027 预算，按 2026 预算由高到低
        vm.ChartDepartments = vm.ChartDepartments
            .OrderByDescending(d => d.Budget)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        // —— 按预算科目 ——
        foreach (var subject in subjects)
        {
            var budget = baselines.Where(b => b.SubjectId == subject.Id)
                .Sum(b => b.BudgetBaseline + b.BudgetBaselineProject);
            var actual = actualDetails.Where(a => a.SubjectId == subject.Id).Sum(a => a.AccountAmount);
            var plan = plans.Where(p => p.SubjectId == subject.Id).Sum(p => p.Budget2027);

            if (budget == 0m && actual == 0m && plan == 0m) continue;

            vm.Subjects.Add(new ExecutionRow
            {
                Name = subject.Name,
                Note = subject.Category,
                Budget = budget,
                Actual = actual,
                Plan = plan,
                ExecRate = ExecutionAnalysisViewModel.Ratio(actual, budget)
            });
        }

        vm.Subjects = vm.Subjects
            .OrderByDescending(s => s.Actual)
            .ThenByDescending(s => s.Budget)
            .ToList();

        // —— 饼图：2026 决算按预算科目构成，占比过小的并入「其他」 ——
        const int PieCoreCount = 7;
        var pieSource = vm.Subjects.Where(s => s.Actual > 0m).OrderByDescending(s => s.Actual).ToList();
        var pieTotal = pieSource.Sum(s => s.Actual);
        if (pieTotal <= 0m)
        {
            // 决算尚未归集时退回到预算口径，避免空白饼图
            pieSource = vm.Subjects.Where(s => s.Budget > 0m).OrderByDescending(s => s.Budget).ToList();
            pieTotal = pieSource.Sum(s => s.Budget);
            vm.PieBasis = "预算";
        }

        foreach (var slice in pieSource.Take(PieCoreCount))
        {
            vm.PieSlices.Add(new PieSlice
            {
                Name = slice.Name,
                Amount = vm.PieBasis == "预算" ? slice.Budget : slice.Actual
            });
        }

        if (pieSource.Count > PieCoreCount)
        {
            var rest = pieSource.Skip(PieCoreCount)
                .Sum(s => vm.PieBasis == "预算" ? s.Budget : s.Actual);
            if (rest > 0m)
            {
                vm.PieSlices.Add(new PieSlice { Name = "其他科目", Amount = rest, IsOther = true });
            }
        }

        vm.PieTotal = pieTotal;
        foreach (var slice in vm.PieSlices)
        {
            slice.Share = ExecutionAnalysisViewModel.Ratio(slice.Amount, pieTotal);
        }

        vm.InsightSummary = BuildInsight(vm, batch);
        vm.CaliberNote = ActualCaliber.Note;

        return View(vm);
    }

    /// <summary>首页第一段文字总结：执行率、差额、最大科目、次年填报进度，全部由数据算出。</summary>
    private static string BuildInsight(ExecutionAnalysisViewModel vm, BudgetBatch batch)
    {
        var n2 = CultureInfo.InvariantCulture;
        var rate = vm.ExecRate.HasValue
            ? (vm.ExecRate.Value * 100m).ToString("0.0", n2) + "%"
            : "无法计算";
        var gap = vm.ActualTotal - vm.BudgetTotal;
        var gapText = Math.Abs(gap).ToString("N2", n2);
        var gapWord = gap < 0m ? "低于" : "高于";

        var top = vm.Subjects.Where(s => s.Actual > 0m).OrderByDescending(s => s.Actual).FirstOrDefault();
        var topText = top == null
            ? string.Empty
            : $"决算金额最高的预算科目为「{top.Name}」，{top.Actual.ToString("N2", n2)} 元，占决算总额 {(ExecutionAnalysisViewModel.Ratio(top.Actual, vm.ActualTotal) ?? 0m) * 100m:0.0}%。";

        var planText = vm.HasPlan
            ? $"次年（{batch.PlanYear}）预算已填报 {vm.PlanTotal.ToString("N2", n2)} 元，较 {batch.BaselineYear} 年决算{(vm.PlanChangeAmount >= 0m ? "增加" : "减少")} {Math.Abs(vm.PlanChangeAmount).ToString("N2", n2)} 元。"
            : $"次年（{batch.PlanYear}）预算尚未开始填报，增减对照暂不可用。";

        var overText = vm.Departments.Count(d => d.ExecRate > 1m) is var over && over > 0
            ? $"有 {over} 个部门的决算超过预算。"
            : "各部门决算均未超预算。";

        return $"{batch.BaselineYear} 年集团预算 {vm.BudgetTotal.ToString("N2", n2)} 元，决算 {vm.ActualTotal.ToString("N2", n2)} 元，" +
               $"执行率 {rate}，决算{gapWord}预算 {gapText} 元。{overText}{topText}{planText}";
    }
}
