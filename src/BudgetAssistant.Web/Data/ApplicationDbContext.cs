using BudgetAssistant.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace BudgetAssistant.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public const int EmbeddingDimensions = 384;   // bge-micro-v2

    /// <summary>
    /// Identity's schema version decides which tables its model contains — v3 adds
    /// passkeys — so it is part of the EF model and has to match the migration snapshot.
    /// Declared here rather than inline at the call site because anything that builds this
    /// context outside the app (the integration tests) has to configure the same value or
    /// it builds a different model and every migration looks pending.
    /// </summary>
    public static readonly Version IdentitySchemaVersion = IdentitySchemaVersions.Version3;

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<MerchantExample> MerchantExamples => Set<MerchantExample>();

    /// <summary>Keeps Core entities on plain float[] while the column stays a real
    /// pgvector type. Core therefore has no dependency on Npgsql or Pgvector.</summary>
    // Declared over float[]? because both mapped properties are nullable. EF short-circuits
    // null before the converter runs, so the conversion body never sees one.
    static readonly ValueConverter<float[]?, Vector> VectorConverter =
        new(v => new Vector(v!), v => v.ToArray());

    /// <summary>Without this EF compares float[] by reference, so a recomputed embedding
    /// of equal length would not be detected as a change.</summary>
    static readonly ValueComparer<float[]> VectorComparer =
        new((a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (h, f) => HashCode.Combine(h, f.GetHashCode())),
            v => v.ToArray());

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasPostgresExtension("vector");

        b.Entity<Account>(e =>
        {
            // decimal(18,2): money must never round through binary floating point.
            e.Property(x => x.Balance).HasColumnType("decimal(18,2)");
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Color).HasMaxLength(9);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<MerchantExample>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(200).IsRequired();
            e.Property(x => x.Embedding)
                .HasColumnType($"vector({EmbeddingDimensions})")
                .HasConversion(VectorConverter, VectorComparer);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.RawDescription).HasMaxLength(300).IsRequired();
            e.Property(x => x.NormalizedMerchant).HasMaxLength(300).IsRequired();
            e.Property(x => x.Embedding)
                .HasColumnType($"vector({EmbeddingDimensions})")
                .HasConversion(VectorConverter, VectorComparer);

            e.HasOne(x => x.Account).WithMany(a => a.Transactions).HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
            // Self-reference: a duplicate points at the original charge.
            e.HasOne<Transaction>().WithMany().HasForeignKey(x => x.DuplicateOfId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.Date);
            e.HasIndex(x => new { x.AccountId, x.Date });
        });
    }
}
