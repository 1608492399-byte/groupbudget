using System.Globalization;
using GroupBudget.Data;
using GroupBudget.Models;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;

namespace GroupBudget.Services;

/// <summary>会计科目映射批量导入结果。</summary>
public sealed class MappingImportResult
{
    public int TotalRows { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public List<string> Errors { get; set; } = new();

    public int Succeeded => Inserted + Updated;
}

/// <summary>按模板批量导入「会计科目 → 预算科目」映射，避免一条条手加。</summary>
public sealed class MappingExcelImportService
{
    private readonly ApplicationDbContext _db;

    public MappingExcelImportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<MappingImportResult> ImportAsync(byte[] content)
    {
        var result = new MappingImportResult();
        using var stream = new MemoryStream(content);
        using var workbook = WorkbookFactory.Create(stream);

        var sheet = FindMappingSheet(workbook);
        if (sheet is null)
        {
            result.Errors.Add("工作簿中没有可读取的工作表。");
            return result;
        }

        var formatter = new DataFormatter(CultureInfo.GetCultureInfo("zh-CN"));
        var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
        var headerRowIndex = FindHeaderRow(sheet, formatter, evaluator);
        if (headerRowIndex < 0)
        {
            result.Errors.Add("未找到包含「会计科目名称」的表头行，请使用下载的模板。");
            return result;
        }

        var header = sheet.GetRow(headerRowIndex)!;
        var columns = FindColumns(header, formatter, evaluator);
        if (!columns.TryGetValue("会计科目", out var nameColumn))
        {
            result.Errors.Add("表头缺少「会计科目名称」列。");
            return result;
        }

        var subjects = await _db.Subjects.AsNoTracking().ToListAsync();
        var byCode = subjects
            .GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var byName = subjects
            .GroupBy(x => Normalize(x.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var existing = await _db.SubjectMappings.ToListAsync();
        var existingByName = new Dictionary<string, SubjectMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in existing.GroupBy(x => Normalize(x.AccountingSubjectName), StringComparer.OrdinalIgnoreCase))
            existingByName[group.Key] = group.First();

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var rowIndex = headerRowIndex + 1; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row is null || IsBlankRow(row, formatter, evaluator)) continue;

            var sourceRow = rowIndex + 1;
            result.TotalRows++;

            var accountingName = Normalize(CellText(row, nameColumn, formatter, evaluator));
            if (string.IsNullOrWhiteSpace(accountingName))
            {
                result.Errors.Add($"第 {sourceRow} 行：会计科目名称为空。");
                continue;
            }
            if (accountingName.Length > 200)
            {
                result.Errors.Add($"第 {sourceRow} 行：会计科目名称超过 200 个字符。");
                continue;
            }
            if (!seenInFile.Add(accountingName))
            {
                result.Errors.Add($"第 {sourceRow} 行：会计科目「{accountingName}」在文件中重复，已跳过。");
                continue;
            }

            var codeText = columns.TryGetValue("预算科目编码", out var codeColumn)
                ? Normalize(CellText(row, codeColumn, formatter, evaluator))
                : string.Empty;
            var nameText = columns.TryGetValue("预算科目名称", out var subjectNameColumn)
                ? Normalize(CellText(row, subjectNameColumn, formatter, evaluator))
                : string.Empty;

            Subject? subject = null;
            if (!string.IsNullOrWhiteSpace(codeText)) byCode.TryGetValue(codeText, out subject);
            if (subject is null && !string.IsNullOrWhiteSpace(nameText)) byName.TryGetValue(nameText, out subject);
            if (subject is null)
            {
                result.Errors.Add(
                    $"第 {sourceRow} 行：预算科目「{(string.IsNullOrWhiteSpace(codeText) ? nameText : codeText)}」不存在，请核对编码或名称。");
                continue;
            }

            var expenseText = columns.TryGetValue("费用属性", out var expenseColumn)
                ? Normalize(CellText(row, expenseColumn, formatter, evaluator))
                : string.Empty;
            var expenseType = string.IsNullOrWhiteSpace(expenseText)
                ? "基本支出"
                : expenseText.Contains("项目", StringComparison.Ordinal) ? "项目支出"
                : expenseText.Contains("基本", StringComparison.Ordinal) ? "基本支出"
                : string.Empty;
            if (string.IsNullOrEmpty(expenseType))
            {
                result.Errors.Add($"第 {sourceRow} 行：费用属性「{expenseText}」无法识别，只能填基本支出或项目支出。");
                continue;
            }

            var remarks = columns.TryGetValue("备注", out var remarkColumn)
                ? CellText(row, remarkColumn, formatter, evaluator).Trim()
                : string.Empty;

            if (existingByName.TryGetValue(accountingName, out var mapping))
            {
                mapping.SubjectId = subject.Id;
                mapping.ExpenseType = expenseType;
                mapping.Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks;
                result.Updated++;
            }
            else
            {
                mapping = new SubjectMapping
                {
                    AccountingSubjectName = accountingName,
                    SubjectId = subject.Id,
                    ExpenseType = expenseType,
                    Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks
                };
                _db.SubjectMappings.Add(mapping);
                existingByName[accountingName] = mapping;
                result.Inserted++;
            }
        }

