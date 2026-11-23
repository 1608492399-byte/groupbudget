using System.ComponentModel.DataAnnotations;

namespace GroupBudget.Models;

/// <summary>填报单位（部门）。</summary>
public class Organization
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ContactPerson { get; set; }

    [MaxLength(30)]
    public string? ContactPhone { get; set; }

    public ICollection<FilingForm> FilingForms { get; set; } = new List<FilingForm>();
    public ICollection<BaselineRecord> BaselineRecords { get; set; } = new List<BaselineRecord>();
    public ICollection<BaselineSnapshot> BaselineSnapshots { get; set; } = new List<BaselineSnapshot>();
    public ICollection<UserOrganization> UserOrganizations { get; set; } = new List<UserOrganization>();
    public ICollection<SubjectDepartmentScope> SubjectDepartmentScopes { get; set; } = new List<SubjectDepartmentScope>();
    public ICollection<ActualDetail> ActualDetails { get; set; } = new List<ActualDetail>();
}
