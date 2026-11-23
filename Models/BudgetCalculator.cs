namespace GroupBudget.Models;

/// <summary>
/// 增减口径（用户 2026-11-20 确认）：
/// 增减额 = 次年预算数 − 本年决算数；增幅 = 增减额 ÷ 本年决算数。
/// 分母为 0 时增幅留空，不报错也不显示 0%。
/// </summary>
public static class BudgetCalculator
{
    public static decimal Change(decimal budgetNextYear, decimal actualThisYear)
        => budgetNextYear - actualThisYear;

    public static decimal? Rate(decimal budgetNextYear, decimal actualThisYear)
        => actualThisYear == 0m ? null : Change(budgetNextYear, actualThisYear) / actualThisYear;

    /// <summary>偏差是否超过阈值。决算为 0 时，只要填报了金额就要求说明。</summary>
    public static bool NeedsReason(decimal budgetNextYear, decimal actualThisYear, decimal threshold)
    {
        // 0 视为尚未填报或计划停支，由「必填科目」校验和人工审核把关，不在页面上全量标红。
        if (budgetNextYear == 0m)
            return false;

        if (actualThisYear == 0m)
            return budgetNextYear != 0m;

        return Math.Abs(Change(budgetNextYear, actualThisYear) / actualThisYear) > threshold;
    }

    /// <summary>金额是否合法：非负且最多两位小数。</summary>
    public static bool IsValidAmount(decimal value)
        => value >= 0m && decimal.Round(value, 2) == value;
}
