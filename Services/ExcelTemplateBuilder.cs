using GroupBudget.Models;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace GroupBudget.Services;

/// <summary>生成决算导入模板与会计科目映射模板（.xlsx），列名与导入解析逻辑严格一致。</summary>
public static class ExcelTemplateBuilder
{
    /// <summary>决算导入模板的列名，顺序与附表7 一致；解析时按表头名称识别，改顺序不影响导入。</summary>
    public static readonly string[] ActualHeaders =
    {
        "公司", "日期", "会计期间", "凭证字号", "会计科目", "预算归属部门", "费用属性",
        "账套金额", "调整金额", "审定金额"
    };

    public static byte[] BuildActualTemplate(IEnumerable<string> departments, IEnumerable<Subject> subjects)
    {
        var departmentList = departments.ToList();
        IWorkbook workbook = new XSSFWorkbook();

        var sheet = workbook.CreateSheet("决算导入");
        var headerStyle = CreateHeaderStyle(workbook);
        var amountStyle = CreateAmountStyle(workbook);
        var dateStyle = CreateDateStyle(workbook);

        var header = sheet.CreateRow(0);
        for (var i = 0; i < ActualHeaders.Length; i++)
        {
            var cell = header.CreateCell(i);
            cell.SetCellValue(ActualHeaders[i]);
            cell.CellStyle = headerStyle;
        }

        for (var i = 0; i < ActualHeaders.Length; i++)
        {
            sheet.SetColumnWidth(i, (i is 4 or 5) ? 22 * 256 : 16 * 256);
        }

        // 金额列与日期列的显示格式先铺好，填数时直接是两位小数/日期
        for (var rowIndex = 1; rowIndex <= 2000; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex) ?? sheet.CreateRow(rowIndex);
            row.CreateCell(1).CellStyle = dateStyle;
            row.CreateCell(7).CellStyle = amountStyle;
            row.CreateCell(8).CellStyle = amountStyle;
            row.CreateCell(9).CellStyle = amountStyle;
        }

        AddListValidation(sheet, 5, departmentList, 2000);
        AddListValidation(sheet, 6, new[] { "基本支出", "项目支出" }, 2000);

        BuildActualHelpSheet(workbook, departmentList, headerStyle);
        BuildDepartmentSheet(workbook, departmentList, headerStyle);
        BuildSubjectSheet(workbook, subjects, headerStyle);

