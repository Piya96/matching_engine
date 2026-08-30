using MatchingEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MatchingEngine.Data;

public class MatchingEngineDbContext(DbContextOptions<MatchingEngineDbContext> options)
    : DbContext(options)
{
    public DbSet<Entity> Entities => Set<Entity>();
    public DbSet<EntityAttribute> EntityAttributes => Set<EntityAttribute>();
    public DbSet<CriteriaSet> CriteriaSets => Set<CriteriaSet>();
    public DbSet<Criterion> Criteria => Set<Criterion>();
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();
    public DbSet<MatchResultDetail> MatchResultDetails => Set<MatchResultDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every table's shape lives in its own IEntityTypeConfiguration next
        // to this file -- see Configurations/. Keeping it out of this method
        // is what makes each table's indexes and conversions reviewable
        // (and testable in isolation) instead of one growing switch here.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MatchingEngineDbContext).Assembly);
    }
}
