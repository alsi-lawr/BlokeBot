using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureReplyDelivery(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ReplyDeliverySetting>(b =>
        {
            _ = b.ToTable(
                "reply_delivery_settings",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_reply_delivery_settings_Feature",
                        KindIn(modelBuilder, "Feature", ReplyFeaturePersistence.Tokens)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_reply_delivery_settings_Target",
                        KindIn(modelBuilder, "Target", ReplyDeliveryTargetPersistence.Tokens)
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Feature)
                .HasConversion(
                    static feature => ReplyFeaturePersistence.ToToken(feature),
                    static token => ReplyFeaturePersistence.FromToken(token)
                )
                .HasMaxLength(64);
            _ = b.Property(static x => x.ReplyKey).HasMaxLength(128);
            _ = b.Property(static x => x.Target)
                .HasConversion(
                    static target => ReplyDeliveryTargetPersistence.ToToken(target),
                    static token => ReplyDeliveryTargetPersistence.FromToken(token)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.Feature,
                    x.ScopeId,
                    x.ReplyKey,
                })
                .IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
