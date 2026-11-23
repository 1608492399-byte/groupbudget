using System.Text;
using System.Globalization;
using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>
/// 导出：① 预决算汇总（部门内基本支出 / 项目支出分块，各自小计）；② 表2 部门项目支出预算表。
/// 统一输出 UTF-8 BOM 的 CSV，Excel 直接打开不乱码。
/// </summary>
[Authorize(Roles = AppRoles.Reviewer + "," + AppRoles.Admin)]
public class ExportController : Controller
{
    private const string BasicType = "基本支出";
    private const string ProjectType = "项目支出";

    private readonly ApplicationDbContext _db;
    public ExportController(ApplicationDbContext db) => _db = db;

    /// <summary>导出入口页。</summary>
    [HttpGet]
    public async Task<IActionResult> Index(int batchId = 0)
    {
        var batches = await _db.BudgetBatches.AsNoTracking()
            .OrderByDescending(x => x.PlanYear).ThenByDescending(x => x.Id)
            .ToListAsync();

        var batch = batches.FirstOrDefault(x => x.Id == batchId) ?? batches.FirstOrDefault();
        var vm = new ExportIndexViewModel
        {
            Batches = batches,
            BatchId = batch?.Id ?? 0,
            BatchName = batch?.Name ?? "（暂无批次）"
        };

        if (batch is not null)
        {
            var lines = await _db.ReportLines.AsNoTracking()
                .Where(x => x.FilingForm.BudgetBatchId == batch.Id)
                .ToListAsync();

            vm.LineCount = lines.Count;
            vm.Budget2026Total = lines.Sum(x => x.Budget2026);
            vm.Actual2026Total = lines.Sum(x => x.Actual2026);
            vm.Budget2027Total = lines.Sum(x => x.Budget2027);
            vm.ProjectSubjectCount = lines.Where(x => x.ExpenseType == ProjectType)
                .Select(x => x.SubjectId).Distinct().Count();
            vm.DepartmentCount = await _db.Organizations.AsNoTracking().CountAsync();
        }

        return View("All", vm);
    }

