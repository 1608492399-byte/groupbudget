namespace GroupBudget.Services;

/// <summary>
/// 决算口径（2026-11-25 与财务确认，用户选定「完全对齐 Excel」）。
/// 目标值：《部门支出预算执行总表》S98「2026年1月-10月决算数」＝ 29,864,792.11 元。
///
/// 系统原始凭证明细合计为 30,296,124.89 元，差额 431,332.78 元由三笔构成，
/// 源表透视表对这三笔全部未计入，系统按同一口径不计入，保证
/// 「首页 / 执行分析 / 汇总表 / 导出」四个出口同一个数：
///
/// ① 辞退福利 385,987.78 元（人事部 188,786.73 + 工程部 197,201.05）
///    源表校验公式 = 借方总计 − 辞退福利，属一次性支出，不进预算执行口径。
/// ② 人事部「工资」10 笔各 4,500 元的计提，合计 45,000 元
///    源表部门名写成「人事部门」（多一个「门」字），透视表无此成员，整块漏计。
/// ③ 营销部「广告费」345 元广告制作费
///    源表费用类别写成「营销推广费」，透视表只有人员/日常/设施/食堂四类，漏计。
///
/// 三笔均已与源表逐笔核对。财务修订源表后，只需清空本类中的排除规则并重新导入，
/// 系统即可回到「明细全量」口径。
/// </summary>
public static class ActualCaliber
{
    /// <summary>辞退福利：一次性支出，源表已剔除。</summary>
    public const string SeveranceKeyword = "辞退福利";

    /// <summary>源表误写的费用类别，透视表无此类目。</summary>
    public const string UnmappedMarketingKeyword = "营销推广费";

    /// <summary>计提工资所在预算科目。</summary>
    public const string PayrollSubjectName = "工资";

    /// <summary>源表「人事部门」计提工资的单笔金额（10 笔 × 4,500）。</summary>
    public const decimal UnmappedPayrollUnitAmount = 4_500m;

    /// <summary>源表「人事部门」计提工资合计。</summary>
    public const decimal UnmappedPayrollTotal = 45_000m;

    /// <summary>源表「营销推广费」广告制作费合计。</summary>
    public const decimal UnmappedMarketingTotal = 345m;

    /// <summary>口径调整总额 = 385,987.78 + 45,000 + 345。</summary>
    public const decimal KnownAdjustment = 431_332.78m;

    /// <summary>与源表对齐后的集团决算合计，仅作自检基准。</summary>
    public const decimal ExcelGroupActual = 29_864_792.11m;

    /// <summary>页面脚注用的口径说明。</summary>
    public const string Note =
        "与《部门支出预算执行总表》一致：已剔除辞退福利及源表透视表漏计的两笔支出，合计 431,332.78 元。";

    /// <summary>
    /// 判断一条实际发生数是否不计入决算口径。
    /// 只依赖科目名与金额，重新导入实际发生数后规则仍然生效。
    /// </summary>
    /// <param name="subjectName">预算科目名称，如「工资」。</param>
    /// <param name="accountSubjectName">会计科目全名，如「管理费用_人员费用_辞退福利」。</param>
    /// <param name="amount">借方金额。</param>
    public static bool IsExcluded(string? subjectName, string? accountSubjectName, decimal amount)
    {
        if (!string.IsNullOrEmpty(accountSubjectName) &&
            accountSubjectName.Contains(SeveranceKeyword, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(accountSubjectName) &&
            accountSubjectName.Contains(UnmappedMarketingKeyword, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(subjectName, PayrollSubjectName, StringComparison.Ordinal) &&
            Math.Abs(amount - UnmappedPayrollUnitAmount) < 0.005m)
        {
            return true;
        }

        return false;
    }
}
