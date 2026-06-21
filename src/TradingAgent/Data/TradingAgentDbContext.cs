using Microsoft.EntityFrameworkCore;
using TradingAgent.Models;

namespace TradingAgent.Data;

public sealed class TradingAgentDbContext(DbContextOptions<TradingAgentDbContext> options) : DbContext(options)
{
    public DbSet<TradingSignal> TradingSignals => Set<TradingSignal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TradingSignal>();

        entity.HasKey(signal => signal.Id);
        entity.Property(signal => signal.Symbol).HasMaxLength(32).IsRequired();
        entity.Property(signal => signal.OriginalSignal).HasMaxLength(16).IsRequired();
        entity.Property(signal => signal.ClaudeAction).HasMaxLength(16);
        entity.Property(signal => signal.RiskLevel).HasMaxLength(16);
        entity.Property(signal => signal.Timeframe).HasMaxLength(16);
        entity.Property(signal => signal.Strategy).HasMaxLength(64);
        entity.Property(signal => signal.ShortReason).HasMaxLength(1024);
        entity.Property(signal => signal.Notes).HasMaxLength(4000);
        entity.Property(signal => signal.RawPayload).IsRequired();
        entity.Property(signal => signal.Price).HasColumnType("decimal(18,4)");
        entity.Property(signal => signal.SuggestedStopLoss).HasColumnType("decimal(18,4)");
        entity.Property(signal => signal.SuggestedTakeProfit).HasColumnType("decimal(18,4)");
        entity.Property(signal => signal.ProfitLoss).HasColumnType("decimal(18,4)");
        entity.HasIndex(signal => signal.Symbol);
        entity.HasIndex(signal => signal.CreatedAtUtc);
    }
}
