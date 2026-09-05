using System.Security.Claims;
using BlokeBot.Core.Features.ViewerPortal.Boundary;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PublicDocumentAdmissionTests
{
    [Test]
    public void PrivateDocumentMarker_DoesNotSurviveIdentityChangeLogoutOrTamperingButDoesSurviveRename()
    {
        var protection = new PublicDocumentProtector(new EphemeralDataProtectionProvider());
        var original = Principal("stable-id", "original");
        var document = protection.Create(false, original);
        protection
            .Read(document.Marker, Principal("stable-id", "renamed"))!
            .Nonce.ShouldBe(document.Document.Nonce);
        protection.Read(document.Marker, Principal("other-id", "original")).ShouldBeNull();
        protection.Read(document.Marker, new ClaimsPrincipal()).ShouldBeNull();
        protection.Read("broken" + document.Marker, original).ShouldBeNull();
        var replacedKeyRing = new PublicDocumentProtector(new EphemeralDataProtectionProvider());
        replacedKeyRing.Read(document.Marker, original).ShouldBeNull();
    }

    [Test]
    public void RetainedDocumentMarker_SurvivesKeyRotationButNotRevocationAfterRingReload()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-public-document-{Guid.NewGuid():N}"
        );
        try
        {
            var services = new ServiceCollection();
            _ = services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(directory));
            using var provider = services.BuildServiceProvider();
            var protection = new PublicDocumentProtector(
                provider.GetRequiredService<IDataProtectionProvider>()
            );
            var identity = Principal("stable-id", "viewer");
            var document = protection.Create(true, identity);
            var keys = provider.GetRequiredService<IKeyManager>();
            var originalKey = keys.GetAllKeys().Single();
            var now = DateTimeOffset.UtcNow;
            _ = keys.CreateNewKey(now.AddMinutes(-1), now.AddDays(90));
            protection.Read(document.Marker, identity)!.Nonce.ShouldBe(document.Document.Nonce);
            keys.RevokeKey(originalKey.KeyId, "Synthetic lifecycle verification");
            var reloadedServices = new ServiceCollection();
            _ = reloadedServices
                .AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(directory));
            using var reloadedProvider = reloadedServices.BuildServiceProvider();
            var reloaded = new PublicDocumentProtector(
                reloadedProvider.GetRequiredService<IDataProtectionProvider>()
            );
            (reloaded.Read(document.Marker, identity) is null).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ClaimsPrincipal Principal(string id, string name) =>
        new(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Name, name)],
                "test"
            )
        );
}
