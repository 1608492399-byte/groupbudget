using GroupBudget.Models;

namespace GroupBudget.ViewModels;

public class WorkflowSliderViewModel
{
    public int CurrentStage { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<WorkflowStageViewModel> Stages { get; set; } = new();

    public static WorkflowSliderViewModel Create(
        BudgetBatch? batch,
        int draft,
        int submitted,
        int approved,
        int returned,
        int total,
        int pending)
    {
        var currentStage = batch == null || batch.Status == BatchStatus.Draft ? 0
            : batch.Status == BatchStatus.Archived ? 4
            : pending > 0 ? 2
            : approved + returned > 0 ? 3
            : 1;

        return new WorkflowSliderViewModel
        {
            CurrentStage = currentStage,
            Summary = batch == null ? "等待管理员创建批次" : $"{total} 个填报单 · 进度 {(total == 0 ? 0 : (int)Math.Round((submitted + approved + returned) * 100m / total))}%",
            Stages = new List<WorkflowStageViewModel>
            {
                new("草稿", $"{draft} 单待完善", "填报员在本部门工作区编制预算。草稿可持续修改，金额变动由系统计算。"),
                new("提交", $"{submitted + approved + returned} / {total} 单已提交", "填报完成后提交，系统记录版本与提交时间；超过偏差阈值时需填写说明。"),
                new("集团审核", $"{pending} 单待审核", "集团审核员核对部门预算与基准数据，可通过或退回并填写审核意见。"),
                new("通过 / 退回", $"通过 {approved} · 退回 {returned}", "审核通过后进入归档准备；退回单据可按意见修订后重新提交。"),
                new("归档", batch?.Status == BatchStatus.Archived ? "批次已归档" : "等待批次归档", "批次归档后，相关预算版本与审核记录保留，汇总数据可供导出与追溯。")
            }
        };
    }
}

public record WorkflowStageViewModel(string Title, string Meta, string Detail);
