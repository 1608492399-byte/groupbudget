using System.Globalization;
using GroupBudget.Data;
using GroupBudget.Models;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;

namespace GroupBudget.Services;

public sealed class ActualExcelImportService
{
    private static readonly string[] RequiredHeaders =
    {
        "公司", "日期", "会计期间", "凭证字号", "会计科目", "预算归属部门", "费用属性",
        "账套金额", "调整金额", "审定金额"
    };

    private readonly ApplicationDbContext _db;

    public ActualExcelImportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ActualWorkbookPreview> PreviewAsync(byte[] content, int budgetBatchId, string? requestedSheet)
    {
        using var stream = new MemoryStream(content);
        using var workbook = WorkbookFactory.Create(stream);
        var preview = new ActualWorkbookPreview
        {
            SheetNames = Enumerable.Range(0, workbook.NumberOfSheets)
                .Select(workbook.GetSheetName)
                .ToList()
        };

        if (preview.SheetNames.Count == 0)
        {
            preview.Errors.Add("工作簿中没有可读取的工作表。");
            return preview;
        }

        var selectedSheet = string.IsNullOrWhiteSpace(requestedSheet)
            ? preview.SheetNames.FirstOrDefault(x => x.Contains("附表7", StringComparison.Ordinal)) ?? preview.SheetNames[0]
            : requestedSheet;
        var sheet = workbook.GetSheet(selectedSheet);
        if (sheet is null)
        {
            preview.Errors.Add($"找不到工作表「{selectedSheet}」。");
            return preview;
        }
        preview.SelectedSheetName = selectedSheet;

        var formatter = new DataFormatter(CultureInfo.GetCultureInfo("zh-CN"));
        var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
        var headerRowIndex = FindHeaderRow(sheet, formatter, evaluator);
        if (headerRowIndex < 0)
        {
            preview.Errors.Add("未找到同时包含「会计科目」和「审定金额」的表头行。");
            return preview;
        }

        var header = sheet.GetRow(headerRowIndex)!;
        var columns = FindRequiredColumns(header, formatter, evaluator);
        var missingHeaders = RequiredHeaders.Where(x => !columns.ContainsKey(x)).ToList();
        if (missingHeaders.Count > 0)
        {
            preview.Errors.Add("表头缺少必需列：" + string.Join("、", missingHeaders));
            return preview;
        }

        var batch = await _db.BudgetBatches.FindAsync(budgetBatchId);
        if (batch is null)
        {
            preview.Errors.Add("所选预算批次不存在。");
            return preview;
        }
        preview.IsStructureValid = true;

        var organizations = await _db.Organizations.AsNoTracking().ToListAsync();
        var organizationsByName = organizations
            .GroupBy(x => NormalizeDepartmentName(x.Name))
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var mappings = await _db.SubjectMappings.AsNoTracking().ToListAsync();
        var mappingsByName = new Dictionary<string, SubjectMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in mappings.GroupBy(x => NormalizeName(x.AccountingSubjectName), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(x => (x.SubjectId, x.ExpenseType)).Distinct().Count() > 1)
            {
                preview.IsStructureValid = false;
                preview.Errors.Add($"会计科目映射「{group.Key}」存在冲突，请先修正映射配置。");
                return preview;
            }
            mappingsByName[group.Key] = group.First();
        }
        var baselineKeys = await _db.BaselineRecords.AsNoTracking()
            .Where(x => x.BudgetBatchId == budgetBatchId && x.Version == batch.CurrentBaselineVersion)
            .Select(x => new { x.OrganizationId, x.SubjectId })
            .ToListAsync();
        var baselineKeySet = baselineKeys.Select(x => (x.OrganizationId, x.SubjectId)).ToHashSet();

        for (var rowIndex = headerRowIndex + 1; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row is null || IsBlankRow(row, formatter, evaluator)) continue;

