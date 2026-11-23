using System.Globalization;
using GroupBudget.Data;
using GroupBudget.Models;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;

namespace GroupBudget.Services;

/// <summary>
/// 「预算执行情况明细」格式导入（区别于附表7 格式）。
/// 列结构：公司 / 日期 / 会计年度 / 期间 / 凭证字 / 凭证号 / 摘要 / 科目编码 / 科目全名 /
///         费用类别 / 预算科目 / 预算科目2 / 核算维度 / 部门 / 币别 / 原币金额 / 借方金额 / 贷方金额。
/// 与附表7 的差别：没有费用属性与审定金额，金额取「借方金额」，费用属性统一按「基本支出」。
/// </summary>
public sealed class ExecutionImportService
{
    private static readonly string[] RequiredHeaders =
    {
        "日期", "期间", "科目全名", "费用类别", "预算科目2", "部门", "借方金额"
    };

    /// <summary>明细表「预算科目2」与系统预算科目字典的名称差异对照。</summary>
    private static readonly Dictionary<string, string> SubjectAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["基本工资"] = "工资",
        ["社会保险费"] = "社保和公积金",
        ["住房公积金"] = "社保和公积金",
        ["辞退福利"] = "福利费",
        ["食堂运营"] = "食材采购",
        ["节日福利"] = "节日福利费",
        ["体检费"] = "其他员工福利费",
        ["其他福利费"] = "其他员工福利费",
        ["团建活动"] = "团建活动费",
        ["国内差旅费"] = "差旅费",
        ["出国差旅费"] = "差旅费"
    };

    /// <summary>部门历史叫法映射到系统标准名称。</summary>
    private static readonly Dictionary<string, string> DepartmentAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["投资1部"] = "投资一部",
        ["投资2部"] = "投资二部",
        ["投资3部"] = "投资三部",
        ["行政部"] = "行政管理部",
        ["人事部门"] = "人事部",
        ["信息中心"] = "信息部"
    };

    private readonly ApplicationDbContext _db;

    public ExecutionImportService(ApplicationDbContext db) => _db = db;

    /// <summary>解析执行情况明细文件，返回可导入的行与未匹配清单；不写任何数据。</summary>
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
            ? preview.SheetNames.FirstOrDefault(x => x.Trim() == "各部门实际发生数")
              ?? preview.SheetNames.FirstOrDefault(x => x.Contains("实际发生数") && !x.Contains("汇总"))
              ?? preview.SheetNames[0]
            : requestedSheet;
        var sheet = workbook.GetSheet(selectedSheet);
        if (sheet is null)
        {
            preview.Errors.Add($"找不到工作表「{selectedSheet}」。");
            return preview;
        }
        preview.SelectedSheetName = selectedSheet;

        var formatter = new DataFormatter(CultureInfo.GetCultureInfo("zh-CN"));
        // 明细表中「预算科目2」列大量是 =K4 这类公式，必须用求值器取计算结果，否则读到的是公式文本
        var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
        var headerRowIndex = FindHeaderRow(sheet, formatter, evaluator);
        if (headerRowIndex < 0)
        {
            preview.Errors.Add("未找到同时包含「部门」和「借方金额」的表头行。");
            return preview;
        }

        var columns = FindColumns(sheet.GetRow(headerRowIndex)!, formatter, evaluator);
        var missing = RequiredHeaders.Where(x => !columns.ContainsKey(x)).ToList();
        if (missing.Count > 0)
        {
            preview.Errors.Add("表头缺少必需列：" + string.Join("、", missing));
            return preview;
        }

        if (await _db.BudgetBatches.FindAsync(budgetBatchId) is null)
        {
            preview.Errors.Add("所选预算批次不存在。");
            return preview;
        }
        preview.IsStructureValid = true;

        var organizations = await _db.Organizations.AsNoTracking().ToListAsync();
        var orgByKey = organizations
            .GroupBy(x => NormalizeDepartment(x.Name))
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var subjects = await _db.Subjects.AsNoTracking().ToListAsync();
        var subjectByName = subjects
            .GroupBy(x => Compact(x.Name))
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        for (var rowIndex = headerRowIndex + 1; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row is null) continue;

            var deptText = GetText(row, columns["部门"], formatter, evaluator);
            if (string.IsNullOrWhiteSpace(deptText)) continue;
            // 跳过小计/合计类的说明行
            if (deptText.Contains("合计") || deptText.Contains("总计")) continue;

            preview.TotalRows++;

            if (!orgByKey.TryGetValue(NormalizeDepartment(deptText), out var organization))
            {
                if (!preview.UnmatchedDepartments.Contains(deptText))
                    preview.UnmatchedDepartments.Add(deptText);
                continue;
            }

            var subjectText = GetText(row, columns["预算科目2"], formatter, evaluator);
            if (string.IsNullOrWhiteSpace(subjectText))
            {
                if (!preview.UnmappedSubjects.Contains("（预算科目2 为空）"))
                    preview.UnmappedSubjects.Add("（预算科目2 为空）");
                continue;
            }

            if (!subjectByName.TryGetValue(Compact(subjectText), out var subject))
            {
                var alias = SubjectAliases.FirstOrDefault(x => Compact(x.Key) == Compact(subjectText));
                if (alias.Key is not null && subjectByName.TryGetValue(Compact(alias.Value), out var aliased))
                {
                    subject = aliased;
                }
                else
                {
                    if (!preview.UnmappedSubjects.Contains(subjectText))
                        preview.UnmappedSubjects.Add(subjectText);
                    continue;
                }
            }

            var amountText = GetText(row, columns["借方金额"], formatter, evaluator);
            if (!TryParseAmount(amountText, out var amount))
            {
                preview.Errors.Add($"第 {rowIndex + 1} 行借方金额无法识别：{amountText}");
                continue;
            }

            var accountingYear = columns.TryGetValue("会计年度", out var yc)
                ? GetText(row, yc, formatter, evaluator)
                : DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
            var period = GetText(row, columns["期间"], formatter, evaluator);
            var voucherWord = columns.TryGetValue("凭证字", out var wc) ? GetText(row, wc, formatter, evaluator) : string.Empty;
            var voucherNo = columns.TryGetValue("凭证号", out var nc) ? GetText(row, nc, formatter, evaluator) : string.Empty;
            var subjectFullName = GetText(row, columns["科目全名"], formatter, evaluator);

            preview.Rows.Add(new ParsedActualRow
            {
                OrganizationId = organization.Id,
                OrganizationName = organization.Name,
                SubjectId = subject.Id,
                AccountingPeriod = BuildPeriod(accountingYear, period),
                VoucherDate = ParseDate(GetText(row, columns["日期"], formatter, evaluator)),
                VoucherNumber = Trim($"{voucherWord}-{voucherNo}".Trim('-'), 100),
                AccountingSubjectName = Trim(string.IsNullOrWhiteSpace(subjectFullName) ? subject.Name : subjectFullName, 200),
                ExpenseType = "基本支出",
                AccountAmount = amount,
                AdjustmentAmount = 0m,
                ApprovedAmount = amount,
                SourceRowNumber = rowIndex + 1
            });
        }

        if (preview.Rows.Count == 0)
            preview.Errors.Add("没有可导入的有效数据行，请检查表头与数据是否完整。");

        return preview;
    }

    /// <summary>在前 30 行内查找表头行（同时含「部门」与「借方金额」）。</summary>
    private static int FindHeaderRow(ISheet sheet, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        var last = Math.Min(sheet.LastRowNum, 30);
        for (var i = sheet.FirstRowNum; i <= last; i++)
        {
            var row = sheet.GetRow(i);
            if (row is null) continue;
            var texts = new List<string>();
            for (var c = row.FirstCellNum; c < row.LastCellNum; c++)
                texts.Add(Compact(formatter.FormatCellValue(row.GetCell(c), evaluator)));
            if (texts.Contains("部门") && texts.Contains("借方金额")) return i;
        }
        return -1;
    }

    /// <summary>按表头文字建立「列名 → 列号」映射，避免写死列序号。</summary>
    private static Dictionary<string, int> FindColumns(IRow header, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = header.FirstCellNum; c < header.LastCellNum; c++)
        {
            var text = Compact(formatter.FormatCellValue(header.GetCell(c), evaluator));
            if (string.IsNullOrEmpty(text) || map.ContainsKey(text)) continue;
            map[text] = c;
        }
        return map;
    }

    private static string GetText(IRow row, int columnIndex, DataFormatter formatter, IFormulaEvaluator evaluator)
        => columnIndex < 0 ? string.Empty : formatter.FormatCellValue(row.GetCell(columnIndex), evaluator).Trim();

    /// <summary>部门名称归一化：去空格、去「部门」后缀、按别名转换。</summary>
    private static string NormalizeDepartment(string value)
    {
        var text = Compact(value);
        return DepartmentAliases.TryGetValue(text, out var mapped) ? mapped : text;
    }

    private static string Compact(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : string.Concat(value.Where(x => !char.IsWhiteSpace(x)));

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    /// <summary>把「会计年度 + 期间」规范成 yyyy-MM；期间可能带「月」或前导零。</summary>
    private static string BuildPeriod(string year, string period)
    {
        var digits = new string(period.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var month) && month is >= 1 and <= 12)
        {
            var y = int.TryParse(new string(year.Where(char.IsDigit).ToArray()), out var parsed) && parsed > 1900
                ? parsed
                : DateTime.Now.Year;
            return $"{y:D4}-{month:D2}";
        }
        return string.IsNullOrWhiteSpace(period) ? string.Empty : Trim(period, 20) ?? string.Empty;
    }

    private static DateTime? ParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string[] formats =
        {
            "yyyy/M/d", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy/M/d HH:mm:ss",
            "yyyy.MM.dd", "yyyy年M月d日", "M/d/yyyy", "yyyyMMdd"
        };
        if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exact))
            return exact;
        return DateTime.TryParse(text, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static bool TryParseAmount(string text, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.Trim();
        if (trimmed is "-" or "—" or "/") return true;
        trimmed = trimmed.Replace(",", string.Empty).Replace("，", string.Empty);

        if (!decimal.TryParse(trimmed, NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowParentheses,
                CultureInfo.GetCultureInfo("zh-CN"), out amount) &&
            !decimal.TryParse(trimmed, NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowParentheses,
                CultureInfo.InvariantCulture, out amount))
            return false;

        amount = decimal.Round(amount, 2);
        return true;
    }
}
