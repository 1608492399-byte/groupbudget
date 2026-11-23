using System.Text.Json;
using GroupBudget.Data;
using GroupBudget.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>提供当前用户所属部门、操作留痕、版本快照等公共能力。</summary>
public abstract class AppControllerBase : Controller
{
    protected readonly ApplicationDbContext Db;
    protected readonly UserManager<IdentityUser> UserManager;

    protected AppControllerBase(ApplicationDbContext db, UserManager<IdentityUser> userManager)
    {
        Db = db;
        UserManager = userManager;
    }

    /// <summary>取当前登录账号绑定的部门；集团审核员/管理员未绑定部门时返回 null。</summary>
    protected async Task<int?> CurrentOrganizationIdAsync()
    {
        var userId = UserManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return null;

        return await Db.UserOrganizations
            .Where(x => x.UserId == userId)
            .Select(x => (int?)x.OrganizationId)
            .FirstOrDefaultAsync();
    }

    protected void WriteLog(string action, string? target = null, string? detail = null)
    {
        Db.OperationLogs.Add(new OperationLog
        {
            UserName = User.Identity?.Name,
            Action = action,
            Target = target,
            Detail = detail
        });
    }

    /// <summary>提交/打回时保存明细快照，只增不改，供审计追溯。</summary>
    protected void SaveVersionSnapshot(FilingForm form, string action, string? comments = null)
    {
        // ExpenseType 必须入快照：同一科目在单据里可能同时有「基本支出」「项目支出」两行，
        // 只按 SubjectId 无法区分，版本对比会串行。
        var snapshot = form.Lines
            .OrderBy(x => x.Id)
            .Select(line => new
            {
                line.SubjectId,
                line.ExpenseType,
                line.Budget2026,
                line.Actual2026,
                line.Budget2027,
                line.Reason
            }).ToList();

        Db.FormVersions.Add(new FormVersion
        {
            FilingForm = form,
            VersionNumber = form.VersionNumber,
            Action = action,
            SnapshotJson = JsonSerializer.Serialize(snapshot),
            Comments = comments,
            Operator = User.Identity?.Name
        });
    }
}
