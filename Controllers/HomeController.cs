using System.Diagnostics;
using GroupBudget.Data;
using GroupBudget.Models;
using GroupBudget.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GroupBudget.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<HomeController> _logger;

    public HomeController(ApplicationDbContext db, UserManager<IdentityUser> userManager, ILogger<HomeController> logger)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var vm = new HomeDashboardViewModel
        {
            IsAuthenticated = User.Identity?.IsAuthenticated == true,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsReviewer = User.IsInRole(AppRoles.Reviewer),
            IsFiller = User.IsInRole(AppRoles.Filler)
        };

        vm.RoleName = vm.IsAdmin ? AppRoles.Admin
            : vm.IsReviewer ? AppRoles.Reviewer
            : vm.IsFiller ? AppRoles.Filler
            : "访客";

        vm.DepartmentCount = await _db.Organizations.CountAsync();
        vm.SubjectCount = await _db.Subjects.CountAsync();
        vm.Batch = await _db.BudgetBatches.OrderByDescending(b => b.Id).FirstOrDefaultAsync();

        if (vm.Batch != null)
        {
            var forms = await _db.FilingForms
                .Where(f => f.BudgetBatchId == vm.Batch.Id)
                .Select(f => new { f.OrganizationId, f.Status })
                .ToListAsync();

            vm.FormTotal = forms.Count;
            vm.FormDraft = forms.Count(f => f.Status == FormStatus.Draft);
            vm.FormSubmitted = forms.Count(f => f.Status == FormStatus.Submitted);
            vm.FormApproved = forms.Count(f => f.Status == FormStatus.Approved);
            vm.FormReturned = forms.Count(f => f.Status == FormStatus.Returned);
            vm.PendingCount = vm.FormSubmitted;

            // SQLite 下 decimal 存 TEXT，SumAsync 无法翻译，先取回内存再求和
            var planned = await _db.ReportLines
                .Where(l => l.FilingForm!.BudgetBatchId == vm.Batch.Id && l.Budget2027 != 0m)
                .Select(l => l.Budget2027)
                .ToListAsync();
            vm.PlannedTotal = planned.Sum();

            if (vm.IsFiller && vm.IsAuthenticated)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    var orgId = await _db.UserOrganizations
                        .Where(u => u.UserId == user.Id)
                        .Select(u => (int?)u.OrganizationId)
                        .FirstOrDefaultAsync();

                    if (orgId != null)
                    {
                        vm.OrganizationName = await _db.Organizations
                            .Where(o => o.Id == orgId)
                            .Select(o => o.Name)
                            .FirstOrDefaultAsync();

                        vm.MyFormStatus = forms
                            .Where(f => f.OrganizationId == orgId.Value)
                            .Select(f => f.Status)
                            .FirstOrDefault();
                    }
                }
            }
        }

        return View(vm);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
