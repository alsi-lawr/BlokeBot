using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationImportAuditPrivacyTests
{
    [Test]
    public async Task Audit_ExportsErasesIdentityAndCascadesWithHost()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        var operationId = Guid.NewGuid();
        const string Summary = "{\"Sections\":[{\"Id\":\"points\",\"Count\":1}]}";
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "destination",
                DisplayName = "Destination",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            _ = seed.ConfigurationImportAudits.Add(
                new()
                {
                    HostId = hostId,
                    OperationId = operationId,
                    ActorTwitchUserId = "actor-id",
                    ActorLogin = "actor",
                    SourceFormatVersion = 1,
                    OccurredAtUtc = DateTime.UtcNow,
                    SummaryJson = Summary,
                }
            );
            _ = seed.ConfigurationActivations.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    HostId = hostId,
                    Status = ConfigurationActivationStatus.Complete,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        await using (var db = await database.CreateDbContextAsync())
        {
            var exported = await ViewerPrivacyService.ExportAsync(
                db,
                PrivacySubject.Create("actor-id", "actor"),
                hostId,
                CancellationToken.None
            );
            exported.Sections["configuration-imports.audits"].Count.ShouldBe(1);
            var erased = await ViewerPrivacyService.EraseAsync(
                db,
                PrivacySubject.Create("actor-id", "actor"),
                hostId,
                CancellationToken.None
            );
            erased.ChangedRows["configuration-imports.audits.actor"].ShouldBe(1);
        }

        await using (var verify = await database.CreateDbContextAsync())
        {
            var audit = await verify.ConfigurationImportAudits.SingleAsync();
            audit.ActorTwitchUserId.ShouldBe(ViewerPrivacyService.ErasedToken);
            audit.ActorLogin.ShouldBe(ViewerPrivacyService.ErasedToken);
            audit.OperationId.ShouldBe(operationId);
            audit.SourceFormatVersion.ShouldBe(1);
            audit.SummaryJson.ShouldBe(Summary);
            _ = verify.Hosts.Remove(await verify.Hosts.SingleAsync(x => x.Id == hostId));
            _ = await verify.SaveChangesAsync();
        }

        await using var final = await database.CreateDbContextAsync();
        (await final.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
        (await final.ConfigurationActivations.CountAsync()).ShouldBe(0);
    }
}
