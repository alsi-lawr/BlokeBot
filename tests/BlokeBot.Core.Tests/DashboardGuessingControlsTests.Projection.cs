using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardGuessingControlsTests
{
    [Test]
    public async Task DashboardGuessing_ExternalConfigurationAndActions_RefreshDeletedAndHostScopedBindings()
    {
        await using var f = await Fixture.CreateAsync();
        _ = (
            await f.Rounds.StartRound(f.Host.Value, f.Profile).RunAsync(_ct)
        ).ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        var next = Configuration(f.NextProfile);
        var states = new List<DashboardResult<DashboardGuessingResult<GuessingControlState>>>();
        using var subscription = f.Events.Subscribe(
            AppEventKind.GuessingChanged,
            ObserverIdentity.Named("bound-next"),
            async (_, ct) => states.Add(await f.Controls.ReadAsync(f.Owner, f.Host, next, ct))
        );
        var configuration = new GuessingConfigurationService(f.Database, f.Notifier);
        var load = await configuration
            .LoadConfiguration(f.Host.Value, new GuessingProfileSelection.Selected(f.NextProfile))
            .ExecuteAsync(_ct);
        var loaded = load.Match(
            value => value,
            failure => throw new InvalidOperationException(failure.Message)
        );
        loaded.Profile.Name = "Changed Next";
        loaded.Profile.Options[0].Name = "Silver, Grey";
        var command = GuessingConfigurationValidator
            .Validate(loaded)
            .Match(
                value => value,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(x => x.Message))
                    )
            );
        var saved = await configuration.SaveConfiguration(f.Host.Value, command).ExecuteAsync(_ct);
        saved.Match(_ => true, _ => false).ShouldBeTrue();
        var changed = Ready(states.ShouldHaveSingleItem());
        changed.SelectedProfile.Name.ShouldBe(loaded.Profile.Name);
        changed.SelectedProfile.Revision.ShouldBe(loaded.Profile.Revision + 1);
        changed.WinnerOptions.Select(x => x.Value).ShouldBe(["blue", "red"]);
        _ = (
            await f.Rounds.StartRound(f.Other.Value, f.OtherProfile).RunAsync(_ct)
        ).ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        Ready(states.Last())
            .CurrentRound.ShouldNotBeNull()
            .Id.ShouldBe(changed.CurrentRound.ShouldNotBeNull().Id);
        var deleted = await configuration
            .DeleteProfile(f.Host.Value, new(f.NextProfile, changed.SelectedProfile.Revision))
            .ExecuteAsync(_ct);
        deleted.Match(_ => true, _ => false).ShouldBeTrue();
        Unavailable(states.Last()).ShouldBe(DashboardBindingAvailability.Missing);
        foreach (var binding in new[] { next, Configuration(f.OtherProfile) })
        {
            Unavailable(await f.Controls.ReadAsync(f.Owner, f.Host, binding, _ct))
                .ShouldBe(DashboardBindingAvailability.Missing);
            Unavailable(await f.Controls.StartAsync(f.Owner, f.Host, binding, _ct))
                .ShouldBe(DashboardBindingAvailability.Missing);
            Unavailable(await f.Controls.StopAsync(f.Owner, f.Host, binding, _ct))
                .ShouldBe(DashboardBindingAvailability.Missing);
            Unavailable(
                    await f.Controls.DeclareWinnerAsync(
                        f.Owner,
                        f.Host,
                        binding,
                        GuessName.Parse("blue"),
                        _ct
                    )
                )
                .ShouldBe(DashboardBindingAvailability.Missing);
        }
        Ready(await f.Controls.ReadAsync(f.Owner, f.Host, f.Bound, _ct))
            .CurrentRound.ShouldNotBeNull()
            .Id.ShouldBe(changed.CurrentRound.Id);
        var count = states.Count;
        subscription.Dispose();
        _ = (
            await f.Rounds.StopGuessing(f.Host.Value).RunAsync(_ct)
        ).ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        states.Count.ShouldBe(count);
    }

    [Test]
    public async Task DashboardGuessing_CompactProjection_DoesNotScaleWithUnrelatedProfilesVotesOrHistory() =>
        await CompactProjectionAsync(await Fixture.CreateAsync());

    [Test]
    [Explicit]
    public async Task DashboardGuessing_PostgreSql_CompactProjection_HostFilteringAndBoundedVolume() =>
        await CompactProjectionAsync(await Fixture.CreateAsync(postgres: true));

    private static async Task CompactProjectionAsync(Fixture fixture)
    {
        await using var f = fixture;
        _ = await f.Rounds.StartRound(f.Host.Value, f.Profile).RunAsync(_ct);
        var selected = Configuration(f.NextProfile);
        f.Queries.Reset();
        var small = Ready(await f.Controls.ReadAsync(f.Owner, f.Host, selected, _ct));
        var commands = f.Queries.Commands.Count;
        var readerOperations = f.Queries.ReaderOperations.Sum();
        f.Queries.Entities.ShouldBeEmpty();
        await f.SeedVolumeAsync(100, 300, 1000);
        var large = Ready(await f.Controls.ReadAsync(f.Owner, f.Host, selected, _ct));
        large.ShouldBe(small with { WinnerOptions = large.WinnerOptions });
        large.WinnerOptions.SequenceEqual(small.WinnerOptions).ShouldBeTrue();
        f.Queries.Commands.Count.ShouldBe(commands);
        f.Queries.ReaderOperations.Sum().ShouldBe(readerOperations);
        f.Queries.Entities.ShouldBeEmpty();
        Console.WriteLine(
            $"Compact guessing refresh: {commands} reader commands; {readerOperations} DbDataReader.Read operations; selected descriptor + one current header + {large.WinnerOptions.Length} option names; zero entities.100 unrelated profiles/300 completed rounds per host;1000 current votes."
        );
        var destination = Environment.GetEnvironmentVariable(
            "BLOKEBOT_DASHBOARD_GUESSING_SQL_OUTPUT"
        );
        if (!string.IsNullOrWhiteSpace(destination))
        {
            await File.WriteAllTextAsync(destination, string.Join("\n---\n", f.Queries.Commands));
        }
    }
}
