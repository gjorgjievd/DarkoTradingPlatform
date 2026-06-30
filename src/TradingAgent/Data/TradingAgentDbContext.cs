using Microsoft.EntityFrameworkCore;
using TradingAgent.Models;

namespace TradingAgent.Data;

public sealed class TradingAgentDbContext(DbContextOptions<TradingAgentDbContext> options) : DbContext(options)
{
    public DbSet<TradingSignal> TradingSignals => Set<TradingSignal>();
    public DbSet<SignalMarketData> SignalMarketData => Set<SignalMarketData>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<WebhookRequestLog> WebhookRequestLogs => Set<WebhookRequestLog>();
    public DbSet<RuntimeSetting> RuntimeSettings => Set<RuntimeSetting>();

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
        entity.Property(signal => signal.ReasonCategories).HasMaxLength(256);
        entity.Property(signal => signal.Notes).HasMaxLength(4000);
        entity.Property(signal => signal.Source).HasMaxLength(32);
        entity.Property(signal => signal.RawPayload).IsRequired();
        entity.Property(signal => signal.Price).HasColumnType("decimal(18,4)");
        entity.Property(signal => signal.SuggestedStopLoss).HasColumnType("decimal(18,4)");
        entity.Property(signal => signal.SuggestedTakeProfit).HasColumnType("decimal(18,4)");
        entity.Property(signal => signal.RiskRewardRatio).HasColumnType("decimal(18,4)");
        entity.Property(signal => signal.PositionSizePercent).HasColumnType("decimal(18,4)");
        entity.Property(signal => signal.ProfitLoss).HasColumnType("decimal(18,4)");
        entity.HasIndex(signal => signal.Symbol);
        entity.HasIndex(signal => signal.CreatedAtUtc);

        var marketData = modelBuilder.Entity<SignalMarketData>();
        marketData.HasKey(item => item.Id);
        marketData.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
        marketData.Property(item => item.CurrentPrice).HasColumnType("decimal(18,4)");
        marketData.Property(item => item.Ema9).HasColumnType("decimal(18,4)");
        marketData.Property(item => item.Ema20).HasColumnType("decimal(18,4)");
        marketData.Property(item => item.Ema50).HasColumnType("decimal(18,4)");
        marketData.Property(item => item.Rsi14).HasColumnType("decimal(18,4)");
        marketData.Property(item => item.Macd).HasColumnType("decimal(18,4)");
        marketData.Property(item => item.MacdSignal).HasColumnType("decimal(18,4)");
        marketData.Property(item => item.Atr).HasColumnType("decimal(18,4)");
        marketData.Property(item => item.Week52High).HasColumnType("decimal(18,4)");
        marketData.Property(item => item.Week52Low).HasColumnType("decimal(18,4)");
        marketData.HasIndex(item => item.TradingSignalId).IsUnique();
        marketData.HasOne(item => item.TradingSignal)
            .WithOne(signal => signal.MarketData)
            .HasForeignKey<SignalMarketData>(item => item.TradingSignalId)
            .OnDelete(DeleteBehavior.Cascade);

        var position = modelBuilder.Entity<Position>();
        position.HasKey(item => item.Id);
        position.Property(item => item.Symbol).HasMaxLength(32).IsRequired();
        position.Property(item => item.Status).HasMaxLength(16).IsRequired();
        position.Property(item => item.EntryPrice).HasColumnType("decimal(18,4)");
        position.Property(item => item.ExitPrice).HasColumnType("decimal(18,4)");
        position.Property(item => item.Quantity).HasColumnType("decimal(18,4)");
        position.Property(item => item.ProfitLoss).HasColumnType("decimal(18,4)");
        position.Property(item => item.ProfitLossPercent).HasColumnType("decimal(18,4)");
        position.Property(item => item.MaxRiskPercent).HasColumnType("decimal(18,4)");
        position.Property(item => item.Notes).HasMaxLength(4000);
        position.HasIndex(item => item.Symbol);
        position.HasIndex(item => item.Status);
        position.HasIndex(item => item.EntryTimeUtc);

        var webhookLog = modelBuilder.Entity<WebhookRequestLog>();
        webhookLog.HasKey(item => item.Id);
        webhookLog.Property(item => item.Source).HasMaxLength(32).IsRequired();
        webhookLog.Property(item => item.RemoteIp).HasMaxLength(64);
        webhookLog.Property(item => item.UserAgent).HasMaxLength(512);
        webhookLog.Property(item => item.RawPayload).IsRequired();
        webhookLog.Property(item => item.ResultStatus).HasMaxLength(32).IsRequired();
        webhookLog.Property(item => item.ErrorMessage).HasMaxLength(1024);
        webhookLog.HasIndex(item => item.ReceivedAtUtc);
        webhookLog.HasIndex(item => item.IsTest);
        webhookLog.HasIndex(item => item.TradingSignalId);

        var runtimeSetting = modelBuilder.Entity<RuntimeSetting>();
        runtimeSetting.HasKey(item => item.Key);
        runtimeSetting.Property(item => item.Key).HasMaxLength(64).IsRequired();
        runtimeSetting.Property(item => item.Value).HasMaxLength(512).IsRequired();
    }
}