        return ToBytes(workbook);
    }

    public static byte[] BuildMappingTemplate(IEnumerable<Subject> subjects)
    {
        var subjectList = subjects.ToList();
        IWorkbook workbook = new XSSFWorkbook();

        var sheet = workbook.CreateSheet("会计科目映射");
        var headerStyle = CreateHeaderStyle(workbook);
        var headers = new[] { "会计科目名称", "预算科目编码", "预算科目名称", "费用属性", "备注" };

        var header = sheet.CreateRow(0);
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = header.CreateCell(i);
            cell.SetCellValue(headers[i]);
            cell.CellStyle = headerStyle;
        }
        sheet.SetColumnWidth(0, 30 * 256);
        sheet.SetColumnWidth(1, 14 * 256);
        sheet.SetColumnWidth(2, 22 * 256);
        sheet.SetColumnWidth(3, 12 * 256);
        sheet.SetColumnWidth(4, 30 * 256);

        AddListValidation(sheet, 3, new[] { "基本支出", "项目支出" }, 2000);
        AddListValidation(sheet, 2, subjectList.Select(x => x.Name).ToList(), 2000);
        AddListValidation(sheet, 1, subjectList.Select(x => x.Code).ToList(), 2000);

        BuildMappingHelpSheet(workbook, subjectList, headerStyle);
        BuildSubjectSheet(workbook, subjectList, headerStyle);

        return ToBytes(workbook);
    }

    private static void BuildActualHelpSheet(IWorkbook workbook, List<string> departments, ICellStyle headerStyle)
    {
        var help = workbook.CreateSheet("填写说明");
        help.SetColumnWidth(0, 18 * 256);
        help.SetColumnWidth(1, 60 * 256);
        help.SetColumnWidth(2, 28 * 256);

        var title = help.CreateRow(0);
        title.CreateCell(0).SetCellValue("决算导入模板填写说明");
        title.GetCell(0)!.CellStyle = headerStyle;

        var lines = new (string Field, string Note, string Example)[]
        {
            ("公司", "填集团内公司名称，仅用于留痕，不参与部门匹配。", "示例公司"),
            ("日期", "凭证日期，格式 YYYY-MM-DD 或 Excel 日期；可留空。", "2025-03-31"),
            ("会计期间", "必填，作为明细筛选依据，建议格式 YYYY-MM。", "2025-03"),
            ("凭证字号", "凭证编号，可留空。", "记-0012"),
            ("会计科目", "必填。必须先在「会计科目映射」里配好对应预算科目，否则该行会被列为未映射。", "差旅费"),
            ("预算归属部门", "必填，必须是「部门清单」表中的 11 个部门名称之一。", departments.FirstOrDefault() ?? "投资一部"),
            ("费用属性", "基本支出 / 项目支出。必须与会计科目映射里配置的费用属性一致。", "基本支出"),
            ("账套金额", "账面金额，仅留痕，不参与汇总计算。", "12000.00"),
            ("调整金额", "审计调整金额，仅留痕，不参与汇总计算。", "0.00"),
            ("审定金额", "必填，决算口径以此为准，参与 2026 决算汇总与增减比较。", "12000.00")
        };

        var header = help.CreateRow(1);
        header.CreateCell(0).SetCellValue("列名");
        header.CreateCell(1).SetCellValue("填写要求");
        header.CreateCell(2).SetCellValue("示例");
        for (var i = 0; i < 3; i++) header.GetCell(i)!.CellStyle = headerStyle;

        for (var i = 0; i < lines.Length; i++)
        {
            var row = help.CreateRow(i + 2);
            row.CreateCell(0).SetCellValue(lines[i].Field);
            row.CreateCell(1).SetCellValue(lines[i].Note);
            row.CreateCell(2).SetCellValue(lines[i].Example);
        }

        var footer = help.CreateRow(lines.Length + 4);
        footer.CreateCell(0).SetCellValue("注意：本说明页与「部门清单」「预算科目清单」不会被导入，只有「决算导入」这张表的数据行会被读取。");
    }

    private static void BuildMappingHelpSheet(IWorkbook workbook, List<Subject> subjects, ICellStyle headerStyle)
    {
        var help = workbook.CreateSheet("填写说明");
        help.SetColumnWidth(0, 18 * 256);
        help.SetColumnWidth(1, 60 * 256);
        help.SetColumnWidth(2, 26 * 256);

        var title = help.CreateRow(0);
        title.CreateCell(0).SetCellValue("会计科目映射模板填写说明");
        title.GetCell(0)!.CellStyle = headerStyle;

        var lines = new (string Field, string Note, string Example)[]
        {
            ("会计科目名称", "必填，填写决算文件（附表7）里出现的会计科目原文。同一名称只会保留一条映射，重复导入按更新处理。", "差旅费"),
            ("预算科目编码", "与「预算科目名称」二选一即可，填编码更准。编码见「预算科目清单」页。", subjects.FirstOrDefault()?.Code ?? "103"),
            ("预算科目名称", "预算科目清单中的名称，必须完全一致。", subjects.FirstOrDefault()?.Name ?? "差旅费"),
            ("费用属性", "基本支出 / 项目支出，不填默认基本支出。", "基本支出"),
            ("备注", "可留空。", "原表按部门归集")
        };

        var header = help.CreateRow(1);
        header.CreateCell(0).SetCellValue("列名");
        header.CreateCell(1).SetCellValue("填写要求");
        header.CreateCell(2).SetCellValue("示例");
        for (var i = 0; i < 3; i++) header.GetCell(i)!.CellStyle = headerStyle;

        for (var i = 0; i < lines.Length; i++)
        {
            var row = help.CreateRow(i + 2);
            row.CreateCell(0).SetCellValue(lines[i].Field);
            row.CreateCell(1).SetCellValue(lines[i].Note);
            row.CreateCell(2).SetCellValue(lines[i].Example);
        }

        var footer = help.CreateRow(lines.Length + 4);
        footer.CreateCell(0).SetCellValue("注意：只读取「会计科目映射」这张表；编码或名称匹配不到预算科目的行会被列入错误清单，不会写入。");
    }

    private static void BuildDepartmentSheet(IWorkbook workbook, List<string> departments, ICellStyle headerStyle)
    {
        var sheet = workbook.CreateSheet("部门清单");
        var header = sheet.CreateRow(0);
        header.CreateCell(0).SetCellValue("预算归属部门（可复制到数据表）");
        header.GetCell(0)!.CellStyle = headerStyle;
        sheet.SetColumnWidth(0, 24 * 256);

        for (var i = 0; i < departments.Count; i++)
            sheet.CreateRow(i + 1).CreateCell(0).SetCellValue(departments[i]);
    }

    private static void BuildSubjectSheet(IWorkbook workbook, IEnumerable<Subject> subjects, ICellStyle headerStyle)
    {
        var sheet = workbook.CreateSheet("预算科目清单");
        var header = sheet.CreateRow(0);
        header.CreateCell(0).SetCellValue("预算科目编码");
        header.CreateCell(1).SetCellValue("预算科目名称");
        header.CreateCell(2).SetCellValue("费用类别");
        for (var i = 0; i < 3; i++) header.GetCell(i)!.CellStyle = headerStyle;
        sheet.SetColumnWidth(0, 16 * 256);
        sheet.SetColumnWidth(1, 24 * 256);
        sheet.SetColumnWidth(2, 16 * 256);

        var rowIndex = 1;
        foreach (var subject in subjects)
        {
            var row = sheet.CreateRow(rowIndex++);
            row.CreateCell(0).SetCellValue(subject.Code);
            row.CreateCell(1).SetCellValue(subject.Name);
            row.CreateCell(2).SetCellValue(subject.Category ?? string.Empty);
        }
    }

    private static ICellStyle CreateHeaderStyle(IWorkbook workbook)
    {
        var style = workbook.CreateCellStyle();
        var font = workbook.CreateFont();
        font.IsBold = true;
        style.SetFont(font);
        style.FillForegroundColor = IndexedColors.Grey25Percent.Index;
        style.FillPattern = FillPattern.SolidForeground;
        style.Alignment = HorizontalAlignment.Center;
        style.VerticalAlignment = VerticalAlignment.Center;
        style.BorderBottom = BorderStyle.Thin;
        return style;
    }

    private static ICellStyle CreateAmountStyle(IWorkbook workbook)
    {
        var style = workbook.CreateCellStyle();
        style.DataFormat = workbook.CreateDataFormat().GetFormat("#,##0.00");
        return style;
    }

    private static ICellStyle CreateDateStyle(IWorkbook workbook)
    {
        var style = workbook.CreateCellStyle();
        style.DataFormat = workbook.CreateDataFormat().GetFormat("yyyy-mm-dd");
        return style;
    }

    /// <summary>给某列加下拉列表校验，减少手填错部门名/费用属性的概率。</summary>
    private static void AddListValidation(ISheet sheet, int columnIndex, IList<string> values, int lastRow)
    {
        var effectiveValues = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (effectiveValues.Count == 0) return;
        // 显式列表的总长度受 Excel 限制（255 字符），超长就不加校验，不影响导入
        if (string.Join(",", effectiveValues).Length > 250) return;

        var helper = sheet.GetDataValidationHelper();
        var constraint = helper.CreateExplicitListConstraint(effectiveValues.ToArray());
        var address = new CellRangeAddressList(1, lastRow, columnIndex, columnIndex);
        var validation = helper.CreateValidation(constraint, address);
        validation.ShowErrorBox = true;
        validation.CreateErrorBox("取值不在清单内", "请从下拉列表中选择，或到「部门清单/预算科目清单」页复制。");
        validation.ShowPromptBox = true;
        validation.CreatePromptBox("选择取值", "可直接从下拉列表选择。");
        sheet.AddValidationData(validation);
    }

    private static byte[] ToBytes(IWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.Write(stream, true);
        workbook.Close();
        return stream.ToArray();
    }
}
