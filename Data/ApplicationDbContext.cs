using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using GroupBudget.Models;

namespace GroupBudget.Data;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<BudgetBatch> BudgetBatches => Set<BudgetBatch>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<BaselineRecord> BaselineRecords => Set<BaselineRecord>();
    public DbSet<BaselineSnapshot> BaselineSnapshots => Set<BaselineSnapshot>();
    public DbSet<ActualDetail> ActualDetails => Set<ActualDetail>();
    public DbSet<SubjectDepartmentScope> SubjectDepartmentScopes => Set<SubjectDepartmentScope>();
    public DbSet<SubjectMapping> SubjectMappings => Set<SubjectMapping>();
    public DbSet<BudgetImportBatch> BudgetImportBatches => Set<BudgetImportBatch>();
    public DbSet<FilingForm> FilingForms => Set<FilingForm>();
    public DbSet<ReportLine> ReportLines => Set<ReportLine>();
    public DbSet<ReviewRecord> ReviewRecords => Set<ReviewRecord>();
    public DbSet<FormVersion> FormVersions => Set<FormVersion>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();
    public DbSet<UserOrganization> UserOrganizations => Set<UserOrganization>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // 同一部门同一批次只能有一张有效单据（数据库层兜底）
        builder.Entity<FilingForm>()
            .HasIndex(x => new { x.BudgetBatchId, x.OrganizationId })
            .IsUnique();

        builder.Entity<ReportLine>()
            .HasIndex(x => new { x.FilingFormId, x.SubjectId, x.ExpenseType })
            .IsUnique();
        // 为既有填报明细迁移回填默认费用属性，且新建行在数据库层也有默认值。
        builder.Entity<ReportLine>().Property(x => x.ExpenseType).HasDefaultValue("基本支出");

        builder.Entity<BaselineRecord>().Property(x => x.BudgetBaselineProject).HasDefaultValue(0m);
        builder.Entity<BaselineRecord>().Property(x => x.ActualBaselineProject).HasDefaultValue(0m);
        builder.Entity<BaselineSnapshot>().Property(x => x.BudgetBaselineProject).HasDefaultValue(0m);
        builder.Entity<BaselineSnapshot>().Property(x => x.ActualBaselineProject).HasDefaultValue(0m);

        builder.Entity<SubjectMapping>().HasIndex(x => x.AccountingSubjectName).IsUnique();
        builder.Entity<SubjectDepartmentScope>()
            .HasIndex(x => new { x.SubjectId, x.OrganizationId })
            .IsUnique();
        builder.Entity<ActualDetail>()
            .HasIndex(x => new { x.BudgetBatchId, x.OrganizationId, x.AccountingPeriod });
        builder.Entity<ActualDetail>()
            .HasIndex(x => new { x.BudgetBatchId, x.SubjectId, x.OrganizationId });
        builder.Entity<BudgetImportBatch>().HasIndex(x => x.ImportedAt);

        builder.Entity<BaselineRecord>()
            .HasIndex(x => new { x.BudgetBatchId, x.OrganizationId, x.SubjectId, x.Version })
            .IsUnique();

        builder.Entity<BaselineSnapshot>()
            .HasIndex(x => new { x.BudgetBatchId, x.Version, x.OrganizationId, x.SubjectId })
            .IsUnique();

        builder.Entity<Subject>().HasIndex(x => x.Code).IsUnique();
        builder.Entity<UserOrganization>().HasIndex(x => x.UserId).IsUnique();

        // 避免多重级联路径，历史数据只增不删
        builder.Entity<BaselineRecord>().HasOne(x => x.Subject).WithMany()
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BaselineSnapshot>().HasOne(x => x.Subject).WithMany()
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ReportLine>().HasOne(x => x.Subject).WithMany(x => x.ReportLines)
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SubjectMapping>().HasOne(x => x.Subject).WithMany(x => x.Mappings)
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SubjectDepartmentScope>().HasOne(x => x.Subject).WithMany(x => x.DepartmentScopes)
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SubjectDepartmentScope>().HasOne(x => x.Organization).WithMany(x => x.SubjectDepartmentScopes)
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ActualDetail>().HasOne(x => x.BudgetImportBatch).WithMany(x => x.ActualDetails)
            .HasForeignKey(x => x.BudgetImportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ActualDetail>().HasOne(x => x.BudgetBatch).WithMany(x => x.ActualDetails)
            .HasForeignKey(x => x.BudgetBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ActualDetail>().HasOne(x => x.Organization).WithMany(x => x.ActualDetails)
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ActualDetail>().HasOne(x => x.Subject).WithMany()
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
