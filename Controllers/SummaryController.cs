using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>集团汇总表：按「表1-部门支出预算总表」的版式展示全部部门填报结果。</summary>
[Authorize(Roles = AppRoles.Reviewer + "," + AppRoles.Admin)]
public class SummaryController : AppControllerBase
{
    public SummaryController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        : base(db, userManager)
    {
    }

    public async Task<IActionResult> Index(int? batchId)
    {
        var batches = await Db.BudgetBatches
            .OrderByDescending(x => x.Id)
            .Select(x => new BatchListItem
            {
                Id = x.Id,
                Name = x.Name,
                PlanYear = x.PlanYear,
                BaselineYear = x.BaselineYear,
                Status = x.Status
            })
            .ToListAsync();

        var batch = batchId.HasValue
            ? await Db.BudgetBatches.FirstOrDefaultAsync(x => x.Id == batchId.Value)
            : await Db.BudgetBatches.OrderByDescending(x => x.Id).FirstOrDefaultAsync();

        var vm = new BudgetSummaryViewModel { Batches = batches };
        if (batch is null)
        {
            return View(vm);
        }

        vm.BatchId = batch.Id;
        vm.BatchName = batch.Name;
        vm.PlanYear = batch.PlanYear;
        vm.BaselineYear = batch.BaselineYear;
        vm.Organizations = await Db.Organizations.OrderBy(x => x.Id).ToListAsync();

        var subjects = await Db.Subjects
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();

        // 一次取全批次明细，避免逐科目查询
        var lines = await Db.ReportLines
            .Where(x => x.FilingForm.BudgetBatchId == batch.Id)
            .Select(x => new
            {
                x.SubjectId,
                x.ExpenseType,
                x.Budget2027,
                x.Budget2026,
                x.Actual2026,
                OrganizationId = x.FilingForm.OrganizationId
            })
            .ToListAsync();

        var sequenceByCategory = new Dictionary<string, int>();

        foreach (var subject in subjects)
        {
            var subjectLines = lines.Where(x => x.SubjectId == subject.Id).ToList();
            var category = subject.Category ?? "其他";

            var row = new BudgetSummaryRow
            {
                Category = category,
                SubjectId = subject.Id,
                SubjectName = subject.Name,
                Code = subject.Code,
                BasicTotal = subjectLines.Where(x => x.ExpenseType == "基本支出").Sum(x => x.Budget2027),
                ProjectTotal = subjectLines.Where(x => x.ExpenseType == "项目支出").Sum(x => x.Budget2027),
                Budget2026Total = subjectLines.Sum(x => x.Budget2026),
                Actual2026Total = subjectLines.Sum(x => x.Actual2026)
            };

            foreach (var org in vm.Organizations)
            {
                row.BasicByOrganization[org.Id] = subjectLines
                    .Where(x => x.OrganizationId == org.Id && x.ExpenseType == "基本支出")
                    .Sum(x => x.Budget2027);
                row.ProjectByOrganization[org.Id] = subjectLines
                    .Where(x => x.OrganizationId == org.Id && x.ExpenseType == "项目支出")
                    .Sum(x => x.Budget2027);
            }

            sequenceByCategory.TryGetValue(category, out var seq);
            row.Sequence = seq + 1;
            sequenceByCategory[category] = row.Sequence;

            vm.Rows.Add(row);
        }

        var total = new BudgetSummaryRow { SubjectName = "预算金额合计" };
        foreach (var org in vm.Organizations)
        {
            total.BasicByOrganization[org.Id] = vm.Rows.Sum(x => x.BasicByOrganization[org.Id]);
            total.ProjectByOrganization[org.Id] = vm.Rows.Sum(x => x.ProjectByOrganization[org.Id]);
        }
        total.BasicTotal = vm.Rows.Sum(x => x.BasicTotal);
        total.ProjectTotal = vm.Rows.Sum(x => x.ProjectTotal);
        total.Budget2026Total = vm.Rows.Sum(x => x.Budget2026Total);
        total.Actual2026Total = vm.Rows.Sum(x => x.Actual2026Total);
        vm.TotalRow = total;

        return View(vm);
    }
}
