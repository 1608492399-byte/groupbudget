using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Models;

/// <summary>系统角色常量（中文角色名，种子数据自动创建）。</summary>
public static class AppRoles
{
    public const string Admin = "系统管理员";
    public const string Reviewer = "集团审核员";
    public const string Filler = "部门填报员";
}

/// <summary>批次状态：未下发 → 已下发(开放填报) → 已截止 → 已归档。</summary>
public static class BatchStatus
{
    public const string Draft = "未下发";
    public const string Published = "已下发";
    public const string Closed = "已截止";
    public const string Archived = "已归档";
}

/// <summary>单据状态：草稿 → 已提交 → 审核通过 / 已打回。</summary>
public static class FormStatus
{
    public const string Draft = "草稿";
    public const string Submitted = "已提交";
    public const string Approved = "审核通过";
    public const string Returned = "已打回";
}

/// <summary>报送批次：一次预算编制工作，例如「编制 2027 年预算」。</summary>
public class BudgetBatch
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>要编制的预算年度，例如 2027。</summary>
    public int PlanYear { get; set; }

    /// <summary>基准年度（本年），例如 2026。</summary>
    public int BaselineYear { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = BatchStatus.Draft;

    /// <summary>偏差阈值，0.20 表示 20%，可在管理端调整。</summary>
    [Precision(5, 2)]
    public decimal DeviationThreshold { get; set; } = 0.20m;

    /// <summary>基准数据当前版本号；管理员修改基准会生成新版本。</summary>
    public int CurrentBaselineVersion { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }

    public ICollection<BaselineRecord> Baselines { get; set; } = new List<BaselineRecord>();
    public ICollection<BaselineSnapshot> Snapshots { get; set; } = new List<BaselineSnapshot>();
    public ICollection<FilingForm> FilingForms { get; set; } = new List<FilingForm>();
    public ICollection<ActualDetail> ActualDetails { get; set; } = new List<ActualDetail>();
}

/// <summary>预算科目字典（示例科目，可自行扩充为树形结构）。</summary>
public class Subject
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>费用类别，例如「日常管理费」。</summary>
    [MaxLength(50)]
    public string? Category { get; set; }

    /// <summary>父科目 Id，预留树形结构；当前种子均为末级科目。</summary>
    public int? ParentId { get; set; }

    public bool IsLeaf { get; set; } = true;
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }

    public ICollection<ReportLine> ReportLines { get; set; } = new List<ReportLine>();
    public ICollection<SubjectDepartmentScope> DepartmentScopes { get; set; } = new List<SubjectDepartmentScope>();
    public ICollection<SubjectMapping> Mappings { get; set; } = new List<SubjectMapping>();
}

/// <summary>会计科目名称与预算科目的映射规则。</summary>
public class SubjectMapping
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string AccountingSubjectName { get; set; } = string.Empty;

    public int SubjectId { get; set; }

    [Required, MaxLength(20)]
    public string ExpenseType { get; set; } = "基本支出";

    [MaxLength(500)]
    public string? Remarks { get; set; }

    public Subject Subject { get; set; } = null!;
}

/// <summary>科目适用的部门范围；无记录时由业务逻辑视为全部部门适用。</summary>
public class SubjectDepartmentScope
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public int OrganizationId { get; set; }

    public Subject Subject { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}

/// <summary>一次决算文件导入的批次及处理统计。</summary>
public class BudgetImportBatch
{
    public int Id { get; set; }

    [Required, MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SheetName { get; set; } = string.Empty;

    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }

    [Required, MaxLength(100)]
    public string ImportedBy { get; set; } = string.Empty;

    public DateTime ImportedAt { get; set; } = DateTime.Now;

    [Required, MaxLength(20)]
    public string Status { get; set; } = string.Empty;

    public string? ErrorSummary { get; set; }

    public ICollection<ActualDetail> ActualDetails { get; set; } = new List<ActualDetail>();
}

/// <summary>决算凭证明细；审定金额是预算对比使用的决算口径。</summary>
public class ActualDetail
{
    public int Id { get; set; }
    public int BudgetImportBatchId { get; set; }
    public int BudgetBatchId { get; set; }
    public int OrganizationId { get; set; }

    /// <summary>导入时匹配到的预算科目；用于决算明细钻取与勾稽，历史数据可能为空。</summary>
    public int? SubjectId { get; set; }

    [Required, MaxLength(20)]
    public string AccountingPeriod { get; set; } = string.Empty;

    public DateTime? VoucherDate { get; set; }

    [MaxLength(100)]
    public string? VoucherNumber { get; set; }

