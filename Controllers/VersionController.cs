using System.Text.Json;
using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>
/// 单据版本对比：任选两个版本，逐科目（含费用属性）比较 2027 预算差异。
/// 快照在提交 / 打回 / 通过时由 AppControllerBase.SaveVersionSnapshot 写入，只增不改。
/// </summary>
[Authorize(Roles = AppRoles.Admin + "," + AppRoles.Reviewer + "," + AppRoles.Filler)]
public class VersionController : AppControllerBase
{
    public VersionController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        : base(db, userManager) { }

    [HttpGet]
    public async Task<IActionResult> Compare(int formId, int? from = null, int? to = null, int showAll = 0)
    {
        var form = await Db.FilingForms
            .Include(x => x.Organization)
            .Include(x => x.BudgetBatch)
            .Include(x => x.Lines)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == formId);
        if (form is null) return NotFound();

        // 部门填报员只能看本部门单据
        if (User.IsInRole(AppRoles.Filler) &&
            !User.IsInRole(AppRoles.Admin) &&
            !User.IsInRole(AppRoles.Reviewer))
        {
            var own = await CurrentOrganizationIdAsync();
            if (own is null || own.Value != form.OrganizationId) return Forbid();
        }

        var versionEntities = await Db.FormVersions
            .Where(x => x.FilingFormId == formId)
            .OrderBy(x => x.VersionNumber).ThenBy(x => x.Id)
            .AsNoTracking()
            .ToListAsync();

        var subjects = await Db.Subjects.AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.Category, x.SortOrder })
            .ToDictionaryAsync(x => x.Id);

        // 历史快照可能没有 ExpenseType，用当前单据的行序兜底
        var expenseFallback = form.Lines
            .OrderBy(x => x.Id)
            .GroupBy(x => x.SubjectId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ExpenseType).ToList());

        var vm = new VersionCompareViewModel
        {
            FilingFormId = form.Id,
            FormNumber = form.FormNumber,
            OrganizationName = form.Organization?.Name ?? string.Empty,
            BatchName = form.BudgetBatch?.Name ?? string.Empty,
            Status = form.Status,
            ShowAll = showAll == 1,
            Options = versionEntities.Select(x => new VersionOption
            {
                Id = x.Id,
                VersionNumber = x.VersionNumber,
                Action = x.Action,
                Operator = x.Operator,
                CreatedAt = x.CreatedAt,
                Comments = x.Comments
            }).ToList()
        };

        if (versionEntities.Count < 2)
        {
            vm.OnlyOneVersion = true;
            var onlyVersion = versionEntities.FirstOrDefault();
            WriteLog("查看版本对比", form.FormNumber,
                onlyVersion is null ? "暂无版本快照" : $"V{onlyVersion.VersionNumber}（暂无可对比内容）");
            await Db.SaveChangesAsync();
            return View(vm);
        }

        var toVersion = versionEntities.FirstOrDefault(x => x.VersionNumber == to) ?? versionEntities[^1];
        var fromVersion = versionEntities.FirstOrDefault(x => x.VersionNumber == from) ?? versionEntities[^2];
        if (fromVersion.VersionNumber == toVersion.VersionNumber)
            fromVersion = versionEntities.FirstOrDefault(x => x.VersionNumber != toVersion.VersionNumber) ?? fromVersion;

        vm.FromVersionNumber = fromVersion.VersionNumber;
        vm.ToVersionNumber = toVersion.VersionNumber;
        vm.ShowAll = showAll == 1;

        WriteLog("查看版本对比", form.FormNumber,
            $"V{fromVersion.VersionNumber} → V{toVersion.VersionNumber}");
        await Db.SaveChangesAsync();

        var fromMap = ParseSnapshot(fromVersion.SnapshotJson, expenseFallback);
        var toMap = ParseSnapshot(toVersion.SnapshotJson, expenseFallback);

        var keys = fromMap.Keys.Union(toMap.Keys).ToList();
        var rows = new List<VersionCompareRow>();
        foreach (var key in keys)
        {
            fromMap.TryGetValue(key, out var a);
            toMap.TryGetValue(key, out var b);
            var subjectId = a?.SubjectId ?? b!.SubjectId;
            var expenseType = a?.ExpenseType ?? b?.ExpenseType ?? string.Empty;

            subjects.TryGetValue(subjectId, out var subject);

            rows.Add(new VersionCompareRow
            {
                SubjectId = subjectId,
                SortOrder = subject?.SortOrder ?? int.MaxValue,
                Category = subject?.Category,
                SubjectName = subject?.Name ?? $"科目ID {subjectId}",
                ExpenseType = expenseType,
                FromBudget = a?.Budget2027 ?? 0m,
                ToBudget = b?.Budget2027 ?? 0m,
                FromReason = a?.Reason,
                ToReason = b?.Reason
            });
        }

        var ordered = rows
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.ExpenseType)
            .ThenBy(r => r.SubjectName)
            .ToList();

        var changed = ordered.Where(r => r.IsChanged).ToList();
        vm.ChangedCount = changed.Select(r => r.SubjectId).Distinct().Count();
        vm.TotalIncrease = changed.Where(r => r.Delta > 0m).Sum(r => r.Delta);
        vm.TotalDecrease = changed.Where(r => r.Delta < 0m).Sum(r => -r.Delta);
        vm.Rows = vm.ShowAll ? ordered : changed;

        return View(vm);
    }

    private sealed class SnapLine
    {
        public int SubjectId { get; set; }
        public string ExpenseType { get; set; } = string.Empty;
        public decimal Budget2027 { get; set; }
        public string? Reason { get; set; }
    }

    private static Dictionary<string, SnapLine> ParseSnapshot(string? json,
        Dictionary<int, List<string>> expenseFallback)
    {
        var result = new Dictionary<string, SnapLine>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            var counters = new Dictionary<int, int>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("SubjectId", out var sidElement) ||
                    !sidElement.TryGetInt32(out var subjectId))
                    continue;

                var expenseType = string.Empty;
                if (item.TryGetProperty("ExpenseType", out var et) && et.ValueKind == JsonValueKind.String)
                    expenseType = et.GetString() ?? string.Empty;

                if (string.IsNullOrEmpty(expenseType))
                {
                    var seen = counters.TryGetValue(subjectId, out var c) ? c : 0;
                    counters[subjectId] = seen + 1;
                    if (expenseFallback.TryGetValue(subjectId, out var list) && seen < list.Count)
                        expenseType = list[seen];
                }

                var line = new SnapLine
                {
                    SubjectId = subjectId,
                    ExpenseType = expenseType,
                    Budget2027 = ReadDecimal(item, "Budget2027"),
                    Reason = item.TryGetProperty("Reason", out var r) && r.ValueKind == JsonValueKind.String
                        ? r.GetString()
                        : null
                };

                result[$"{subjectId}|{expenseType}"] = line;
            }
        }
        catch (JsonException)
        {
            // 快照损坏不阻断页面，按空快照处理
        }

        return result;
    }

    private static decimal ReadDecimal(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var element)) return 0m;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var value)) return value;
        if (element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(element.GetString(), out var parsed)) return parsed;
        return 0m;
    }
}
