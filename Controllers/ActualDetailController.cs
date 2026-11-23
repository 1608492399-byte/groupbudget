using System.Text;
using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>
/// 决算明细钻取：从集团汇总表或部门填报页的「上年决算数」点进来，看到凭证级明细。
/// 部门填报员只能看本部门，审核员与管理员可看全部。
/// </summary>
[Authorize(Roles = AppRoles.Admin + "," + AppRoles.Reviewer + "," + AppRoles.Filler)]
public class ActualDetailController : AppControllerBase
{
    private const int PageSize = 50;

    public ActualDetailController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        : base(db, userManager) { }

    [HttpGet]
    public async Task<IActionResult> Index(int? batchId, int? orgId, int? subjectId,
        string? period, string? expenseType, int page = 1)
    {
        var vm = await BuildPageAsync(batchId, orgId, subjectId, period, expenseType, page);
        if (vm is null) return Forbid();
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Export(int? batchId, int? orgId, int? subjectId,
        string? period, string? expenseType)
    {
        var vm = await BuildPageAsync(batchId, orgId, subjectId, period, expenseType, 1, int.MaxValue);
        if (vm is null) return Forbid();

        var sb = new StringBuilder();
        sb.AppendLine("日期,会计期间,凭证号,部门,预算科目,会计科目全名,费用属性,借方金额,调整金额,审定金额,源行号");
        foreach (var row in vm.Rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.VoucherDate?.ToString("yyyy-MM-dd") ?? string.Empty),
                Csv(row.AccountingPeriod),
                Csv(row.VoucherNumber),
                Csv(row.OrganizationName),
                Csv(row.SubjectName),
                Csv(row.AccountingSubjectName),
                Csv(row.ExpenseType),
                row.AccountAmount.ToString("F2"),
                row.AdjustmentAmount.ToString("F2"),
                row.ApprovedAmount.ToString("F2"),
                row.SourceRowNumber.ToString()));
        }
        sb.AppendLine(string.Join(",",
            Csv($"合计（{vm.RecordCount} 笔）"), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, vm.AccountTotal.ToString("F2"),
            vm.AdjustmentTotal.ToString("F2"), vm.ApprovedTotal.ToString("F2"), string.Empty));

        WriteLog("导出决算明细", $"{vm.OrganizationName} / {vm.SubjectName}", $"{vm.RecordCount} 笔，审定合计 {vm.ApprovedTotal:F2}");

        var fileName = $"决算明细_{vm.OrganizationName}_{vm.SubjectName}.csv";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private async Task<ActualDetailPageViewModel?> BuildPageAsync(int? batchId, int? orgId, int? subjectId,
        string? period, string? expenseType, int page, int pageSize = PageSize)
    {
        var batch = batchId.HasValue
            ? await Db.BudgetBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batchId.Value)
            : await Db.BudgetBatches.AsNoTracking().OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (batch is null) return null;

        var locked = false;
        if (User.IsInRole(AppRoles.Filler))
        {
            var own = await CurrentOrganizationIdAsync();
            if (own is null) return null;
            orgId = own;
            locked = true;
        }

        var query = Db.ActualDetails.AsNoTracking()
            .Where(x => x.BudgetBatchId == batch.Id);

        if (orgId.HasValue) query = query.Where(x => x.OrganizationId == orgId.Value);
        if (subjectId.HasValue)
        {
            query = query.Where(x => x.SubjectId == subjectId.Value);
            if (!orgId.HasValue)
            {
                // 未指定部门时只列「该科目适用的部门」，避免出现汇总表里没有的口径（如人员费用归口人事部）
                var applicableOrgIds = await Db.ReportLines.AsNoTracking()
                    .Where(x => x.SubjectId == subjectId.Value && x.FilingForm.BudgetBatchId == batch.Id)
                    .Select(x => x.FilingForm.OrganizationId)
                    .Distinct()
                    .ToListAsync();
                if (applicableOrgIds.Count > 0)
                    query = query.Where(x => applicableOrgIds.Contains(x.OrganizationId));
            }
        }
        if (!string.IsNullOrWhiteSpace(period)) query = query.Where(x => x.AccountingPeriod == period);
        if (!string.IsNullOrWhiteSpace(expenseType)) query = query.Where(x => x.ExpenseType == expenseType);

        var vm = new ActualDetailPageViewModel
        {
            BatchId = batch.Id,
            BatchName = batch.Name,
            BaselineYear = batch.BaselineYear,
            OrganizationId = orgId,
            SubjectId = subjectId,
            Period = period,
            ExpenseType = expenseType,
            OrganizationLocked = locked,
            PageSize = pageSize == int.MaxValue ? 0 : pageSize
        };

        if (orgId.HasValue)
            vm.OrganizationName = await Db.Organizations
                .Where(x => x.Id == orgId.Value).Select(x => x.Name).FirstOrDefaultAsync() ?? "未知部门";

        if (subjectId.HasValue)
        {
            var subject = await Db.Subjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subjectId.Value);
            if (subject is not null) vm.SubjectName = $"{subject.Name}（{subject.Category}）";
        }

        vm.Periods = await Db.ActualDetails.AsNoTracking()            .Where(x => x.BudgetBatchId == batch.Id && (!orgId.HasValue || x.OrganizationId == orgId.Value))
            .Select(x => x.AccountingPeriod)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        vm.RecordCount = await query.CountAsync();
        // SQLite 把 decimal 存成 TEXT，无法在数据库端 SUM，这里取回当前筛选集后求和（单批次最多几千行）
        var sums = await query
            .Select(x => new { x.AccountAmount, x.AdjustmentAmount, x.ApprovedAmount })
            .ToListAsync();
        vm.AccountTotal = sums.Sum(x => x.AccountAmount);
        vm.AdjustmentTotal = sums.Sum(x => x.AdjustmentAmount);
        vm.ApprovedTotal = sums.Sum(x => x.ApprovedAmount);

        vm.Organizations = await Db.Organizations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

        var paged = query;
        if (pageSize != int.MaxValue)
        {
            vm.Page = Math.Max(1, page);
            if (vm.Page > vm.TotalPages) vm.Page = vm.TotalPages;
            paged = query.OrderBy(x => x.VoucherDate).ThenBy(x => x.SourceRowNumber)
                .Skip((vm.Page - 1) * pageSize).Take(pageSize);
        }
        else
        {
            paged = query.OrderBy(x => x.VoucherDate).ThenBy(x => x.SourceRowNumber);
        }

        vm.Rows = await paged
            .Select(x => new ActualDetailRowViewModel
            {
                VoucherDate = x.VoucherDate,
                AccountingPeriod = x.AccountingPeriod,
                VoucherNumber = x.VoucherNumber,
                OrganizationName = x.Organization.Name,
                SubjectName = x.Subject != null ? x.Subject.Name : "（未匹配科目）",
                AccountingSubjectName = x.AccountingSubjectName,
                ExpenseType = x.ExpenseType,
                AccountAmount = x.AccountAmount,
                AdjustmentAmount = x.AdjustmentAmount,
                ApprovedAmount = x.ApprovedAmount,
                SourceRowNumber = x.SourceRowNumber
            })
            .ToListAsync();

        // 勾稽：选中部门 + 科目且未按期间筛选时，取填报单里对应的决算数（年报口径才可比）
        if (orgId.HasValue && subjectId.HasValue && string.IsNullOrWhiteSpace(period))
        {
            var lineQuery = Db.ReportLines.AsNoTracking()
                .Where(x => x.SubjectId == subjectId.Value &&
                            x.FilingForm.BudgetBatchId == batch.Id &&
                            x.FilingForm.OrganizationId == orgId.Value);
            if (!string.IsNullOrWhiteSpace(expenseType))
                lineQuery = lineQuery.Where(x => x.ExpenseType == expenseType);

            if (await lineQuery.AnyAsync())
                vm.LineActual = (await lineQuery.Select(x => x.Actual2026).ToListAsync()).Sum();
        }

        return vm;
    }
}