            var sourceRow = rowIndex + 1;
            preview.TotalRows++;
            try
            {
                var departmentText = CellText(row, columns["预算归属部门"], formatter, evaluator).Trim();
                var hasOrganization = organizationsByName.TryGetValue(
                    NormalizeDepartmentName(departmentText), out var organization);
                if (!hasOrganization)
                {
                    preview.UnmatchedDepartments.Add($"第 {sourceRow} 行：{(string.IsNullOrEmpty(departmentText) ? "（空白）" : departmentText)}");
                    preview.Errors.Add($"第 {sourceRow} 行：部门未匹配。");
                }

                var accountingSubject = CellText(row, columns["会计科目"], formatter, evaluator).Trim();
                var hasMapping = mappingsByName.TryGetValue(NormalizeName(accountingSubject), out var mapping);
                if (!hasMapping)
                {
                    preview.UnmappedSubjects.Add($"第 {sourceRow} 行：{(string.IsNullOrEmpty(accountingSubject) ? "（空白）" : accountingSubject)}");
                    preview.Errors.Add($"第 {sourceRow} 行：会计科目未配置映射。");
                }
                if (!hasOrganization || !hasMapping) continue;

                if (!baselineKeySet.Contains((organization.Id, mapping.SubjectId)))
                {
                    preview.Errors.Add($"第 {sourceRow} 行：部门「{organization.Name}」没有对应预算科目的当前版本基准记录。");
                    continue;
                }

                var sourceExpenseType = CellText(row, columns["费用属性"], formatter, evaluator).Trim();
                if (!TryNormalizeExpenseType(mapping.ExpenseType, out var mappedExpenseType))
                {
                    preview.Errors.Add($"第 {sourceRow} 行：会计科目「{accountingSubject}」映射中的费用属性无效。");
                    continue;
                }

                var expenseType = mappedExpenseType;
                if (!string.IsNullOrWhiteSpace(sourceExpenseType))
                {
                    if (!TryNormalizeExpenseType(sourceExpenseType, out var rowExpenseType))
                    {
                        preview.Errors.Add($"第 {sourceRow} 行：费用属性「{sourceExpenseType}」无法判定为基本支出或项目支出。");
                        continue;
                    }
                    if (!string.Equals(rowExpenseType, mappedExpenseType, StringComparison.Ordinal))
                    {
                        preview.Errors.Add($"第 {sourceRow} 行：费用属性与会计科目映射不一致。");
                        continue;
                    }
                    expenseType = rowExpenseType;
                }

                var period = CellText(row, columns["会计期间"], formatter, evaluator).Trim();
                if (string.IsNullOrWhiteSpace(period))
                {
                    preview.Errors.Add($"第 {sourceRow} 行：会计期间为空。");
                    continue;
                }

                var dateCell = row.GetCell(columns["日期"]);
                DateTime? voucherDate = null;
                if (dateCell is not null && dateCell.CellType != CellType.Blank)
                {
                    var dateText = formatter.FormatCellValue(dateCell, evaluator).Trim();
                    if (DateUtil.IsCellDateFormatted(dateCell))
                        voucherDate = dateCell.DateCellValue;
                    else if (DateTime.TryParse(dateText, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out var parsedDate) ||
                             DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                        voucherDate = parsedDate;
                    else if (!string.IsNullOrWhiteSpace(dateText) && dateText != "-")
                    {
                        preview.Errors.Add($"第 {sourceRow} 行：凭证日期「{dateText}」无法识别。");
                        continue;
                    }
                }

                if (!TryParseAmount(CellText(row, columns["账套金额"], formatter, evaluator), out var accountAmount) ||
                    !TryParseAmount(CellText(row, columns["调整金额"], formatter, evaluator), out var adjustmentAmount) ||
                    !TryParseAmount(CellText(row, columns["审定金额"], formatter, evaluator), out var approvedAmount))
                {
                    preview.Errors.Add($"第 {sourceRow} 行：账套金额、调整金额或审定金额格式无效或超出 decimal(18,2) 范围。");
                    continue;
                }

                preview.Rows.Add(new ParsedActualRow
                {
                    OrganizationId = organization.Id,
                    OrganizationName = organization.Name,
                    SubjectId = mapping.SubjectId,
                    AccountingPeriod = period,
                    VoucherDate = voucherDate,
                    VoucherNumber = NullIfDash(CellText(row, columns["凭证字号"], formatter, evaluator)),
                    AccountingSubjectName = accountingSubject,
                    ExpenseType = expenseType,
                    AccountAmount = accountAmount,
                    AdjustmentAmount = adjustmentAmount,
                    ApprovedAmount = approvedAmount,
                    SourceRowNumber = sourceRow
                });
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
            {
                preview.Errors.Add($"第 {sourceRow} 行：数据解析失败（{ex.Message}）。");
            }
        }

        return preview;
    }

