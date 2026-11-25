using GroupBudget.Models;

namespace GroupBudget.ViewModels;

/// <summary>首页仪表盘数据。只读聚合，不涉及任何写操作。</summary>
public class HomeDashboardViewModel
{
    public BudgetBatch? Batch { get; set; }

    public int DepartmentCount { get; set; }
    public int SubjectCount { get; set; }

    /// <summary>已提交、等待集团审核的单据数。</summary>
    public int PendingCount { get; set; }

    public int FormTotal { get; set; }
    public int FormDraft { get; set; }
    public int FormSubmitted { get; set; }
    public int FormApproved { get; set; }
    public int FormReturned { get; set; }

    /// <summary>本批次已填报的次年预算合计（元）。</summary>
    public decimal PlannedTotal { get; set; }

    public bool IsAuthenticated { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsReviewer { get; set; }
    public bool IsFiller { get; set; }

    public string RoleName { get; set; } = "访客";

    /// <summary>填报员所属部门名称。</summary>
    public string? OrganizationName { get; set; }

    /// <summary>填报员本批次单据状态。</summary>
    public string? MyFormStatus { get; set; }

    /// <summary>已提交或已审核（通过/退回）的单据占比（0-100）。</summary>
    public int ProgressPercent
        => FormTotal == 0 ? 0 : (int)Math.Round((FormSubmitted + FormApproved + FormReturned) * 100m / FormTotal);

    public WorkflowSliderViewModel Workflow
        => WorkflowSliderViewModel.Create(Batch, FormDraft, FormSubmitted, FormApproved, FormReturned, FormTotal, PendingCount);
}