    /// <summary>预决算汇总：部门内先基本支出、后项目支出，各自小计，再部门合计与集团总计。</summary>
    [HttpGet]
    public async Task<IActionResult> All(int batchId = 0)
    {
        var query = _db.ReportLines
            .Include(x => x.Subject)
            .Include(x => x.FilingForm).ThenInclude(x => x.Organization)
            .Include(x => x.FilingForm).ThenInclude(x => x.BudgetBatch)
            .AsQueryable();

        if (batchId > 0)
            query = query.Where(x => x.FilingForm.BudgetBatchId == batchId);

        var lines = await query
            .OrderBy(x => x.FilingForm.OrganizationId)
            .ThenByDescending(x => x.FilingForm.BudgetBatch.PlanYear)
            .ThenBy(x => x.Subject.SortOrder)
            .AsNoTracking()
            .ToListAsync();

        var sb = new StringBuilder();
        AppendCsvRow(sb, "部门", "费用类别", "科目", "2026预算数", "2026决算数", "2027预算数", "增减额", "增幅", "增减原因", "单据状态");

        var basicAll = new List<ReportLine>();
        var projectAll = new List<ReportLine>();

        foreach (var department in lines.GroupBy(x => new
                 {
                     x.FilingForm.OrganizationId,
                     x.FilingForm.Organization.Name
                 }).OrderBy(x => x.Key.OrganizationId))
        {
            var deptLines = department.ToList();
            var basics = deptLines.Where(x => x.ExpenseType == BasicType).ToList();
            var projects = deptLines.Where(x => x.ExpenseType == ProjectType).ToList();
            basicAll.AddRange(basics);
            projectAll.AddRange(projects);

            AppendSectionRow(sb, "一、基本支出");
            AppendDetailRows(sb, basics);
            AppendSummaryRow(sb, "基本支出小计", basics);

            AppendSectionRow(sb, "二、项目支出");
            AppendDetailRows(sb, projects);
            AppendSummaryRow(sb, "项目支出小计", projects);

            AppendSummaryRow(sb, "部门合计", deptLines);
        }

        AppendSummaryRow(sb, "集团基本支出合计", basicAll);
        AppendSummaryRow(sb, "集团项目支出合计", projectAll);
        AppendSummaryRow(sb, "集团总计", lines);

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var fileName = $"预决算汇总_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    /// <summary>表2：行=项目支出科目，列=各部门，值=该部门该科目 2027 预算数。</summary>
    [HttpGet]
    public Task<IActionResult> Project(int batchId = 0) => ExportProjectAsync(batchId);

    // 保留旧动作名，避免已有直达链接失效。
    [HttpGet]
    public Task<IActionResult> ProjectTable(int batchId = 0) => ExportProjectAsync(batchId);

    private async Task<IActionResult> ExportProjectAsync(int batchId)
    {
        var batchQuery = _db.BudgetBatches.AsNoTracking();
        var batch = batchId > 0
            ? await batchQuery.FirstOrDefaultAsync(x => x.Id == batchId)
            : await batchQuery.OrderByDescending(x => x.PlanYear).ThenByDescending(x => x.Id).FirstOrDefaultAsync();

        var query = _db.ReportLines
            .Include(x => x.Subject)
            .Include(x => x.FilingForm).ThenInclude(x => x.Organization)
            .Where(x => x.ExpenseType == ProjectType);

        if (batch is not null)
            query = query.Where(x => x.FilingForm.BudgetBatchId == batch.Id);

        var lines = await query.AsNoTracking().ToListAsync();
        var organizations = await _db.Organizations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

        var subjects = lines
            .GroupBy(x => new { x.SubjectId, x.Subject.Name, x.Subject.Category, x.Subject.SortOrder })
            .OrderBy(g => g.Key.SortOrder)
            .Select(g => new
            {
                g.Key.SubjectId,
                g.Key.Name,
                g.Key.Category,
                Amounts = organizations.ToDictionary(
                    o => o.Id,
                    o => g.Where(x => x.FilingForm.OrganizationId == o.Id).Sum(x => x.Budget2027))
            })
            .ToList();

        var sb = new StringBuilder();
        AppendCsvRow(sb, "2027年度部门项目支出预算表");
        AppendCsvRow(sb, "编制单位：光华集团　　单位：元");
        AppendCsvRow(sb, new[] { "序号", "费用类别", "项目支出科目" }
            .Concat(organizations.Select(o => o.Name))
            .Concat(new[] { "合计" }).ToArray());

        var index = 0;
        var columnTotals = organizations.ToDictionary(o => o.Id, _ => 0m);
        var grandTotal = 0m;

        foreach (var subject in subjects)
        {
            index++;
            var cells = new List<string> { index.ToString(), subject.Category ?? "", subject.Name };
            var rowTotal = 0m;
            foreach (var org in organizations)
            {
                var value = subject.Amounts.TryGetValue(org.Id, out var v) ? v : 0m;
                rowTotal += value;
                columnTotals[org.Id] += value;
                cells.Add(value == 0m ? "" : Money(value));
            }
            cells.Add(Money(rowTotal));
            grandTotal += rowTotal;
            AppendCsvRow(sb, cells.ToArray());
        }

        var totalCells = new List<string> { "", "", "合计" };
        foreach (var org in organizations)
            totalCells.Add(columnTotals[org.Id] == 0m ? "" : Money(columnTotals[org.Id]));
        totalCells.Add(Money(grandTotal));
        AppendCsvRow(sb, totalCells.ToArray());

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var fileName = $"项目支出预算表_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static void AppendDetailRows(StringBuilder sb, IReadOnlyCollection<ReportLine> lines)
    {
        foreach (var line in lines.OrderBy(x => x.Subject.SortOrder))
        {
            var change = BudgetCalculator.Change(line.Budget2027, line.Actual2026);
            var rate = BudgetCalculator.Rate(line.Budget2027, line.Actual2026);
            AppendCsvRow(sb,
                line.FilingForm.Organization.Name,
                line.Subject.Category ?? "",
                line.Subject.Name,
                Money(line.Budget2026),
                Money(line.Actual2026),
                Money(line.Budget2027),
                Money(change),
                Percent(rate),
                line.Reason ?? "",
                line.FilingForm.Status);
        }
    }

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>百分比：InvariantCulture 会在数字与 % 之间加空格，去掉后 Excel 更整洁。</summary>
    private static string Percent(decimal? value)
        => value.HasValue ? value.Value.ToString("P2", CultureInfo.InvariantCulture).Replace(" ", "") : "";

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    /// <summary>块标题行：只占第一列，其余留空，便于在 Excel 里看层级。</summary>
    private static void AppendSectionRow(StringBuilder sb, string label)
        => AppendCsvRow(sb, new[] { label, "", "", "", "", "", "", "", "", "" });

    private static void AppendSummaryRow(StringBuilder sb, string label, IReadOnlyCollection<ReportLine> lines)
    {
        var budget2026 = lines.Sum(x => x.Budget2026);
        var actual2026 = lines.Sum(x => x.Actual2026);
        var budget2027 = lines.Sum(x => x.Budget2027);
        var change = lines.Sum(x => BudgetCalculator.Change(x.Budget2027, x.Actual2026));
        var rate = actual2026 == 0m ? "" : Percent(change / actual2026);

        AppendCsvRow(sb, label, "", "",
            Money(budget2026), Money(actual2026), Money(budget2027),
            Money(change), rate, "", "");
    }

    private static void AppendCsvRow(StringBuilder sb, params string[] cells)
        => sb.Append(string.Join(",", cells.Select(Escape))).Append("\r\n");
}
