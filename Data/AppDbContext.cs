using Microsoft.EntityFrameworkCore;
using WorkReport.Data.Models;

namespace WorkReport.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<User> Users => Set<User>();
    public DbSet<DocumentFile> Documents => Set<DocumentFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkTask>(entity =>
        {
            entity.ToTable("Tasks");
            entity.HasIndex(t => t.Period);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<DocumentFile>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasIndex(d => d.Category);
            entity.HasIndex(d => d.IsDeleted);
            entity.HasIndex(d => d.CreatedAt);
            entity.HasIndex(d => d.TaskId);
            entity.HasOne(d => d.Task).WithMany().HasForeignKey(d => d.TaskId).IsRequired(false);
        });
    }
}
