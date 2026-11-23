using System.ComponentModel.DataAnnotations;
using GroupBudget.Models;

namespace GroupBudget.ViewModels;

/// <summary>填报页一行：三列同屏，2026 两列只读。</summary>
public class LineViewModel
{
    public int ReportLineId { get; set; }
    public int SubjectId { get; set; }
    public string ExpenseType { get; set; } = "基本支出";

    [Display(Name = "费用类别")]
    public string? Category { get; set; }

    [Display(Name = "科目")]
    public string SubjectName { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    [Display(Name = "2026 预算数（只读）")]
    public decimal Budget2026 { get; set; }

    [Display(Name = "2026 决算数（只读）")]
    public decimal Actual2026 { get; set; }

    [Display(Name = "2027 预算数")]
    public decimal Budget2027 { get; set; }

    [Display(Name = "增减原因")]
    [MaxLength(500)]
    public string? Reason { get; set; }

    // 以下三项由服务端计算，仅用于展示
    public decimal ChangeAmount { get; set; }
    public decimal? ChangeRate { get; set; }
    public bool NeedsReason { get; set; }
}

/// <summary>填报 / 查看单据页面模型。</summary>
public class FilingEditViewModel
{
    public int FilingFormId { get; set; }
    public int BatchId { get; set; }
    public int OrganizationId { get; set; }
    public string FormNumber { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public int PlanYear { get; set; }
    public int BaselineYear { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal DeviationThreshold { get; set; }
    public int VersionNumber { get; set; }
    public bool ReadOnly { get; set; }
    public string? ReturnReason { get; set; }
    public List<LineViewModel> Lines { get; set; } = new();

    public decimal TotalBudget2026 => Lines.Sum(x => x.Budget2026);
    public decimal TotalActual2026 => Lines.Sum(x => x.Actual2026);
    public decimal TotalBudget2027 => Lines.Sum(x => x.Budget2027);
    public decimal TotalChange => Lines.Sum(x => x.ChangeAmount);
    public decimal BasicBudget2026 => Lines.Where(x => x.ExpenseType == "基本支出").Sum(x => x.Budget2026);
    public decimal BasicActual2026 => Lines.Where(x => x.ExpenseType == "基本支出").Sum(x => x.Actual2026);
    public decimal BasicBudget2027 => Lines.Where(x => x.ExpenseType == "基本支出").Sum(x => x.Budget2027);
    public decimal BasicChange => Lines.Where(x => x.ExpenseType == "基本支出").Sum(x => x.ChangeAmount);
    public decimal? BasicChangeRate => BasicActual2026 == 0m ? null : BasicChange / BasicActual2026;
    public decimal ProjectBudget2026 => Lines.Where(x => x.ExpenseType == "项目支出").Sum(x => x.Budget2026);
    public decimal ProjectActual2026 => Lines.Where(x => x.ExpenseType == "项目支出").Sum(x => x.Actual2026);
    public decimal ProjectBudget2027 => Lines.Where(x => x.ExpenseType == "项目支出").Sum(x => x.Budget2027);
    public decimal ProjectChange => Lines.Where(x => x.ExpenseType == "项目支出").Sum(x => x.ChangeAmount);
    public decimal? ProjectChangeRate => ProjectActual2026 == 0m ? null : ProjectChange / ProjectActual2026;
    public decimal? TotalChangeRate => TotalActual2026 == 0m ? null : TotalChange / TotalActual2026;
}

/// <summary>管理员录入基准数据的一行。</summary>
public class BaselineInputRow
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal BudgetBaseline { get; set; }
    public decimal ActualBaseline { get; set; }
    public decimal BudgetBaselineProject { get; set; }
    public decimal ActualBaselineProject { get; set; }
}

/// <summary>批次列表的一行。</summary>
public class BatchListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PlanYear { get; set; }
    public int BaselineYear { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CurrentBaselineVersion { get; set; }
    public decimal DeviationThreshold { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int FormCount { get; set; }
    public int SubmittedCount { get; set; }
}

/// <summary>管理员基准录入页面模型。</summary>
public class BaselineEditViewModel
{
    public int BatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public int BaselineYear { get; set; }
    public string BatchStatus { get; set; } = string.Empty;
    public int CurrentVersion { get; set; }
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public List<BaselineInputRow> Rows { get; set; } = new();
}

/// <summary>集团汇总表（按表1-部门支出预算总表版式）。</summary>
public class BudgetSummaryViewModel
{
    public int? BatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public int PlanYear { get; set; }
    public int BaselineYear { get; set; }
    public List<BatchListItem> Batches { get; set; } = new();
    public List<Organization> Organizations { get; set; } = new();
    public List<BudgetSummaryRow> Rows { get; set; } = new();
    public BudgetSummaryRow? TotalRow { get; set; }
}

/// <summary>汇总表一行：一个预算科目在各部门的金额，以及基本/项目/上年决算列。</summary>
public class BudgetSummaryRow
{
    public string? Category { get; set; }
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string? Code { get; set; }

    public Dictionary<int, decimal> BasicByOrganization { get; } = new();
    public Dictionary<int, decimal> ProjectByOrganization { get; } = new();

    public decimal BasicTotal { get; set; }
    public decimal ProjectTotal { get; set; }
    public decimal Subtotal => BasicTotal + ProjectTotal;

    public decimal Budget2026Total { get; set; }
    public decimal Actual2026Total { get; set; }
    public decimal Change => Subtotal - Actual2026Total;
    public decimal? Rate => Actual2026Total == 0m ? null : Change / Actual2026Total;
}

/// <summary>决算导入入口页面模型。</summary>
public class ActualImportPageViewModel
{
    public List<BudgetBatch> Batches { get; set; } = new();
    public List<Subject> Subjects { get; set; } = new();
    public List<SubjectMapping> SubjectMappings { get; set; } = new();
    public int? SelectedBatchId { get; set; }
    public bool HasSubjectMappings { get; set; }
    public string? Message { get; set; }
}

/// <summary>决算导入预览页面模型。</summary>
public class ActualImportPreviewViewModel
{
    public string Token { get; set; } = string.Empty;
    public int BudgetBatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SelectedSheetName { get; set; } = string.Empty;
    public List<string> SheetNames { get; set; } = new();
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }
    public bool HasExistingDetails { get; set; }
    public List<ActualImportSubtotalViewModel> Subtotals { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> UnmatchedDepartments { get; set; } = new();
    public List<string> UnmappedSubjects { get; set; } = new();
    public string? Message { get; set; }
}

/// <summary>决算导入预览中的部门费用属性小计。</summary>
public class ActualImportSubtotalViewModel
{
    public string OrganizationName { get; set; } = string.Empty;
    public string ExpenseType { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public decimal AccountAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
}

/// <summary>科目部门范围配置页模型。</summary>
public class SubjectDepartmentScopePageViewModel
{
    public List<BudgetBatch> Batches { get; set; } = new();
    public List<Organization> Organizations { get; set; } = new();
    public List<SubjectScopeRowViewModel> Rows { get; set; } = new();
    public List<SubjectScopeIssueViewModel> ManualIssues { get; set; } = new();
    public int SelectedBatchId { get; set; }
    public string? Message { get; set; }
}

public class SubjectScopeRowViewModel
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public List<int> OrganizationIds { get; set; } = new();
}

public class SubjectScopeIssueViewModel
{
    public string BatchName { get; set; } = string.Empty;
    public string FormNumber { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public string ExpenseType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Budget2026 { get; set; }
    public decimal Actual2026 { get; set; }
    public decimal Budget2027 { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>决算明细钻取页：按部门 × 科目 × 会计期间查看凭证级明细。</summary>
public class ActualDetailPageViewModel
{
    public int BatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public int BaselineYear { get; set; }

    public int? OrganizationId { get; set; }
    public string OrganizationName { get; set; } = "全部部门";
    public int? SubjectId { get; set; }
    public string SubjectName { get; set; } = "全部科目";
    public string? Period { get; set; }
    public string? ExpenseType { get; set; }

    /// <summary>部门填报员只能看本部门，界面不提供部门切换。</summary>
    public bool OrganizationLocked { get; set; }

    public List<ActualDetailRowViewModel> Rows { get; set; } = new();
    public List<string> Periods { get; set; } = new();
    public List<Organization> Organizations { get; set; } = new();

    public int RecordCount { get; set; }
    public decimal AccountTotal { get; set; }
    public decimal AdjustmentTotal { get; set; }
    public decimal ApprovedTotal { get; set; }

    /// <summary>对应填报单该科目的决算数，用于与明细勾稽。</summary>
    public decimal? LineActual { get; set; }
    public decimal? Difference => LineActual.HasValue ? ApprovedTotal - LineActual.Value : null;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => RecordCount == 0 ? 1 : (int)Math.Ceiling(RecordCount / (double)PageSize);
}

public class ActualDetailRowViewModel
{
    public DateTime? VoucherDate { get; set; }
    public string AccountingPeriod { get; set; } = string.Empty;
    public string? VoucherNumber { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public string AccountingSubjectName { get; set; } = string.Empty;
    public string ExpenseType { get; set; } = string.Empty;
    public decimal AccountAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public int SourceRowNumber { get; set; }
}

/// <summary>单据版本对比页：两个版本之间逐科目（含费用属性）比较 2027 预算。</summary>
public class VersionCompareViewModel
{
    public int FilingFormId { get; set; }
    public string FormNumber { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public List<VersionOption> Options { get; set; } = new();
    public int? FromVersionNumber { get; set; }
    public int? ToVersionNumber { get; set; }

    /// <summary>仅有 1 个版本时为 true，页面只显示提示。</summary>
    public bool OnlyOneVersion { get; set; }

    public bool ShowAll { get; set; }
    public int ChangedCount { get; set; }
    public decimal TotalIncrease { get; set; }
    public decimal TotalDecrease { get; set; }
    public decimal NetChange => TotalIncrease - TotalDecrease;

    public List<VersionCompareRow> Rows { get; set; } = new();
}

public class VersionOption
{
    public int Id { get; set; }
    public int VersionNumber { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Operator { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Comments { get; set; }

    public string Display =>
        $"V{VersionNumber} {Action} {CreatedAt:yyyy-MM-dd HH:mm} {(string.IsNullOrWhiteSpace(Operator) ? "" : Operator)}".Trim();
}

public class VersionCompareRow
{
    public int SubjectId { get; set; }
    public int SortOrder { get; set; }
    public string? Category { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string ExpenseType { get; set; } = string.Empty;
    public decimal FromBudget { get; set; }
    public decimal ToBudget { get; set; }
    public decimal Delta => ToBudget - FromBudget;
    public decimal? Rate => FromBudget == 0m ? null : Delta / FromBudget;
    public string? FromReason { get; set; }
    public string? ToReason { get; set; }

    public bool IsNew => FromBudget == 0m && ToBudget != 0m;
    public bool IsRemoved => FromBudget != 0m && ToBudget == 0m;
    public bool HasReasonChange => (FromReason ?? string.Empty) != (ToReason ?? string.Empty);
    public bool IsChanged => Delta != 0m || HasReasonChange;
}

/// <summary>导出页：选择批次后分别导出「预决算汇总（基本/项目分列）」与「表2 项目支出预算表」。</summary>
public class ExportIndexViewModel
{
    public int BatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public List<BudgetBatch> Batches { get; set; } = new();

    public int LineCount { get; set; }
    public decimal Budget2026Total { get; set; }
    public decimal Actual2026Total { get; set; }
    public decimal Budget2027Total { get; set; }

    public int ProjectSubjectCount { get; set; }
    public int DepartmentCount { get; set; }
}
