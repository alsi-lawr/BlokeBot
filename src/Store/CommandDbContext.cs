namespace CommandBot.Store;

using Microsoft.EntityFrameworkCore;

public sealed class CounterDbContext : DbContext
{
    public CounterDbContext(DbContextOptions<CounterDbContext> options)
        : base(options) { }

    public DbSet<CounterRow> Counters => Set<CounterRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CounterRow>(b =>
        {
            b.ToTable("counters");
            b.HasKey(x => x.Key);
            b.Property(x => x.Key).HasColumnName("key");
            b.Property(x => x.Value).HasColumnName("value");
        });
    }
}

public sealed class CounterRow
{
    public required string Key { get; set; }
    public int Value { get; set; }
}
