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
                        KindIn("Feature", ReplyFeaturePersistence.Tokens)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_reply_delivery_settings_Target",
                        KindIn("Target", ReplyDeliveryTargetPersistence.Tokens)
                    );
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Feature)
                .HasConversion(
                    feature => ReplyFeaturePersistence.ToToken(feature),
                    token => ReplyFeaturePersistence.FromToken(token)
                )
                .HasMaxLength(64);
            _ = b.Property(x => x.ReplyKey).HasMaxLength(128);
            _ = b.Property(x => x.Target)
                .HasConversion(
                    target => ReplyDeliveryTargetPersistence.ToToken(target),
                    token => ReplyDeliveryTargetPersistence.FromToken(token)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.Feature,
                    x.ScopeId,
                    x.ReplyKey,
                })
                .IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
