using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

/// <summary>管理员：维护批次、录入并下发本年基准数据（部门只读）。</summary>
[Authorize(Roles = AppRoles.Admin)]
public class AdminController : AppControllerBase
{
    public AdminController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        : base(db, userManager) { }

    public async Task<IActionResult> Index()
    {
        var batches = await Db.BudgetBatches
            .OrderByDescending(x => x.Id)
            .Select(x => new BatchListItem
            {
                Id = x.Id,
                Name = x.Name,
                PlanYear = x.PlanYear,
                BaselineYear = x.BaselineYear,
                Status = x.Status,
                CurrentBaselineVersion = x.CurrentBaselineVersion,
                DeviationThreshold = x.DeviationThreshold,
                PublishedAt = x.PublishedAt,
                FormCount = Db.FilingForms.Count(f => f.BudgetBatchId == x.Id),
                SubmittedCount = Db.FilingForms.Count(f => f.BudgetBatchId == x.Id && f.Status == FormStatus.Submitted)
            })
            .ToListAsync();

        ViewBag.Organizations = await Db.Organizations.OrderBy(x => x.Id).ToListAsync();
        ViewBag.SubjectCount = await Db.Subjects.CountAsync();
        ViewBag.TotalForms = await Db.FilingForms.CountAsync();
        return View(batches);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBatch(string name, int planYear, decimal deviationThreshold)
    {
        if (string.IsNullOrWhiteSpace(name) || planYear < 2000 || planYear > 2100 ||
            deviationThreshold < 0 || deviationThreshold > 1)
        {
            TempData["Error"] = "批次名称、编制年度或偏差阈值不合法（阈值请填 0~1，例如 0.2 表示 20%）。";
            return RedirectToAction(nameof(Index));
        }

        var exists = await Db.BudgetBatches.AnyAsync(x => x.PlanYear == planYear);
        if (exists)
        {
            TempData["Error"] = $"已存在 {planYear} 年度的批次，一年只报一次，不能重复创建。";
            return RedirectToAction(nameof(Index));
        }

        var batch = new BudgetBatch
        {
            Name = name.Trim(),
            PlanYear = planYear,
            BaselineYear = planYear - 1,
            DeviationThreshold = deviationThreshold,
            Status = BatchStatus.Draft,
            CurrentBaselineVersion = 1
        };
        Db.BudgetBatches.Add(batch);
        WriteLog("新建批次", batch.Name, $"编制 {planYear} 年预算，基准年 {planYear - 1}");
        await Db.SaveChangesAsync();

        // 新批次按科目生成一份全 0 的基准表，供管理员逐部门填写
        var orgs = await Db.Organizations.OrderBy(x => x.Id).ToListAsync();
        var subjects = await Db.Subjects.OrderBy(x => x.SortOrder).ToListAsync();
        foreach (var org in orgs)
        foreach (var subject in subjects)
        {
            Db.BaselineRecords.Add(new BaselineRecord
            {
                BudgetBatch = batch,
                OrganizationId = org.Id,
                SubjectId = subject.Id,
                Version = 1,
                UpdatedBy = User.Identity?.Name
            });
        }
        await Db.SaveChangesAsync();

        TempData["Message"] = "批次已创建，请先录入各部门 2026 基准数据，再点「下发」。";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>按部门录入 / 查看 2026 预算数与决算数。</summary>
    [HttpGet]
    public async Task<IActionResult> Baseline(int batchId, int? orgId)
    {
        var batch = await Db.BudgetBatches.FindAsync(batchId);
        if (batch is null) return NotFound();

        var orgs = await Db.Organizations.OrderBy(x => x.Id).ToListAsync();
        var org = orgs.FirstOrDefault(x => x.Id == orgId) ?? orgs.FirstOrDefault();
        if (org is null) return NotFound();

        var allSubjects = await Db.Subjects.OrderBy(x => x.SortOrder).ToListAsync();
        var scopes = await Db.SubjectDepartmentScopes.AsNoTracking().ToListAsync();
        var configuredSubjectIds = scopes.Select(x => x.SubjectId).ToHashSet();
        var allowedSubjectIds = scopes.Where(x => x.OrganizationId == org.Id)
            .Select(x => x.SubjectId).ToHashSet();
        // 与填报页口径一致：只列本部门适用的科目；未配置范围的科目视为全部门适用
        var subjects = allSubjects
            .Where(s => !configuredSubjectIds.Contains(s.Id) || allowedSubjectIds.Contains(s.Id))
            .ToList();

        var records = await Db.BaselineRecords
            .Where(x => x.BudgetBatchId == batchId && x.OrganizationId == org.Id &&
                        x.Version == batch.CurrentBaselineVersion)
            .ToListAsync();

        var vm = new BaselineEditViewModel
        {
            BatchId = batch.Id,
            BatchName = batch.Name,
            BaselineYear = batch.BaselineYear,
            BatchStatus = batch.Status,
            CurrentVersion = batch.CurrentBaselineVersion,
            OrganizationId = org.Id,
            OrganizationName = org.Name,
            Rows = subjects.Select(s =>
            {
                var r = records.FirstOrDefault(x => x.SubjectId == s.Id);
                return new BaselineInputRow
                {
                    SubjectId = s.Id,
                    SubjectName = s.Name,
                    Category = s.Category,
                    BudgetBaseline = r?.BudgetBaseline ?? 0m,
                    ActualBaseline = r?.ActualBaseline ?? 0m,
                    BudgetBaselineProject = r?.BudgetBaselineProject ?? 0m,
                    ActualBaselineProject = r?.ActualBaselineProject ?? 0m
                };
            }).ToList()
        };

        ViewBag.Organizations = orgs;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBaseline(BaselineEditViewModel vm)
    {
        var batch = await Db.BudgetBatches.FindAsync(vm.BatchId);
        if (batch is null) return NotFound();

        if (batch.Status == BatchStatus.Closed || batch.Status == BatchStatus.Archived)
        {
            TempData["Error"] = "批次已截止或已归档，不能再修改基准数据。";
            return RedirectToAction(nameof(Baseline), new { batchId = batch.Id, orgId = vm.OrganizationId });
        }

        foreach (var row in vm.Rows)
        {
            if (!BudgetCalculator.IsValidAmount(row.BudgetBaseline) ||
                !BudgetCalculator.IsValidAmount(row.ActualBaseline) ||
                !BudgetCalculator.IsValidAmount(row.BudgetBaselineProject) ||
                !BudgetCalculator.IsValidAmount(row.ActualBaselineProject))
            {
                TempData["Error"] = $"「{row.SubjectName}」金额必须 ≥ 0 且最多两位小数。";
                return RedirectToAction(nameof(Baseline), new { batchId = batch.Id, orgId = vm.OrganizationId });
            }
        }

        // 已下发后修改基准：生成新版本并留痕，不覆盖历史版本；需重新下发才影响部门
        var creatingNewVersion = batch.Status == BatchStatus.Published;
        var targetVersion = creatingNewVersion
            ? batch.CurrentBaselineVersion + 1
            : batch.CurrentBaselineVersion;

        var currentRows = await Db.BaselineRecords
            .Where(x => x.BudgetBatchId == batch.Id && x.OrganizationId == vm.OrganizationId &&
                        x.Version == batch.CurrentBaselineVersion)
            .ToListAsync();

        if (creatingNewVersion)
        {
            foreach (var r in currentRows)
            {
                Db.BaselineRecords.Add(new BaselineRecord
                {
                    BudgetBatchId = batch.Id,
                    OrganizationId = r.OrganizationId,
                    SubjectId = r.SubjectId,
                    BudgetBaseline = r.BudgetBaseline,
                    ActualBaseline = r.ActualBaseline,
                    BudgetBaselineProject = r.BudgetBaselineProject,
                    ActualBaselineProject = r.ActualBaselineProject,
                    Version = targetVersion,
                    IsLocked = false,
                    UpdatedAt = DateTime.Now,
                    UpdatedBy = User.Identity?.Name
                });
            }
            await Db.SaveChangesAsync();
            batch.CurrentBaselineVersion = targetVersion;
        }

        var targetRows = await Db.BaselineRecords
            .Where(x => x.BudgetBatchId == batch.Id && x.OrganizationId == vm.OrganizationId &&
                        x.Version == targetVersion)
            .ToListAsync();

        foreach (var row in vm.Rows)
        {
            var target = targetRows.FirstOrDefault(x => x.SubjectId == row.SubjectId);
            if (target is null) continue;
            target.BudgetBaseline = row.BudgetBaseline;
            target.ActualBaseline = row.ActualBaseline;
            target.BudgetBaselineProject = row.BudgetBaselineProject;
            target.ActualBaselineProject = row.ActualBaselineProject;
            target.IsLocked = false;
            target.UpdatedAt = DateTime.Now;
            target.UpdatedBy = User.Identity?.Name;
        }

        WriteLog(creatingNewVersion ? "修改基准(新版本)" : "录入基准",
            $"{batch.Name} / 部门 {vm.OrganizationId}", $"版本 V{targetVersion}，共 {vm.Rows.Count} 个科目");
        await Db.SaveChangesAsync();

        TempData["Message"] = creatingNewVersion
            ? $"已生成基准版本 V{targetVersion} 并留痕。原版本和已提交单据不受影响，请点「下发」使其生效。"
            : "基准数据已保存，请点「下发」后部门才能看到。";
        return RedirectToAction(nameof(Baseline), new { batchId = batch.Id, orgId = vm.OrganizationId });
    }

    [HttpGet]
    public async Task<IActionResult> SubjectScopes(int? batchId)
    {
        var batches = await Db.BudgetBatches.OrderByDescending(x => x.PlanYear).ToListAsync();
        var selectedBatchId = batchId ?? batches.FirstOrDefault()?.Id ?? 0;
        return View("SubjectScopes", await BuildSubjectScopePageAsync(selectedBatchId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSubjectScopes(SubjectDepartmentScopePageViewModel vm)
    {
        var subjects = await Db.Subjects.OrderBy(x => x.SortOrder).ToListAsync();
        var organizations = await Db.Organizations.OrderBy(x => x.Id).ToListAsync();
        var batch = await Db.BudgetBatches.FindAsync(vm.SelectedBatchId);
        if (batch is null || subjects.Count == 0 || organizations.Count == 0)
            return BadRequest();

        var rowsBySubject = vm.Rows.GroupBy(x => x.SubjectId).ToDictionary(x => x.Key, x => x.First());
        if (rowsBySubject.Count != subjects.Count || subjects.Any(x => !rowsBySubject.ContainsKey(x.Id)))
            return BadRequest("提交的科目范围数据不完整。");

        var organizationIds = organizations.Select(x => x.Id).ToHashSet();
        var selections = new Dictionary<int, HashSet<int>>();
        foreach (var subject in subjects)
        {
            var selected = rowsBySubject[subject.Id].OrganizationIds.Distinct().ToHashSet();
            if (selected.Any(x => !organizationIds.Contains(x))) return BadRequest();
            if (selected.Count == 0) selected = organizationIds;
            selections[subject.Id] = selected;
        }

        await using var transaction = await Db.Database.BeginTransactionAsync();
        var existingScopes = await Db.SubjectDepartmentScopes.ToListAsync();
        Db.SubjectDepartmentScopes.RemoveRange(existingScopes);
        await Db.SaveChangesAsync();
        foreach (var pair in selections)
        foreach (var organizationId in pair.Value)
        {
            Db.SubjectDepartmentScopes.Add(new SubjectDepartmentScope
            {
                SubjectId = pair.Key,
                OrganizationId = organizationId
            });
        }
        await Db.SaveChangesAsync();

        var lines = await Db.ReportLines
            .Include(x => x.FilingForm).ThenInclude(x => x.BudgetBatch)
            .Include(x => x.FilingForm).ThenInclude(x => x.Organization)
            .Include(x => x.Subject)
            .Where(x => x.FilingForm.BudgetBatchId == batch.Id)
            .ToListAsync();
        var removedZeroLines = 0;
        var retainedIssues = 0;
        foreach (var line in lines)
        {
            if (selections[line.SubjectId].Contains(line.FilingForm.OrganizationId)) continue;

            var allAmountsZero = line.Budget2026 == 0m && line.Actual2026 == 0m && line.Budget2027 == 0m;
            var hasReason = !string.IsNullOrWhiteSpace(line.Reason);
            var isLocked = line.FilingForm.Status == FormStatus.Submitted ||
                           line.FilingForm.Status == FormStatus.Approved;
            if (allAmountsZero && !hasReason && !isLocked)
            {
                Db.ReportLines.Remove(line);
                removedZeroLines++;
            }
            else
            {
                retainedIssues++;
            }
        }

        WriteLog("保存科目部门范围", batch.Name,
            $"已保存 {selections.Sum(x => x.Value.Count)} 条范围；删除零金额明细 {removedZeroLines} 条；保留待处理明细 {retainedIssues} 条");
        await Db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Message"] = $"范围已保存；已删除 {removedZeroLines} 条可清理的零金额明细，另有 {retainedIssues} 条不适用明细保留待人工处理。";
        return RedirectToAction(nameof(SubjectScopes), new { batchId = batch.Id });
    }

    private async Task<SubjectDepartmentScopePageViewModel> BuildSubjectScopePageAsync(int batchId)
    {
        var batches = await Db.BudgetBatches.OrderByDescending(x => x.PlanYear).ToListAsync();
        var organizations = await Db.Organizations.OrderBy(x => x.Id).ToListAsync();
        var subjects = await Db.Subjects.OrderBy(x => x.SortOrder).ToListAsync();
        var scopes = await Db.SubjectDepartmentScopes.AsNoTracking().ToListAsync();
        var scopeGroups = scopes.GroupBy(x => x.SubjectId).ToDictionary(x => x.Key, x => x.Select(s => s.OrganizationId).ToList());
        var allOrganizationIds = organizations.Select(x => x.Id).ToList();

        var rows = subjects.Select(subject => new SubjectScopeRowViewModel
        {
            SubjectId = subject.Id,
            SubjectName = subject.Name,
            Category = subject.Category,
            OrganizationIds = scopeGroups.TryGetValue(subject.Id, out var selected)
                ? selected
                : allOrganizationIds.ToList()
        }).ToList();

        var manualIssues = new List<SubjectScopeIssueViewModel>();
        var selectedBatch = batches.FirstOrDefault(x => x.Id == batchId);
        if (selectedBatch is not null)
        {
            var lines = await Db.ReportLines.AsNoTracking()
                .Include(x => x.FilingForm).ThenInclude(x => x.BudgetBatch)
                .Include(x => x.FilingForm).ThenInclude(x => x.Organization)
                .Include(x => x.Subject)
                .Where(x => x.FilingForm.BudgetBatchId == selectedBatch.Id)
                .ToListAsync();

            foreach (var line in lines)
            {
                var allowed = !scopeGroups.TryGetValue(line.SubjectId, out var selectedOrganizationIds) ||
                              selectedOrganizationIds.Contains(line.FilingForm.OrganizationId);
                if (allowed) continue;

                var allAmountsZero = line.Budget2026 == 0m && line.Actual2026 == 0m && line.Budget2027 == 0m;
                var hasReason = !string.IsNullOrWhiteSpace(line.Reason);
                var isLocked = line.FilingForm.Status == FormStatus.Submitted ||
                               line.FilingForm.Status == FormStatus.Approved;
                if (allAmountsZero && !hasReason && !isLocked) continue;

                manualIssues.Add(new SubjectScopeIssueViewModel
                {
                    BatchName = line.FilingForm.BudgetBatch.Name,
                    FormNumber = line.FilingForm.FormNumber,
                    OrganizationName = line.FilingForm.Organization.Name,
                    SubjectName = line.Subject.Name,
                    ExpenseType = line.ExpenseType,
                    Status = line.FilingForm.Status,
                    Budget2026 = line.Budget2026,
                    Actual2026 = line.Actual2026,
                    Budget2027 = line.Budget2027,
                    Reason = line.Reason ?? string.Empty,
                    Explanation = isLocked
                        ? "单据已提交或审核通过，未自动删除，请人工核对。"
                        : hasReason
                            ? "金额为零但存在原因说明，未自动删除，请人工核对。"
                            : "金额不为零，未自动删除，请人工处理。"
                });
            }
        }

        return new SubjectDepartmentScopePageViewModel
        {
            Batches = batches,
            Organizations = organizations,
            Rows = rows,
            ManualIssues = manualIssues,
            SelectedBatchId = selectedBatch?.Id ?? batches.FirstOrDefault()?.Id ?? 0,
            Message = TempData["Message"] as string
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RepairHistoricalLines()
    {
        var scopes = await Db.SubjectDepartmentScopes.AsNoTracking().ToListAsync();
        var scopesBySubject = scopes.GroupBy(x => x.SubjectId)
            .ToDictionary(x => x.Key, x => x.Select(scope => scope.OrganizationId).ToHashSet());
        var subjects = await Db.Subjects.OrderBy(x => x.SortOrder).ToListAsync();
        var forms = await Db.FilingForms
            .Include(x => x.BudgetBatch)
            .Include(x => x.Lines).ThenInclude(x => x.Subject)
            .Where(x => x.Status == FormStatus.Draft || x.Status == FormStatus.Returned)
            .ToListAsync();

        var repairedForms = 0;
        var deletedLines = 0;
        var addedLines = 0;

        foreach (var form in forms)
        {
            var changed = false;
            foreach (var line in form.Lines.ToList())
            {
                var applicable = !scopesBySubject.TryGetValue(line.SubjectId, out var allowedOrganizations) ||
                                 allowedOrganizations.Contains(form.OrganizationId);
                if (applicable) continue;

                if (line.Budget2027 != 0m)
                {
                    WriteLog("修正历史明细-删除不适用科目", form.FormNumber,
                        $"科目「{line.Subject.Name}」，清零前 2027 预算 {line.Budget2027.ToString("F2")}。");
                }
                Db.ReportLines.Remove(line);
                deletedLines++;
                changed = true;
            }

            foreach (var subject in subjects)
            {
                var applicable = !scopesBySubject.TryGetValue(subject.Id, out var allowedOrganizations) ||
                                 allowedOrganizations.Contains(form.OrganizationId);
                if (!applicable || form.Lines.Any(x => x.SubjectId == subject.Id && x.ExpenseType == "项目支出"))
                    continue;

                var projectLine = new ReportLine
                {
                    FilingFormId = form.Id,
                    SubjectId = subject.Id,
                    ExpenseType = "项目支出",
                    Budget2026 = 0m,
                    Actual2026 = 0m,
                    Budget2027 = 0m,
                    BaselineVersion = form.BudgetBatch.CurrentBaselineVersion
                };
                Db.ReportLines.Add(projectLine);
                form.Lines.Add(projectLine);
                addedLines++;
                changed = true;
            }

            if (changed) repairedForms++;
        }

        var lockedForms = await Db.FilingForms.CountAsync(x =>
            x.Status == FormStatus.Submitted || x.Status == FormStatus.Approved);
        WriteLog("修正历史明细", "草稿及已打回单据",
            $"修正单据 {repairedForms} 张，删除明细 {deletedLines} 行，新增项目支出行 {addedLines} 行；已提交/审核通过 {lockedForms} 张未修改。");
        await Db.SaveChangesAsync();

        TempData["Message"] = $"历史明细修正完成：修正单据 {repairedForms} 张，删除行 {deletedLines} 条，新增项目支出行 {addedLines} 条；已提交/审核通过单据 {lockedForms} 张未修改。";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>下发：冻结基准快照、开放填报、为尚无单据的部门生成草稿。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int batchId)
    {
        var batch = await Db.BudgetBatches.FindAsync(batchId);
        if (batch is null) return NotFound();
        if (batch.Status == BatchStatus.Archived)
        {
            TempData["Error"] = "已归档批次不能下发。";
            return RedirectToAction(nameof(Index));
        }

        var version = batch.CurrentBaselineVersion;
        var baselines = await Db.BaselineRecords
            .Where(x => x.BudgetBatchId == batchId && x.Version == version)
            .ToListAsync();

        if (baselines.Count == 0)
        {
            TempData["Error"] = "该批次还没有基准数据，请先录入。";
            return RedirectToAction(nameof(Index));
        }

        // 同一版本重复下发时覆盖旧快照
        var oldSnapshots = await Db.BaselineSnapshots
            .Where(x => x.BudgetBatchId == batchId && x.Version == version)
            .ToListAsync();
        Db.BaselineSnapshots.RemoveRange(oldSnapshots);

        foreach (var b in baselines)
        {
            b.IsLocked = true;
            Db.BaselineSnapshots.Add(new BaselineSnapshot
            {
                BudgetBatchId = batchId,
                Version = version,
                OrganizationId = b.OrganizationId,
                SubjectId = b.SubjectId,
                BudgetBaseline = b.BudgetBaseline,
                ActualBaseline = b.ActualBaseline,
                BudgetBaselineProject = b.BudgetBaselineProject,
                ActualBaselineProject = b.ActualBaselineProject,
                Operator = User.Identity?.Name
            });
        }

        batch.Status = BatchStatus.Published;
        batch.PublishedAt = DateTime.Now;
        batch.PublishedBy = User.Identity?.Name;

        // 只给还没有单据的部门初始化；已有单据保持不变，避免影响已提交数据
        var existingOrgIds = await Db.FilingForms
            .Where(x => x.BudgetBatchId == batchId)
            .Select(x => x.OrganizationId)
            .ToListAsync();
        var orgs = await Db.Organizations.OrderBy(x => x.Id).ToListAsync();

        foreach (var org in orgs.Where(o => !existingOrgIds.Contains(o.Id)))
        {
            var form = new FilingForm
            {
                FormNumber = $"BG{batch.PlanYear}-{org.Code}",
                BudgetBatchId = batchId,
                OrganizationId = org.Id,
                Status = FormStatus.Draft,
                VersionNumber = 1
            };
            Db.FilingForms.Add(form);
            await Db.SaveChangesAsync();

            foreach (var b in baselines.Where(x => x.OrganizationId == org.Id))
            {
                Db.ReportLines.Add(new ReportLine
                {
                    FilingFormId = form.Id,
                    SubjectId = b.SubjectId,
                    ExpenseType = "基本支出",
                    Budget2026 = b.BudgetBaseline,
                    Actual2026 = b.ActualBaseline,
                    Budget2027 = b.ActualBaseline,
                    BaselineVersion = version
                });
                Db.ReportLines.Add(new ReportLine
                {
                    FilingFormId = form.Id,
                    SubjectId = b.SubjectId,
                    ExpenseType = "项目支出",
                    Budget2026 = b.BudgetBaselineProject,
                    Actual2026 = b.ActualBaselineProject,
                    Budget2027 = b.ActualBaselineProject,
                    BaselineVersion = version
                });
            }
            await Db.SaveChangesAsync();
        }

        WriteLog("下发批次", batch.Name, $"基准版本 V{version}，快照 {baselines.Count} 行");
        await Db.SaveChangesAsync();

        TempData["Message"] = $"已下发基准版本 V{version}，部门现在可以填报 {batch.PlanYear} 年预算。";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int batchId, string status)
    {
        var batch = await Db.BudgetBatches.FindAsync(batchId);
        if (batch is null) return NotFound();
        if (status != BatchStatus.Closed && status != BatchStatus.Archived)
            return BadRequest();

        batch.Status = status;
        WriteLog("变更批次状态", batch.Name, status);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"批次状态已改为「{status}」。";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>管理所有账号的部门绑定，账号本身仍由 Identity 管理。</summary>
    public async Task<IActionResult> OrganizationBindings()
    {
        var accounts = await UserManager.Users.OrderBy(x => x.Email).ToListAsync();
        var organizations = await Db.Organizations.OrderBy(x => x.Name).ToListAsync();
        var bindings = await Db.UserOrganizations
            .ToDictionaryAsync(x => x.UserId, x => (int?)x.OrganizationId);
        var rolesByUser = new Dictionary<string, string>();

        foreach (var account in accounts)
        {
            var roles = await UserManager.GetRolesAsync(account);
            rolesByUser[account.Id] = roles.Count == 0 ? "未分配角色" : string.Join("、", roles);
        }

        ViewBag.Organizations = organizations;
        ViewBag.Bindings = bindings;
        ViewBag.RolesByUser = rolesByUser;
        return View("OrganizationBindings", accounts);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveOrganizationBinding(string userId, int? organizationId)
    {
        var account = await UserManager.FindByIdAsync(userId);
        if (account is null) return NotFound();

        Organization? newOrganization = null;
        if (organizationId.HasValue)
        {
            newOrganization = await Db.Organizations.FindAsync(organizationId.Value);
            if (newOrganization is null) return BadRequest();
        }

        var binding = await Db.UserOrganizations
            .SingleOrDefaultAsync(x => x.UserId == account.Id);
        var oldOrganizationName = binding is null
            ? "未绑定"
            : (await Db.Organizations.FindAsync(binding.OrganizationId))?.Name ?? "未知部门";

        // 更新现有关系或创建/删除关系，保持每个账号至多一条绑定记录。
        if (newOrganization is null)
        {
            if (binding is not null) Db.UserOrganizations.Remove(binding);
        }
        else if (binding is null)
        {
            Db.UserOrganizations.Add(new UserOrganization
            {
                UserId = account.Id,
                OrganizationId = newOrganization.Id
            });
        }
        else
        {
            binding.OrganizationId = newOrganization.Id;
        }

        var newOrganizationName = newOrganization?.Name ?? "未绑定";
        WriteLog("账号部门绑定", account.Email ?? account.UserName,
            $"账号 {account.Email ?? account.UserName}：部门「{oldOrganizationName}」→部门「{newOrganizationName}」");
        await Db.SaveChangesAsync();

        TempData["Message"] = $"账号 {account.Email ?? account.UserName} 的部门绑定已保存。";
        return RedirectToAction(nameof(OrganizationBindings));
    }

    /// <summary>演示账号清单，方便直接用三种角色登录验证权限。</summary>
    public async Task<IActionResult> Accounts()
    {
        var demoEmails = new[] { "admin@group.local", "reviewer@group.local", "filler@group.local" };
        var accounts = await UserManager.Users
            .Where(x => x.Email != null && demoEmails.Contains(x.Email))
            .ToListAsync();
        var userIds = accounts.Select(x => x.Id).ToList();
        var bindings = await Db.UserOrganizations
            .Include(x => x.Organization)
            .Where(x => userIds.Contains(x.UserId))
            .ToListAsync();
        var bindingsByEmail = accounts.ToDictionary(
            x => x.Email!,
            x => bindings.FirstOrDefault(b => b.UserId == x.Id)?.Organization.Name ?? "未绑定",
            StringComparer.OrdinalIgnoreCase);

        ViewBag.DemoBindings = bindingsByEmail;
        return View();
    }
}
