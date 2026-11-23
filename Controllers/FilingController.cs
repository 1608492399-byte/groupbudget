using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>部门填报员：只能看本部门、只能填 2027 预算数，2026 两列只读。</summary>
[Authorize(Roles = AppRoles.Filler)]
public class FilingController : AppControllerBase
{
    public FilingController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        : base(db, userManager) { }

    public async Task<IActionResult> Index()
    {
        var orgId = await CurrentOrganizationIdAsync();
        if (orgId is null)
        {
            ViewBag.Warning = "当前账号尚未绑定部门，请联系系统管理员在「账号与部门绑定」中设置。";
            return View(new List<FilingForm>());
        }

        // 未下发的批次对部门完全不可见
        var forms = await Db.FilingForms
            .Include(x => x.BudgetBatch)
            .Include(x => x.Organization)
            .Where(x => x.OrganizationId == orgId.Value &&
                        x.BudgetBatch.Status != BatchStatus.Draft &&
                        x.BudgetBatch.Status != BatchStatus.Archived)
            .OrderByDescending(x => x.BudgetBatchId)
            .ToListAsync();

        ViewBag.OrganizationName = (await Db.Organizations.FindAsync(orgId.Value))?.Name;
        return View(forms);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var orgId = await CurrentOrganizationIdAsync();
        var form = await LoadFormAsync(id);
        if (form is null || orgId is null || form.OrganizationId != orgId.Value)
            return Forbid();   // 接口层拒绝越权访问他部门数据

        return View(await BuildViewModelAsync(form, IsReadOnly(form)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FilingEditViewModel vm)
    {
        var orgId = await CurrentOrganizationIdAsync();
        var form = await LoadFormAsync(id);
        if (form is null || orgId is null || form.OrganizationId != orgId.Value)
            return Forbid();

        if (IsReadOnly(form))
        {
            ModelState.AddModelError("", "当前单据状态为「" + form.Status + "」，禁止修改。");
            return View(await BuildViewModelAsync(form, true));
        }

        if (form.BudgetBatch.Status != BatchStatus.Published)
        {
            ModelState.AddModelError("", "批次未开放填报（状态：" + form.BudgetBatch.Status + "），禁止修改。");
            return View(await BuildViewModelAsync(form, true));
        }

        ValidateLines(vm, form);
        await ValidateSubjectDepartmentScopesAsync(form);
        if (!ModelState.IsValid)
            return View(await BuildViewModelAsync(form, false, vm));

        // 只更新 2027 预算数和原因；2026 两列完全以数据库中的快照为准，忽略前端传值
        foreach (var input in vm.Lines)
        {
            var line = form.Lines.FirstOrDefault(x => x.Id == input.ReportLineId);
            if (line is null) continue;
            line.Budget2027 = input.Budget2027;
            line.Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        }

        form.VersionNumber++;
        WriteLog("保存填报", form.FormNumber, $"V{form.VersionNumber}，共 {vm.Lines.Count} 行");
        await Db.SaveChangesAsync();

        TempData["Message"] = "已保存草稿，确认无误后请点「提交审核」。";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var orgId = await CurrentOrganizationIdAsync();
        var form = await LoadFormAsync(id);
        if (form is null || orgId is null || form.OrganizationId != orgId.Value)
            return Forbid();

        if (form.Status != FormStatus.Draft && form.Status != FormStatus.Returned)
        {
            TempData["Error"] = "只有草稿或已打回的单据可以提交。";
            return RedirectToAction(nameof(Edit), new { id });
        }

        if (form.BudgetBatch.Status != BatchStatus.Published)
        {
            TempData["Error"] = "批次未开放填报，不能提交。";
            return RedirectToAction(nameof(Edit), new { id });
        }

        // 提交时重新按数据库数据全量校验一次，不依赖上一次保存
        var vm = await BuildViewModelAsync(form, false);
        ValidateLines(vm, form);
        await ValidateSubjectDepartmentScopesAsync(form);
        if (!ModelState.IsValid)
            return View(nameof(Edit), await BuildViewModelAsync(form, false));

        form.Status = FormStatus.Submitted;
        form.SubmittedAt = DateTime.Now;
        form.SubmittedBy = User.Identity?.Name;
        form.ReviewedAt = null;
        form.ReviewedBy = null;
        form.VersionNumber++;
        SaveVersionSnapshot(form, "提交");
        WriteLog("提交审核", form.FormNumber, $"V{form.VersionNumber}");
        await Db.SaveChangesAsync();

        TempData["Message"] = "已提交，等待集团审核。提交后数据已锁定，不能修改。";
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<FilingForm?> LoadFormAsync(int id)
        => await Db.FilingForms
            .Include(x => x.BudgetBatch)
            .Include(x => x.Organization)
            .Include(x => x.Lines).ThenInclude(x => x.Subject)
            .FirstOrDefaultAsync(x => x.Id == id);

    private static bool IsReadOnly(FilingForm form)
        => form.Status == FormStatus.Submitted || form.Status == FormStatus.Approved ||
           form.BudgetBatch.Status != BatchStatus.Published;

    private async Task ValidateSubjectDepartmentScopesAsync(FilingForm form)
    {
        var subjectIds = form.Lines.Select(x => x.SubjectId).Distinct().ToList();
        var scopes = await Db.SubjectDepartmentScopes
            .Where(x => subjectIds.Contains(x.SubjectId))
            .ToListAsync();
        var scopesBySubject = scopes.GroupBy(x => x.SubjectId)
            .ToDictionary(x => x.Key, x => x.Select(scope => scope.OrganizationId).ToHashSet());

        foreach (var line in form.Lines.GroupBy(x => x.SubjectId).Select(x => x.First()))
        {
            if (scopesBySubject.TryGetValue(line.SubjectId, out var organizationIds) &&
                !organizationIds.Contains(form.OrganizationId))
            {
                ModelState.AddModelError("",
                    $"科目「{line.Subject.Name}」不适用于当前部门，不能保存或提交该单据。");
            }
        }
    }

    private async Task<FilingEditViewModel> BuildViewModelAsync(
        FilingForm form, bool readOnly, FilingEditViewModel? posted = null)
    {
        var subjectIds = form.Lines.Select(x => x.SubjectId).Distinct().ToList();
        var scopes = await Db.SubjectDepartmentScopes
            .Where(x => subjectIds.Contains(x.SubjectId))
            .ToListAsync();
        var configuredSubjectIds = scopes.Select(x => x.SubjectId).ToHashSet();
        var applicableSubjectIds = scopes.Where(x => x.OrganizationId == form.OrganizationId)
            .Select(x => x.SubjectId).ToHashSet();

        var lines = form.Lines
            .Where(line => !configuredSubjectIds.Contains(line.SubjectId) || applicableSubjectIds.Contains(line.SubjectId))
            .OrderBy(x => x.Subject.SortOrder)
            .Select(line =>
            {
                var input = posted?.Lines.FirstOrDefault(x => x.ReportLineId == line.Id);
                var budget2027 = input?.Budget2027 ?? line.Budget2027;
                var reason = input?.Reason ?? line.Reason;
                return new LineViewModel
                {
                    ReportLineId = line.Id,
                    SubjectId = line.SubjectId,
                    ExpenseType = line.ExpenseType,
                    SubjectName = line.Subject.Name,
                    Category = line.Subject.Category,
                    IsRequired = line.Subject.IsRequired,
                    Budget2026 = line.Budget2026,
                    Actual2026 = line.Actual2026,
                    Budget2027 = budget2027,
                    Reason = reason,
                    ChangeAmount = BudgetCalculator.Change(budget2027, line.Actual2026),
                    ChangeRate = BudgetCalculator.Rate(budget2027, line.Actual2026),
                    NeedsReason = BudgetCalculator.NeedsReason(budget2027, line.Actual2026,
                        form.BudgetBatch.DeviationThreshold)
                };
            }).ToList();

        return new FilingEditViewModel
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
            ReadOnly = readOnly,
            ReturnReason = form.ReturnReason,
            Lines = lines
        };
    }

    /// <summary>服务端强制校验：金额合法性、偏差原因、必填科目。</summary>
    private void ValidateLines(FilingEditViewModel vm, FilingForm form)
    {
        foreach (var input in vm.Lines)
        {
            var line = form.Lines.FirstOrDefault(x => x.Id == input.ReportLineId);
            if (line is null)
            {
                ModelState.AddModelError("", "提交数据中包含不存在的明细行，请刷新页面重试。");
                continue;
            }

            if (!BudgetCalculator.IsValidAmount(input.Budget2027))
            {
                ModelState.AddModelError($"Lines[{vm.Lines.IndexOf(input)}].Budget2027",
                    $"「{line.Subject.Name}」的 2027 预算数必须 ≥ 0 且最多两位小数。");
                continue;
            }

            if (line.Subject.IsRequired && input.Budget2027 == 0m)
                ModelState.AddModelError($"Lines[{vm.Lines.IndexOf(input)}].Budget2027",
                    $"「{line.Subject.Name}」为必填科目，金额不能为 0。");

            if (BudgetCalculator.NeedsReason(input.Budget2027, line.Actual2026,
                    form.BudgetBatch.DeviationThreshold) && string.IsNullOrWhiteSpace(input.Reason))
            {
                ModelState.AddModelError($"Lines[{vm.Lines.IndexOf(input)}].Reason",
                    $"「{line.Subject.Name}」与 2026 决算数偏差超过 " +
                    $"{form.BudgetBatch.DeviationThreshold:P0}，必须填写增减原因。");
            }
        }
    }
}
