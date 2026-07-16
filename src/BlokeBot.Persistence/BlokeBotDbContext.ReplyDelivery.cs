using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureReplyDelivery(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReplyDeliverySetting>(b =>
        {
            b.ToTable(
                "reply_delivery_settings",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_reply_delivery_settings_Feature",
                        KindIn("Feature", ReplyFeaturePersistence.Tokens)
                    );
                    t.HasCheckConstraint(
                        "CK_reply_delivery_settings_Target",
                        KindIn("Target", ReplyDeliveryTargetPersistence.Tokens)
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Feature)
                .HasConversion(
                    feature => ReplyFeaturePersistence.ToToken(feature),
                    token => ReplyFeaturePersistence.FromToken(token)
                )
                .HasMaxLength(64);
            b.Property(x => x.ReplyKey).HasMaxLength(128);
            b.Property(x => x.Target)
                .HasConversion(
                    target => ReplyDeliveryTargetPersistence.ToToken(target),
                    token => ReplyDeliveryTargetPersistence.FromToken(token)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.Feature,
                    x.ScopeId,
                    x.ReplyKey,
                })
                .IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
