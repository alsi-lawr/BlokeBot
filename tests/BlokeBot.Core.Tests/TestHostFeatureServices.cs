using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlokeBot.Core.Tests;

internal static class TestHostFeatureServices
{
    internal static IServiceCollection Register(IServiceCollection services)
    {
        _ = services.AddLogging();
        services.TryAddSingleton(TestEventBus.Create<AppEventKind>());
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<HostedChannelChangeNotifier>();
        services.TryAddSingleton<DurableAlertService>();
        services.TryAddSingleton<HostFeatureActivationAuthority>();
        services.TryAddSingleton<HostFeatureService>();
        return services;
    }

    internal static HostFeatureService Create(
        IDbContextFactory<BlokeBotDbContext> database,
        HostedChannelChangeNotifier changes,
        IEnumerable<IHostFeatureActivationObserver> observers,
        TimeProvider? timeProvider = null
    )
    {
        var clock = timeProvider ?? TimeProvider.System;
        var alerts = new DurableAlertService(database, clock, TestEventBus.Create<AppEventKind>());
        return new(
            database,
            new(observers, changes, alerts, NullLogger<HostFeatureActivationAuthority>.Instance),
            clock
        );
    }
}
