using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Models;

/// <summary>一张填报单 = 某部门 + 某批次。状态由服务端控制，已提交/已通过一律拒绝修改。</summary>
public class FilingForm
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string FormNumber { get; set; } = string.Empty;

    public int BudgetBatchId { get; set; }
    public int OrganizationId { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = FormStatus.Draft;

    /// <summary>乐观版本号，防止重复提交和并发覆盖。</summary>
    public int VersionNumber { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }

    [MaxLength(1000)]
    public string? ReturnReason { get; set; }

    public BudgetBatch BudgetBatch { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
    public ICollection<ReportLine> Lines { get; set; } = new List<ReportLine>();
    public ICollection<ReviewRecord> ReviewRecords { get; set; } = new List<ReviewRecord>();
    public ICollection<FormVersion> FormVersions { get; set; } = new List<FormVersion>();
}
