using GroupBudget.Models;

namespace GroupBudget.ViewModels;

/// <summary>管理员「预算执行与增减分析」页数据。全部由基准表与填报明细聚合，只读。</summary>
public class ExecutionAnalysisViewModel
{
    public BudgetBatch? Batch { get; set; }

    public decimal BudgetTotal { get; set; }

    /// <summary>2026 决算合计：集团口径，取实际发生数明细全量（含各部门人员费用）。</summary>
    public decimal ActualTotal { get; set; }

    /// <summary>部门口径的决算合计：人员费用已归口人事部，用于说明两张部门图与合计的差异。</summary>
    public decimal DeptActualTotal { get; set; }

    public decimal PlanTotal { get; set; }

    /// <summary>2026 年基本支出预算（不含项目）。</summary>
    public decimal BasicBudget { get; set; }

    /// <summary>2026 年项目支出预算，单独列示，不与基本支出合并。</summary>
    public decimal ProjectBudget { get; set; }

    /// <summary>2026 年基本支出决算：取实际发生数明细中非「项目支出」的部分。</summary>
    public decimal BasicActual { get; set; }

    /// <summary>2026 年项目支出决算：取实际发生数明细中标记为「项目支出」的部分。</summary>
    public decimal ProjectActual { get; set; }

    public decimal BasicPlan { get; set; }

    public decimal ProjectPlan { get; set; }

    /// <summary>项目预算已下达但决算尚未按项目归集（决算明细全部标记为基本支出）。</summary>
    public bool ProjectActualPending => ProjectBudget != 0m && ProjectActual == 0m;

    public decimal? BasicExecRate => Ratio(BasicActual, BasicBudget);

    public decimal? ProjectExecRate => ProjectActualPending ? null : Ratio(ProjectActual, ProjectBudget);

    public decimal? BasicChangeRate => Ratio(BasicPlan - BasicActual, BasicActual);

    public decimal? ProjectChangeRate => ProjectActualPending ? null : Ratio(ProjectPlan - ProjectActual, ProjectActual);

    /// <summary>是否已开始填报次年预算（全为 0 时增减比例无参考意义）。</summary>
    public bool HasPlan => PlanTotal != 0m;

    /// <summary>执行率 = 2026 决算 ÷ 2026 预算；预算为 0 时为空（页面显示「—」）。</summary>
    public decimal? ExecRate => Ratio(ActualTotal, BudgetTotal);

    /// <summary>增减率 =（2027 预算 − 2026 决算）÷ 2026 决算。</summary>
    public decimal? PlanChangeRate => Ratio(PlanTotal - ActualTotal, ActualTotal);

    public decimal PlanChangeAmount => PlanTotal - ActualTotal;

    /// <summary>执行率超过 100%（决算大于预算）的部门数。</summary>
    public int OverBudgetDepartments => Departments.Count(d => d.ExecRate > 1m);

    /// <summary>执行率超过 100%（决算大于预算）的科目数，由控制器计算。</summary>
    public int OverBudgetSubjects { get; set; }

    /// <summary>缺少 2026 预算但有决算的部门数（执行率无法计算）。</summary>
    public int NoBudgetDepartments => Departments.Count(d => d.ExecRate == null);

    public List<ExecutionRow> Departments { get; set; } = new();

    /// <summary>
    /// 本年预算为 0 的部门（执行率无分母、增减比例无意义），不进图表，
    /// 只在「集团汇总表」明细里列示；其决算仍计入集团合计。
    /// </summary>
    public List<ExecutionRow> ChartExcludedDepartments { get; set; } = new();

    public List<ExecutionRow> Categories { get; set; } = new();

    /// <summary>按增减额绝对值排序的前若干个科目。</summary>
    public List<ExecutionRow> TopSubjects { get; set; } = new();

    /// <summary>执行率条形的缩放上限，保证 100% 基准线落在轨道内。</summary>
    public decimal ExecScale
    {
        get
        {
            var max = Departments.Where(d => d.ExecRate != null).Select(d => d.ExecRate!.Value).DefaultIfEmpty(0m).Max();
            return Math.Max(1m, max);
        }
    }

    /// <summary>增减率双向条形的半幅缩放上限。</summary>
    public decimal ChangeScale
    {
        get
        {
            var max = Departments.Where(d => d.ChangeRate != null).Select(d => Math.Abs(d.ChangeRate!.Value)).DefaultIfEmpty(0m).Max();
            return Math.Max(0.2m, max);
        }
    }

    public static decimal? Ratio(decimal numerator, decimal denominator)
        => denominator == 0m ? null : numerator / denominator;
}

public class ExecutionRow
{
    public string Name { get; set; } = string.Empty;

    /// <summary>附加说明，例如费用类别；无值时不显示。</summary>
    public string? Note { get; set; }

    public decimal Budget { get; set; }
    public decimal Actual { get; set; }
    public decimal Plan { get; set; }

    public decimal? ExecRate { get; set; }

    public decimal ChangeAmount => Plan - Actual;

    public decimal? ChangeRate => ExecutionAnalysisViewModel.Ratio(Plan - Actual, Actual);
}
