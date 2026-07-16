using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class GuessingConfigurationCommandTests
{
    [Test]
    public async Task PreCancelledExecution_LoadingConfiguration_PropagatesCancellation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = ConfigurationService(dbFactory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            service
                .LoadConfiguration(1, new GuessingProfileSelection.Default())
                .ExecuteAsync(cancellation.Token)
                .AsTask()
        );
    }

    [Test]
    public async Task MissingProfile_LoadingOptionalEditor_ReturnsAbsence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        await using var db = await dbFactory.CreateDbContextAsync();

        var editor = await GuessingConfigurationService.LoadProfileEditorAsync(
            db,
            seed.HostId,
            int.MaxValue,
            CancellationToken.None
        );

        editor.ShouldBeNull();
    }

    [Test]
    public async Task InvalidDraft_SubmittingThroughValidation_PerformsNoWrite()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);
        var draft = await LoadAsync(service, seed.HostId, seed.DefaultProfileId);
        draft.Profile.Name = "Changed";
        draft.Profile.Options[0].Name = " ";
        var saveInvoked = false;

        await GuessingConfigurationValidator
            .Validate(draft)
            .Match(
                async command =>
                {
                    saveInvoked = true;
                    await service
                        .SaveConfiguration(seed.HostId, command)
                        .ExecuteAsync(CancellationToken.None);
                },
                _ => Task.CompletedTask
            );

        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.Profiles.SingleAsync(x => x.Id == seed.DefaultProfileId);
        saveInvoked.ShouldBeFalse();
        profile.Name.ShouldBe("Default");
        profile.Revision.ShouldBe(0);
    }

    [Test]
    public async Task StaleCommand_Saving_ReturnsConcurrentEditWithoutReplacingCommittedState()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);
        var firstDraft = await LoadAsync(service, seed.HostId, seed.SpecialProfileId);
        var staleDraft = await LoadAsync(service, seed.HostId, seed.SpecialProfileId);
        firstDraft.Profile.Name = "First";
        staleDraft.Profile.Name = "Stale";
        var firstCommand = ValidCommand(firstDraft);
        var staleCommand = ValidCommand(staleDraft);

        await service
            .SaveConfiguration(seed.HostId, firstCommand)
            .ExecuteAsync(CancellationToken.None);
        var staleResult = await service
            .SaveConfiguration(seed.HostId, staleCommand)
            .ExecuteAsync(CancellationToken.None);

        staleResult
            .Match<GuessingConfigurationSaveFailure?>(_ => null, failure => failure)
            .ShouldBeOfType<GuessingConfigurationSaveFailure.ConcurrentEdit>();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.Profiles.SingleAsync(x => x.Id == seed.SpecialProfileId)).Name.ShouldBe("First");
    }

    [Test]
    public async Task DefaultProfile_Deleting_AtomicallySelectsOneReplacement()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);
        var draft = await LoadAsync(service, seed.HostId, seed.DefaultProfileId);
        var command = GuessingConfigurationValidator
            .ValidateDelete(draft)
            .Match(
                value => value,
                errors => throw new InvalidOperationException(ValidationMessage(errors))
            );

        var result = await service
            .DeleteProfile(seed.HostId, command)
            .ExecuteAsync(CancellationToken.None);

        result.Match(_ => true, _ => false).ShouldBeTrue();
        await using var db = await dbFactory.CreateDbContextAsync();
        var profiles = await db.Profiles.Where(x => x.HostId == seed.HostId).ToListAsync();
        profiles.ShouldHaveSingleItem().Id.ShouldBe(seed.SpecialProfileId);
        profiles.ShouldHaveSingleItem().IsDefault.ShouldBeTrue();
    }

    [Test]
    public async Task DeletedSelection_Loading_ReturnsTypedNotFound()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProfilesAsync(dbFactory);
        var service = ConfigurationService(dbFactory);

        var result = await service
            .LoadConfiguration(seed.HostId, new GuessingProfileSelection.Selected(int.MaxValue))
            .ExecuteAsync(CancellationToken.None);

        var failure = result.Match<GuessingConfigurationLoadFailure?>(_ => null, error => error);

        failure.ShouldNotBeNull();
        failure.ShouldBe(new GuessingConfigurationLoadFailure());
        failure.Message.ShouldBe(
            "That round type is no longer available. Reloaded the current settings."
        );
    }

    private static GuessingConfigurationService ConfigurationService(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        return new(dbFactory, new GuessingChangeNotifier(TestEventBus.Create<AppEventKind>()));
    }

    private static async Task<GuessingConfiguration> LoadAsync(
        GuessingConfigurationService service,
        int hostId,
        int profileId
    )
    {
        var result = await service
            .LoadConfiguration(hostId, new GuessingProfileSelection.Selected(profileId))
            .ExecuteAsync(CancellationToken.None);
        return result.Match(
            configuration => configuration,
            failure => throw new InvalidOperationException(failure.Message)
        );
    }

    private static GuessingConfigurationSaveCommand ValidCommand(GuessingConfiguration draft)
    {
        return GuessingConfigurationValidator
            .Validate(draft)
            .Match(
                command => command,
                errors => throw new InvalidOperationException(ValidationMessage(errors))
            );
    }

    private static string ValidationMessage(
        IReadOnlyList<GuessingConfigurationValidationError> errors
    )
    {
        return string.Join(" ", errors.Select(error => error.Message));
    }

    private static async Task<ProfileSeed> SeedProfilesAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var defaultProfile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
            ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
            Options = [new GuessOption { Name = "red", ReplyText = "Red" }],
        };
        var specialProfile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Special",
            Slug = "special",
            ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
            Options = [new GuessOption { Name = "blue", ReplyText = "Blue" }],
        };
        db.Profiles.AddRange(defaultProfile, specialProfile);
        await db.SaveChangesAsync();
        return new(host.Id, defaultProfile.Id, specialProfile.Id);
    }

    private sealed record ProfileSeed(int HostId, int DefaultProfileId, int SpecialProfileId);
}
