using System.ComponentModel.DataAnnotations;

namespace GroupBudget.Models;

/// <summary>审核流水，只增不改；打回必须填写意见。</summary>
public class ReviewRecord
{
    public int Id { get; set; }

    public int FilingFormId { get; set; }

    [Required, MaxLength(100)]
    public string ReviewerName { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Decision { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Comments { get; set; }

    public int VersionNumber { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.Now;

    public FilingForm FilingForm { get; set; } = null!;
}
