using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>集团审核：可看全部部门，只能写审核结论，不能改任何数字。</summary>
[Authorize(Roles = AppRoles.Reviewer + "," + AppRoles.Admin)]
public class ReviewController : AppControllerBase
{
    public ReviewController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        : base(db, userManager) { }

    public async Task<IActionResult> Index(string? status = null)
    {
        var query = Db.FilingForms
            .Include(x => x.BudgetBatch)
            .Include(x => x.Organization)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var forms = await query
            .OrderBy(x => x.Status == FormStatus.Submitted ? 0 : 1)
            .ThenBy(x => x.OrganizationId)
            .ToListAsync();

        var batches = await Db.BudgetBatches
            .AsNoTracking()
            .OrderByDescending(x => x.PlanYear)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        ViewBag.CurrentStatus = status;
        ViewBag.Batches = batches;
        ViewBag.CurrentBatchId = batches.FirstOrDefault(x => x.Status == BatchStatus.Published)?.Id
            ?? batches.FirstOrDefault()?.Id ?? 0;
        return View(forms);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var form = await Db.FilingForms
            .Include(x => x.BudgetBatch)
            .Include(x => x.Organization)
            .Include(x => x.Lines).ThenInclude(x => x.Subject)
            .Include(x => x.ReviewRecords)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (form is null) return NotFound();

        var vm = new FilingEditViewModel
        {
            FilingFormId = form.Id,
            BatchId = form.BudgetBatchId,
            OrganizationId = form.OrganizationId,
            FormNumber = form.FormNumber,
            BatchName = form.BudgetBatch.Name,
            PlanYear = form.BudgetBatch.PlanYear,
            BaselineYear = form.BudgetBatch.BaselineYear,
            OrganizationName = form.Organization.Name,
            Status = form.Status,
            DeviationThreshold = form.BudgetBatch.DeviationThreshold,
            VersionNumber = form.VersionNumber,
            ReadOnly = true,
            ReturnReason = form.ReturnReason,
            Lines = form.Lines.OrderBy(x => x.Subject.SortOrder).Select(line => new LineViewModel
            {
                ReportLineId = line.Id,
                SubjectId = line.SubjectId,
                SubjectName = line.Subject.Name,
                Category = line.Subject.Category,
                IsRequired = line.Subject.IsRequired,
                Budget2026 = line.Budget2026,
                Actual2026 = line.Actual2026,
                Budget2027 = line.Budget2027,
                Reason = line.Reason,
                ChangeAmount = BudgetCalculator.Change(line.Budget2027, line.Actual2026),
                ChangeRate = BudgetCalculator.Rate(line.Budget2027, line.Actual2026),
                NeedsReason = BudgetCalculator.NeedsReason(line.Budget2027, line.Actual2026,
                    form.BudgetBatch.DeviationThreshold)
            }).ToList()
        };

        ViewBag.Reviews = form.ReviewRecords.OrderByDescending(x => x.ReviewedAt).ToList();
        // 历史快照只读 JSON 中冻结的金额；科目名称通过 Subject 表查询，不使用当前明细替代历史值。
        ViewBag.FormVersions = await Db.FormVersions
            .AsNoTracking()
            .Where(x => x.FilingFormId == form.Id)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
        ViewBag.SubjectNames = await Db.Subjects
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var form = await LoadFormAsync(id);
        if (form is null || form.Status != FormStatus.Submitted)
        {
            TempData["Error"] = "只有「已提交」状态的单据可以审核通过。";
            return RedirectToAction(nameof(Details), new { id });
        }

        var now = DateTime.Now;
        form.Status = FormStatus.Approved;
        form.ReviewedAt = now;
        form.ReviewedBy = User.Identity?.Name;
        form.VersionNumber++;
        Db.ReviewRecords.Add(new ReviewRecord
        {
            FilingFormId = form.Id,
            ReviewerName = User.Identity?.Name ?? "集团审核员",
            Decision = FormStatus.Approved,
            VersionNumber = form.VersionNumber,
            ReviewedAt = now
        });
        SaveVersionSnapshot(form, "审核通过");
        WriteLog("审核通过", form.FormNumber);
        await Db.SaveChangesAsync();

        TempData["Message"] = "已审核通过，单据永久锁定。";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int id, string? comments)
    {
        var form = await LoadFormAsync(id);
        if (form is null || form.Status != FormStatus.Submitted)
        {
            TempData["Error"] = "只有「已提交」状态的单据可以打回。";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (string.IsNullOrWhiteSpace(comments))
        {
            TempData["Error"] = "打回必须填写审核意见。";
            return RedirectToAction(nameof(Details), new { id });
        }

        var now = DateTime.Now;
        form.Status = FormStatus.Returned;
        form.ReviewedAt = now;
        form.ReviewedBy = User.Identity?.Name;
        form.ReturnReason = comments.Trim();
        form.VersionNumber++;
        Db.ReviewRecords.Add(new ReviewRecord
        {
            FilingFormId = form.Id,
            ReviewerName = User.Identity?.Name ?? "集团审核员",
            Decision = FormStatus.Returned,
            Comments = comments.Trim(),
            VersionNumber = form.VersionNumber,
            ReviewedAt = now
        });
        SaveVersionSnapshot(form, "打回", comments.Trim());
        WriteLog("打回", form.FormNumber, comments.Trim());
        await Db.SaveChangesAsync();

        TempData["Message"] = "已打回，部门可修改后重新提交，历史版本已留痕。";
        return RedirectToAction(nameof(Index));
    }

    private Task<FilingForm?> LoadFormAsync(int id)
        => Db.FilingForms
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id)!;
}