    [Required, MaxLength(200)]
    public string AccountingSubjectName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string ExpenseType { get; set; } = "基本支出";

    [Precision(18, 2)]
    public decimal AccountAmount { get; set; }

    [Precision(18, 2)]
    public decimal AdjustmentAmount { get; set; }

    [Precision(18, 2)]
    public decimal ApprovedAmount { get; set; }

    public int SourceRowNumber { get; set; }

    public BudgetImportBatch BudgetImportBatch { get; set; } = null!;
    public BudgetBatch BudgetBatch { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
    public Subject? Subject { get; set; }
}

/// <summary>管理员录入的本年基准数据，按「批次 × 部门 × 科目 × 版本」存储。部门只读，绝对不可修改。</summary>
public class BaselineRecord
{
    public int Id { get; set; }
    public int BudgetBatchId { get; set; }
    public int OrganizationId { get; set; }
    public int SubjectId { get; set; }

    /// <summary>2026 年基本支出预算数。</summary>
    [Precision(18, 2)]
    public decimal BudgetBaseline { get; set; }

    /// <summary>2026 年基本支出决算数，增减比较的基数。</summary>
    [Precision(18, 2)]
    public decimal ActualBaseline { get; set; }

    /// <summary>2026 年项目支出预算数。</summary>
    [Precision(18, 2)]
    public decimal BudgetBaselineProject { get; set; } = 0m;

    /// <summary>2026 年项目支出决算数。</summary>
    [Precision(18, 2)]
    public decimal ActualBaselineProject { get; set; } = 0m;

    public int Version { get; set; } = 1;
    public bool IsLocked { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? UpdatedBy { get; set; }

    public BudgetBatch BudgetBatch { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
}

/// <summary>下发瞬间的基准数据快照，锁定后不随管理员后续修改而变化，审计可追溯。</summary>
public class BaselineSnapshot
{
    public int Id { get; set; }
    public int BudgetBatchId { get; set; }
    public int Version { get; set; }
    public int OrganizationId { get; set; }
    public int SubjectId { get; set; }

    [Precision(18, 2)]
    public decimal BudgetBaseline { get; set; }

    [Precision(18, 2)]
    public decimal ActualBaseline { get; set; }

    [Precision(18, 2)]
    public decimal BudgetBaselineProject { get; set; } = 0m;

    [Precision(18, 2)]
    public decimal ActualBaselineProject { get; set; } = 0m;

    public DateTime SnapshotAt { get; set; } = DateTime.Now;
    public string? Operator { get; set; }

    public BudgetBatch BudgetBatch { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
}

/// <summary>填报明细行：2026 两列是快照只读，只有 2027 预算数可填。</summary>
public class ReportLine
{
    public int Id { get; set; }
    public int FilingFormId { get; set; }
    public int SubjectId { get; set; }

    [Required, MaxLength(20)]
    public string ExpenseType { get; set; } = "基本支出";

    /// <summary>下发时冻结的 2026 预算数，服务端写入，接口拒绝前端覆盖。</summary>
    [Precision(18, 2)]
    public decimal Budget2026 { get; set; }

    /// <summary>下发时冻结的 2026 决算数，服务端写入，接口拒绝前端覆盖。</summary>
    [Precision(18, 2)]
    public decimal Actual2026 { get; set; }

    /// <summary>部门填报的次年（2027）预算数。</summary>
    [Precision(18, 2)]
    public decimal Budget2027 { get; set; }

    /// <summary>偏差超阈值时必须填写的增减原因。</summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>本行基准对应的快照版本号。</summary>
    public int BaselineVersion { get; set; }

    public FilingForm FilingForm { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
}

/// <summary>单据版本留痕：提交、打回时保存明细快照，只增不改。</summary>
public class FormVersion
{
    public int Id { get; set; }
    public int FilingFormId { get; set; }
    public int VersionNumber { get; set; }

    [Required, MaxLength(30)]
    public string Action { get; set; } = string.Empty;

    /// <summary>明细行的 JSON 快照。</summary>
    public string SnapshotJson { get; set; } = "[]";

    [MaxLength(1000)]
    public string? Comments { get; set; }

    public string? Operator { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public FilingForm FilingForm { get; set; } = null!;
}

/// <summary>操作日志，只增不改。</summary>
public class OperationLog
{
    public int Id { get; set; }
    public string? UserName { get; set; }

    [Required, MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Target { get; set; }

    [MaxLength(1000)]
    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>登录账号与部门的绑定关系（不改动 Identity 内置用户表结构）。</summary>
public class UserOrganization
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
}
