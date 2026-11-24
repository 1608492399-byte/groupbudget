using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GroupBudget.Areas.Identity.Pages.Account;

/// <summary>
/// 自定义登录页：使用品牌化布局 + 演示账号一键填充。
/// 业务逻辑与默认模板保持一致（密码登录、锁定策略、两步验证跳转）。
/// </summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(SignInManager<IdentityUser> signInManager, ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "请输入登录邮箱")]
        [EmailAddress(ErrorMessage = "邮箱格式不正确")]
        [Display(Name = "登录邮箱")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "请输入密码")]
        [DataType(DataType.Password)]
        [Display(Name = "密码")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "保持登录")]
        public bool RememberMe { get; set; }
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            Input.Email.Trim(), Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("用户 {Email} 登录成功。", Input.Email);
            return Url.IsLocalUrl(ReturnUrl)
                ? LocalRedirect(ReturnUrl)
                : RedirectToPage("/Index", new { area = "" });
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl, Input.RememberMe });
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("用户 {Email} 因多次失败被锁定。", Input.Email);
            ModelState.AddModelError(string.Empty, "连续登录失败次数过多，账号已临时锁定，请稍后再试或联系系统管理员。");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "邮箱或密码不正确，请重新输入。");
        _logger.LogWarning("登录失败：{Email}，结果 {Result}", Input.Email, result.ToString());
        return Page();
    }
}