        return result;
    }

    private static ISheet? FindMappingSheet(IWorkbook workbook)
    {
        for (var i = 0; i < workbook.NumberOfSheets; i++)
        {
            var sheet = workbook.GetSheetAt(i);
            if (sheet.SheetName.Contains("映射", StringComparison.Ordinal)) return sheet;
        }
        return workbook.NumberOfSheets > 0 ? workbook.GetSheetAt(0) : null;
    }

    private static int FindHeaderRow(ISheet sheet, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        for (var rowIndex = sheet.FirstRowNum; rowIndex <= Math.Min(sheet.LastRowNum, sheet.FirstRowNum + 20); rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row is null) continue;
            var first = Math.Max(0, (int)row.FirstCellNum);
            for (var c = first; c < row.LastCellNum; c++)
            {
                if (CellText(row, c, formatter, evaluator).Contains("会计科目", StringComparison.Ordinal))
                    return rowIndex;
            }
        }
        return -1;
    }

    private static Dictionary<string, int> FindColumns(IRow header, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var c = Math.Max(0, (int)header.FirstCellNum); c < header.LastCellNum; c++)
        {
            var text = CellText(header, c, formatter, evaluator);
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (!columns.ContainsKey("会计科目") && text.Contains("会计科目", StringComparison.Ordinal))
                columns["会计科目"] = c;
            else if (!columns.ContainsKey("预算科目编码") && text.Contains("编码", StringComparison.Ordinal))
                columns["预算科目编码"] = c;
            else if (!columns.ContainsKey("预算科目名称") && text.Contains("预算科目", StringComparison.Ordinal))
                columns["预算科目名称"] = c;
            else if (!columns.ContainsKey("费用属性") && text.Contains("费用属性", StringComparison.Ordinal))
                columns["费用属性"] = c;
            else if (!columns.ContainsKey("备注") && text.Contains("备注", StringComparison.Ordinal))
                columns["备注"] = c;
        }
        return columns;
    }

    private static bool IsBlankRow(IRow row, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        for (var c = Math.Max(0, (int)row.FirstCellNum); c < row.LastCellNum; c++)
            if (!string.IsNullOrWhiteSpace(CellText(row, c, formatter, evaluator))) return false;
        return true;
    }

    private static string CellText(IRow row, int columnIndex, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        var cell = row.GetCell(columnIndex);
        return cell is null ? string.Empty : formatter.FormatCellValue(cell, evaluator).Trim();
    }

    private static string Normalize(string value)
        => string.Concat((value ?? string.Empty).Where(x => !char.IsWhiteSpace(x))).Trim();
}