    private static int FindHeaderRow(ISheet sheet, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row is null) continue;
            var firstCell = Math.Max(0, (int)row.FirstCellNum);
            var values = Enumerable.Range(firstCell, Math.Max(0, (int)row.LastCellNum - firstCell))
                .Select(x => NormalizeHeader(CellText(row, x, formatter, evaluator)))
                .ToList();
            if (values.Any(x => x.Contains("会计科目", StringComparison.Ordinal)) &&
                values.Any(x => x.Contains("审定金额", StringComparison.Ordinal)))
                return rowIndex;
        }
        return -1;
    }

    private static Dictionary<string, int> FindRequiredColumns(IRow row, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var columnIndex = Math.Max(0, (int)row.FirstCellNum); columnIndex < row.LastCellNum; columnIndex++)
        {
            var header = NormalizeHeader(CellText(row, columnIndex, formatter, evaluator));
            foreach (var required in RequiredHeaders)
            {
                if (!columns.ContainsKey(required) && header.Contains(NormalizeHeader(required), StringComparison.Ordinal))
                    columns[required] = columnIndex;
            }
        }
        return columns;
    }

    private static bool IsBlankRow(IRow row, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        for (var columnIndex = Math.Max(0, (int)row.FirstCellNum); columnIndex < row.LastCellNum; columnIndex++)
            if (!string.IsNullOrWhiteSpace(CellText(row, columnIndex, formatter, evaluator))) return false;
        return true;
    }

    private static string CellText(IRow row, int columnIndex, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        var cell = row.GetCell(columnIndex);
        return cell is null ? string.Empty : formatter.FormatCellValue(cell, evaluator).Trim();
    }

    private static string NormalizeHeader(string value)
        => string.Concat(value.Where(x => !char.IsWhiteSpace(x) && x != '：' && x != ':' && x != '（' && x != '）' && x != '(' && x != ')'));

    private static string NormalizeName(string value)
        => string.Concat(value.Where(x => !char.IsWhiteSpace(x))).Trim();

    private static string NormalizeDepartmentName(string value)
    {
        var normalized = NormalizeName(value);
        return normalized switch
        {
            "人事部门" => "人事部",
            "信息中心" => "信息部",
            _ => normalized
        };
    }

    private static bool TryNormalizeExpenseType(string value, out string expenseType)
    {
        var normalized = NormalizeName(value);
        if (normalized.Contains("基本", StringComparison.Ordinal))
        {
            expenseType = "基本支出";
            return true;
        }
        if (normalized.Contains("项目", StringComparison.Ordinal))
        {
            expenseType = "项目支出";
            return true;
        }
        expenseType = string.Empty;
        return false;
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "-" || trimmed == "—")
        {
            amount = 0m;
            return true;
        }

        const decimal maxAmount = 9999999999999999.99m;
        if (!decimal.TryParse(trimmed, NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowParentheses,
                CultureInfo.GetCultureInfo("zh-CN"), out amount) &&
            !decimal.TryParse(trimmed, NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowParentheses,
                CultureInfo.InvariantCulture, out amount))
            return false;

        return Math.Abs(amount) <= maxAmount && decimal.Round(amount, 2) == amount;
    }

    private static string? NullIfDash(string value)
        => string.IsNullOrWhiteSpace(value) || value.Trim() == "-" ? null : value.Trim();
}

public sealed class ActualWorkbookPreview
{
    public bool IsStructureValid { get; set; }
    public List<string> SheetNames { get; set; } = new();
    public string SelectedSheetName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public List<ParsedActualRow> Rows { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> UnmatchedDepartments { get; set; } = new();
    public List<string> UnmappedSubjects { get; set; } = new();
}

public sealed class ParsedActualRow
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int SubjectId { get; set; }
    public string AccountingPeriod { get; set; } = string.Empty;
    public DateTime? VoucherDate { get; set; }
    public string? VoucherNumber { get; set; }
    public string AccountingSubjectName { get; set; } = string.Empty;
    public string ExpenseType { get; set; } = string.Empty;
    public decimal AccountAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public int SourceRowNumber { get; set; }
}
