using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.Services;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GroupBudget.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class ActualImportController : AppControllerBase
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly IMemoryCache _cache;
    private readonly ActualExcelImportService _importService;
    private readonly MappingExcelImportService _mappingImportService;
    private readonly ExecutionImportService _executionService;

    public ActualImportController(
        ApplicationDbContext db,
        UserManager<IdentityUser> userManager,
        IMemoryCache cache,
        ActualExcelImportService importService,
        MappingExcelImportService mappingImportService,
        ExecutionImportService executionService)
        : base(db, userManager)
    {
        _cache = cache;
        _importService = importService;
        _mappingImportService = mappingImportService;
        _executionService = executionService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? batchId = null)
    {
        var model = new ActualImportPageViewModel
        {
            Batches = await Db.BudgetBatches.OrderByDescending(x => x.PlanYear).ToListAsync(),
            Subjects = await Db.Subjects.OrderBy(x => x.SortOrder).ToListAsync(),
            SubjectMappings = await Db.SubjectMappings.Include(x => x.Subject)
                .OrderBy(x => x.AccountingSubjectName).ToListAsync(),
            SelectedBatchId = batchId,
            Message = TempData["Message"] as string
        };
        model.HasSubjectMappings = model.SubjectMappings.Count > 0;
        return View("ActualImport", model);
    }

    /// <summary>下载决算导入模板（按附表7 列结构，含部门与预算科目下拉）。</summary>
    [HttpGet]
    public async Task<IActionResult> DownloadTemplate()
    {
        var departments = await Db.Organizations.OrderBy(x => x.Id).Select(x => x.Name).ToListAsync();
        var subjects = await Db.Subjects.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        var bytes = ExcelTemplateBuilder.BuildActualTemplate(departments, subjects);
        return File(bytes, XlsxContentType, $"附表7决算导入模板_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    /// <summary>下载会计科目映射模板（含预算科目清单，便于批量填写）。</summary>
    [HttpGet]
    public async Task<IActionResult> DownloadMappingTemplate()
    {
        var subjects = await Db.Subjects.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        var bytes = ExcelTemplateBuilder.BuildMappingTemplate(subjects);
        return File(bytes, XlsxContentType, $"会计科目映射模板_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    /// <summary>批量导入会计科目映射；同一会计科目重复导入按更新处理。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> ImportSubjectMappings(IFormFile? file)
    {
        var fileName = SafeFileName(file?.FileName);
        var redirect = Url.Action(nameof(Index)) + "#subject-mapping-config";

        if (file is null || file.Length == 0)
        {
            TempData["MappingMessage"] = "请选择映射文件。";
            return Redirect(redirect);
        }

        if (file.Length > MaxUploadBytes ||
            !(Path.GetExtension(fileName).Equals(".xls", StringComparison.OrdinalIgnoreCase) ||
              Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)))
        {
            TempData["MappingMessage"] = "仅支持不超过 20 MB 的 .xls 或 .xlsx 文件。";
            return Redirect(redirect);
        }

        try
        {
            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            var result = await _mappingImportService.ImportAsync(memory.ToArray());

            if (result.Succeeded == 0)
            {
                TempData["MappingMessage"] = "没有写入任何映射，请根据下方错误清单修正后重试。";
                TempData["MappingErrors"] = string.Join(Environment.NewLine, result.Errors.Take(50));
                return Redirect(redirect);
            }

            WriteLog("导入会计科目映射", fileName,
                $"共 {result.TotalRows} 行，新增 {result.Inserted} 条，更新 {result.Updated} 条，失败 {result.Errors.Count} 行");
            await Db.SaveChangesAsync();

            TempData["MappingMessage"] =
                $"映射导入完成：共 {result.TotalRows} 行，新增 {result.Inserted} 条，更新 {result.Updated} 条，失败 {result.Errors.Count} 行。";
            if (result.Errors.Count > 0)
                TempData["MappingErrors"] = string.Join(Environment.NewLine, result.Errors.Take(50));
            return Redirect(redirect);
        }
        catch (Exception ex)
        {
            Db.ChangeTracker.Clear();
            TempData["MappingMessage"] = "映射文件读取失败，未写入任何数据。";
            TempData["MappingErrors"] = ex.Message;
            return Redirect(redirect);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Preview(int budgetBatchId, IFormFile? file)
    {
        var fileName = SafeFileName(file?.FileName);
        var batch = await Db.BudgetBatches.FindAsync(budgetBatchId);
        if (file is null || file.Length == 0)
        {
            TempData["Message"] = "请选择 Excel 文件。";
            return RedirectToAction(nameof(Index), new { batchId = budgetBatchId });
        }

        if (batch is null)
        {
            await RecordFailedImportAsync(fileName, "未读取", 0, 0, "所选预算批次不存在。");
            TempData["Message"] = "所选预算批次不存在。";
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > MaxUploadBytes ||
            !(Path.GetExtension(fileName).Equals(".xls", StringComparison.OrdinalIgnoreCase) ||
              Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)))
        {
            const string error = "仅支持不超过 20 MB 的 .xls 或 .xlsx 文件。";
            await RecordFailedImportAsync(fileName, "未读取", 0, 0, error);
            TempData["Message"] = error;
            return RedirectToAction(nameof(Index), new { batchId = budgetBatchId });
        }

        if (!await Db.SubjectMappings.AnyAsync())
        {
            const string error = "请先配置会计科目映射。";
            await RecordFailedImportAsync(fileName, "未读取", 0, 0, error);
            TempData["Message"] = error;
            return RedirectToAction(nameof(Index), new { batchId = budgetBatchId });
        }

        try
        {
            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            var bytes = memory.ToArray();
            var preview = await _importService.PreviewAsync(bytes, budgetBatchId, null);
            var token = Guid.NewGuid().ToString("N");
            var pending = new PendingActualImport
            {
                UserId = UserManager.GetUserId(User) ?? string.Empty,
                BudgetBatchId = budgetBatchId,
                FileName = fileName,
                Content = bytes,
                Preview = preview
            };
            _cache.Set(CacheKey(token), pending, TimeSpan.FromMinutes(30));

            if (!preview.IsStructureValid)
                await RecordFailedImportAsync(fileName, preview.SelectedSheetName, preview.TotalRows,
                    preview.TotalRows, string.Join("；", preview.Errors));
            else if (preview.Rows.Count == 0)
                await RecordFailedImportAsync(fileName, preview.SelectedSheetName, preview.TotalRows,
                    preview.TotalRows, preview.Errors.Count == 0 ? "工作表没有数据行。" : string.Join("；", preview.Errors));

            return await PreviewViewAsync(token, pending, null);
        }
        catch (Exception ex)
        {
            await RecordFailedImportAsync(fileName, "未读取", 0, 0, "Excel 文件读取失败：" + ex.Message);
            TempData["Message"] = "Excel 文件读取失败，请确认文件有效且未损坏。";
            return RedirectToAction(nameof(Index), new { batchId = budgetBatchId });
        }
    }

    /// <summary>执行情况明细导入预览（「各部门实际发生数」格式：部门 / 期间 / 科目全名 / 预算科目2 / 借方金额）。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> PreviewExecution(int budgetBatchId, IFormFile? file)
    {
        var fileName = SafeFileName(file?.FileName);
        var batch = await Db.BudgetBatches.FindAsync(budgetBatchId);
        if (file is null || file.Length == 0)
        {
            TempData["Message"] = "请选择 Excel 文件。";
            return RedirectToAction(nameof(Index), new { batchId = budgetBatchId });
        }

        if (batch is null)
        {
            await RecordFailedImportAsync(fileName, "未读取", 0, 0, "所选预算批次不存在。");
            TempData["Message"] = "所选预算批次不存在。";
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > MaxUploadBytes ||
            !(Path.GetExtension(fileName).Equals(".xls", StringComparison.OrdinalIgnoreCase) ||
              Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)))
        {
            const string error = "仅支持不超过 20 MB 的 .xls 或 .xlsx 文件。";
            await RecordFailedImportAsync(fileName, "未读取", 0, 0, error);
            TempData["Message"] = error;
            return RedirectToAction(nameof(Index), new { batchId = budgetBatchId });
        }

        try
        {
            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            var bytes = memory.ToArray();
            var preview = await _executionService.PreviewAsync(bytes, budgetBatchId, null);
            var token = Guid.NewGuid().ToString("N");
            var pending = new PendingActualImport
            {
                UserId = UserManager.GetUserId(User) ?? string.Empty,
                BudgetBatchId = budgetBatchId,
                FileName = fileName,
                Content = bytes,
                Preview = preview
            };
            _cache.Set(CacheKey(token), pending, TimeSpan.FromMinutes(30));

            if (!preview.IsStructureValid)
                await RecordFailedImportAsync(fileName, preview.SelectedSheetName, preview.TotalRows,
                    preview.TotalRows, string.Join("；", preview.Errors));
            else if (preview.Rows.Count == 0)
                await RecordFailedImportAsync(fileName, preview.SelectedSheetName, preview.TotalRows,
                    preview.TotalRows, preview.Errors.Count == 0 ? "工作表没有数据行。" : string.Join("；", preview.Errors));

            return await PreviewViewAsync(token, pending, null);
        }
        catch (Exception ex)
        {
            await RecordFailedImportAsync(fileName, "未读取", 0, 0, "Excel 文件读取失败：" + ex.Message);
            TempData["Message"] = "Excel 文件读取失败，请确认文件有效且未损坏。";
            return RedirectToAction(nameof(Index), new { batchId = budgetBatchId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewSheet(string token, string sheetName)
    {
        var pending = GetPending(token);
        if (pending is null || pending.UserId != UserManager.GetUserId(User))
        {
            TempData["Message"] = "预览已过期，请重新上传文件。";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            pending.Preview = await _importService.PreviewAsync(pending.Content, pending.BudgetBatchId, sheetName);
            _cache.Set(CacheKey(token), pending, TimeSpan.FromMinutes(30));
            if (!pending.Preview.IsStructureValid)
                await RecordFailedImportAsync(pending.FileName, pending.Preview.SelectedSheetName,
                    pending.Preview.TotalRows, pending.Preview.TotalRows, string.Join("；", pending.Preview.Errors));
            else if (pending.Preview.Rows.Count == 0)
                await RecordFailedImportAsync(pending.FileName, pending.Preview.SelectedSheetName,
                    pending.Preview.TotalRows, pending.Preview.TotalRows,
                    pending.Preview.Errors.Count == 0 ? "工作表没有数据行。" : string.Join("；", pending.Preview.Errors));
            return await PreviewViewAsync(token, pending, null);
        }
        catch (Exception ex)
        {
            await RecordFailedImportAsync(pending.FileName, sheetName, 0, 0, "Excel 工作表读取失败：" + ex.Message);
            return await PreviewViewAsync(token, pending,
                "工作表读取失败，请重新选择工作表或上传文件。");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string token, bool overwritePrevious = false)
    {
        var pending = GetPending(token);
        if (pending is null || pending.UserId != UserManager.GetUserId(User))
        {
            TempData["Message"] = "预览已过期，请重新上传文件。";
            return RedirectToAction(nameof(Index));
        }

        var preview = pending.Preview;
        if (!preview.IsStructureValid || preview.Rows.Count == 0)
        {
            await RecordFailedImportAsync(pending.FileName, preview.SelectedSheetName,
                preview.TotalRows, preview.TotalRows, string.Join("；", preview.Errors));
            _cache.Remove(CacheKey(token));
            TempData["Message"] = "没有可导入的有效数据行，未写入决算数据。";
            return RedirectToAction(nameof(Index), new { batchId = pending.BudgetBatchId });
        }

        var existing = await Db.ActualDetails.AnyAsync(x => x.BudgetBatchId == pending.BudgetBatchId);
        if (existing && !overwritePrevious)
            return await PreviewViewAsync(token, pending, "该预算批次已有导入数据，请选择“覆盖上一次导入”或取消。");

        await using var transaction = await Db.Database.BeginTransactionAsync();
        try
        {
            var budgetBatch = await Db.BudgetBatches.FindAsync(pending.BudgetBatchId)
                ?? throw new InvalidOperationException("所选预算批次不存在。");
            var oldDetails = await Db.ActualDetails
                .Where(x => x.BudgetBatchId == budgetBatch.Id)
                .ToListAsync();
            if (oldDetails.Count > 0 && !overwritePrevious)
            {
                await transaction.RollbackAsync();
                return await PreviewViewAsync(token, pending, "该预算批次已有导入数据，请选择“覆盖上一次导入”或取消。");
            }

            if (oldDetails.Count > 0)
            {
                var oldImportIds = oldDetails.Select(x => x.BudgetImportBatchId).Distinct().ToList();
                var oldImports = await Db.BudgetImportBatches
                    .Where(x => oldImportIds.Contains(x.Id)).ToListAsync();
                foreach (var oldImport in oldImports) oldImport.Status = "已覆盖";
                Db.ActualDetails.RemoveRange(oldDetails);
            }

            var baselineRows = await Db.BaselineRecords
                .Where(x => x.BudgetBatchId == budgetBatch.Id && x.Version == budgetBatch.CurrentBaselineVersion)
                .ToListAsync();
            var baselineByKey = baselineRows.ToDictionary(x => (x.OrganizationId, x.SubjectId));
            foreach (var baseline in baselineRows)
            {
                baseline.ActualBaseline = 0m;
                baseline.ActualBaselineProject = 0m;
            }

            // 实际账务的「部门 × 科目」可能超出预算填报的科目范围（例如各部门账上都有工资、社保），
            // 遇到缺失的基准记录时自动补建（2026 预算数为 0），避免整批导入因缺行而回滚。
            var groups = preview.Rows
                .GroupBy(x => (x.OrganizationId, x.SubjectId, x.ExpenseType))
                .ToList();
            foreach (var group in groups)
            {
                if (!baselineByKey.TryGetValue((group.Key.OrganizationId, group.Key.SubjectId), out var baseline))
                {
                    baseline = new BaselineRecord
                    {
                        BudgetBatchId = budgetBatch.Id,
                        OrganizationId = group.Key.OrganizationId,
                        SubjectId = group.Key.SubjectId,
                        BudgetBaseline = 0m,
                        BudgetBaselineProject = 0m,
                        ActualBaseline = 0m,
                        ActualBaselineProject = 0m,
                        Version = budgetBatch.CurrentBaselineVersion,
                        IsLocked = true,
                        UpdatedBy = CurrentOperator()
                    };
                    Db.BaselineRecords.Add(baseline);
                    baselineRows.Add(baseline);
                    baselineByKey[(group.Key.OrganizationId, group.Key.SubjectId)] = baseline;
                }

                if (group.Key.ExpenseType == "项目支出")
                    baseline.ActualBaselineProject = group.Sum(x => x.ApprovedAmount);
                else
                    baseline.ActualBaseline = group.Sum(x => x.ApprovedAmount);
            }

            // 不再按导入数据自动补建「科目 × 部门」范围：
            // 未配置范围的科目默认全部门可见；已配置范围的科目（如人员费用仅人事部）必须尊重管理员设置，
            // 否则重新导入同一文件会把范围悄悄扩散到所有部门。

            var importBatch = new BudgetImportBatch
            {
                FileName = pending.FileName,
                SheetName = pending.Preview.SelectedSheetName,
                TotalRows = pending.Preview.TotalRows,
                SuccessRows = pending.Preview.Rows.Count,
                FailedRows = pending.Preview.TotalRows - pending.Preview.Rows.Count,
                ImportedBy = CurrentOperator(),
                ImportedAt = DateTime.Now,
                Status = pending.Preview.Errors.Count == 0 ? "成功" : "部分成功",
                ErrorSummary = pending.Preview.Errors.Count == 0
                    ? null
                    : string.Join(Environment.NewLine, pending.Preview.Errors)
            };
            Db.BudgetImportBatches.Add(importBatch);
            await Db.SaveChangesAsync();

            foreach (var row in preview.Rows)
            {
                Db.ActualDetails.Add(new ActualDetail
                {
                    BudgetImportBatchId = importBatch.Id,
                    BudgetBatchId = budgetBatch.Id,
                    OrganizationId = row.OrganizationId,
                    SubjectId = row.SubjectId,
                    AccountingPeriod = row.AccountingPeriod,
                    VoucherDate = row.VoucherDate,
                    VoucherNumber = row.VoucherNumber,
                    AccountingSubjectName = row.AccountingSubjectName,
                    ExpenseType = row.ExpenseType,
                    AccountAmount = row.AccountAmount,
                    AdjustmentAmount = row.AdjustmentAmount,
                    ApprovedAmount = row.ApprovedAmount,
                    SourceRowNumber = row.SourceRowNumber
                });
            }

            var baselineByOrgSubject = baselineRows.ToDictionary(x => (x.OrganizationId, x.SubjectId));
            var forms = await Db.FilingForms
                .Include(x => x.Lines)
                .Where(x => x.BudgetBatchId == budgetBatch.Id &&
                            (x.Status == FormStatus.Draft || x.Status == FormStatus.Returned))
                .ToListAsync();

            // 草稿/已打回单据里若缺少导入数据涉及的行，先补建空行，保证实际数能在页面看到
            var neededByOrg = groups
                .GroupBy(x => x.Key.OrganizationId)
                .ToDictionary(x => x.Key,
                    x => x.Select(y => (y.Key.SubjectId, y.Key.ExpenseType)).Distinct().ToList());
            foreach (var form in forms)
            {
                if (!neededByOrg.TryGetValue(form.OrganizationId, out var needed)) continue;
                var existingKeys = form.Lines.Select(x => (x.SubjectId, x.ExpenseType)).ToHashSet();
                foreach (var (subjectId, expenseType) in needed)
                {
                    if (existingKeys.Contains((subjectId, expenseType))) continue;
                    form.Lines.Add(new ReportLine
                    {
                        SubjectId = subjectId,
                        ExpenseType = expenseType,
                        Budget2026 = 0m,
                        Actual2026 = 0m,
                        Budget2027 = 0m,
                        BaselineVersion = budgetBatch.CurrentBaselineVersion
                    });
                }
            }

            foreach (var form in forms)
            foreach (var line in form.Lines)
            {
                if (!baselineByOrgSubject.TryGetValue((form.OrganizationId, line.SubjectId), out var baseline))
                {
                    line.Budget2026 = 0m;
                    line.Actual2026 = 0m;
                }
                else if (line.ExpenseType == "项目支出")
                {
                    line.Budget2026 = baseline.BudgetBaselineProject;
                    line.Actual2026 = baseline.ActualBaselineProject;
                }
                else
                {
                    line.Budget2026 = baseline.BudgetBaseline;
                    line.Actual2026 = baseline.ActualBaseline;
                }
                line.BaselineVersion = budgetBatch.CurrentBaselineVersion;
            }

            WriteLog("导入决算", pending.FileName,
                $"批次 {budgetBatch.Name}，工作表 {pending.Preview.SelectedSheetName}，成功 {importBatch.SuccessRows} 行，失败 {importBatch.FailedRows} 行");
            await Db.SaveChangesAsync();
            await transaction.CommitAsync();
            _cache.Remove(CacheKey(token));
            TempData["Message"] = $"决算导入完成：成功 {importBatch.SuccessRows} 行，失败 {importBatch.FailedRows} 行。";
            return RedirectToAction(nameof(Index), new { batchId = budgetBatch.Id });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Db.ChangeTracker.Clear();
            await RecordFailedImportAsync(pending.FileName, pending.Preview.SelectedSheetName,
                pending.Preview.TotalRows, pending.Preview.TotalRows, "导入事务失败并已整体回滚：" + ex.Message);
            _cache.Remove(CacheKey(token));
            TempData["Message"] = "导入失败，数据变更已整体回滚；失败记录已写入导入批次。";
            return RedirectToAction(nameof(Index), new { batchId = pending.BudgetBatchId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSubjectMapping(string accountingSubjectName, int subjectId, string expenseType, string? remarks)
    {
        var accountingName = string.Concat((accountingSubjectName ?? string.Empty)
            .Where(x => !char.IsWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(accountingName) || accountingName.Length > 200 ||
            (expenseType != "基本支出" && expenseType != "项目支出") ||
            !await Db.Subjects.AnyAsync(x => x.Id == subjectId))
        {
            TempData["Message"] = "请填写有效的会计科目名称、预算科目和费用属性。";
            return Redirect(Url.Action(nameof(Index)) + "#subject-mapping-config");
        }

        var existingMappings = await Db.SubjectMappings.ToListAsync();
        var equivalentMappings = existingMappings
            .Where(x => NormalizeMappingName(x.AccountingSubjectName)
                .Equals(accountingName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (equivalentMappings.Count > 1)
        {
            TempData["Message"] = "存在重复的会计科目映射，请先清理重复配置。";
            return Redirect(Url.Action(nameof(Index)) + "#subject-mapping-config");
        }

        var mapping = equivalentMappings.FirstOrDefault();
        var action = mapping is null ? "新增会计科目映射" : "修改会计科目映射";
        if (mapping is null)
        {
            mapping = new SubjectMapping { AccountingSubjectName = accountingName };
            Db.SubjectMappings.Add(mapping);
        }
        mapping.AccountingSubjectName = accountingName;
        mapping.SubjectId = subjectId;
        mapping.ExpenseType = expenseType;
        mapping.Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        WriteLog(action, accountingName, $"预算科目 ID {subjectId}，费用属性 {expenseType}");
        await Db.SaveChangesAsync();
        TempData["Message"] = "会计科目映射已保存。";
        return Redirect(Url.Action(nameof(Index)) + "#subject-mapping-config");
    }

    private static string NormalizeMappingName(string value)
        => string.Concat(value.Where(x => !char.IsWhiteSpace(x)));

    private async Task<IActionResult> PreviewViewAsync(string token, PendingActualImport pending, string? message)
    {
        var batch = await Db.BudgetBatches.FindAsync(pending.BudgetBatchId);
        var rows = pending.Preview.Rows;
        var model = new ActualImportPreviewViewModel
        {
            Token = token,
            BudgetBatchId = pending.BudgetBatchId,
            BatchName = batch?.Name ?? "未知批次",
            FileName = pending.FileName,
            SelectedSheetName = pending.Preview.SelectedSheetName,
            SheetNames = pending.Preview.SheetNames,
            TotalRows = pending.Preview.TotalRows,
            SuccessRows = rows.Count,
            FailedRows = pending.Preview.TotalRows - rows.Count,
            HasExistingDetails = await Db.ActualDetails.AnyAsync(x => x.BudgetBatchId == pending.BudgetBatchId),
            Errors = pending.Preview.Errors,
            UnmatchedDepartments = pending.Preview.UnmatchedDepartments.Distinct().ToList(),
            UnmappedSubjects = pending.Preview.UnmappedSubjects.Distinct().ToList(),
            Message = message,
            Subtotals = rows.GroupBy(x => new { x.OrganizationId, x.OrganizationName, x.ExpenseType })
                .OrderBy(x => x.Key.OrganizationId)
                .ThenBy(x => x.Key.ExpenseType)
                .Select(x => new ActualImportSubtotalViewModel
                {
                    OrganizationName = x.Key.OrganizationName,
                    ExpenseType = x.Key.ExpenseType,
                    RowCount = x.Count(),
                    AccountAmount = x.Sum(y => y.AccountAmount),
                    AdjustmentAmount = x.Sum(y => y.AdjustmentAmount),
                    ApprovedAmount = x.Sum(y => y.ApprovedAmount)
                }).ToList()
        };
        return View("ActualImportPreview", model);
    }

    private PendingActualImport? GetPending(string token)
        => _cache.TryGetValue(CacheKey(token), out PendingActualImport? pending) ? pending : null;

    private static string CacheKey(string token) => "actual-import:" + token;

    private string CurrentOperator()
    {
        var name = User.Identity?.Name ?? "系统管理员";
        return name.Length <= 100 ? name : name[..100];
    }

    private static string SafeFileName(string? fileName)
    {
        var safeName = Path.GetFileName(fileName ?? string.Empty);
        return safeName.Length <= 260 ? safeName : safeName[^260..];
    }

    private async Task RecordFailedImportAsync(string fileName, string? sheetName, int totalRows, int failedRows, string error)
    {
        Db.BudgetImportBatches.Add(new BudgetImportBatch
        {
            FileName = string.IsNullOrWhiteSpace(fileName) ? "未命名文件" : fileName,
            SheetName = string.IsNullOrWhiteSpace(sheetName) ? "未读取" : sheetName.Length > 100 ? sheetName[..100] : sheetName,
            TotalRows = totalRows,
            SuccessRows = 0,
            FailedRows = failedRows,
            ImportedBy = CurrentOperator(),
            ImportedAt = DateTime.Now,
            Status = "失败",
            ErrorSummary = error
        });
        await Db.SaveChangesAsync();
    }

    private sealed class PendingActualImport
    {
        public string UserId { get; set; } = string.Empty;
        public int BudgetBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public ActualWorkbookPreview Preview { get; set; } = new();
    }
}
