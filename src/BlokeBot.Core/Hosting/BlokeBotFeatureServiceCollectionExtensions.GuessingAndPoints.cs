using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Guessing.HostSetup;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Points.HostSetup;
using BlokeBot.Core.Hosts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotGuessing(this IServiceCollection services)
    {
        _ = services.AddSingleton<CommandStrategyCatalog<GuessCommandKind, AppCommandRouteState>>();
        _ = services.AddSingleton<
            CommandStrategyDispatcher<GuessCommandKind, AppCommandRouteState>
        >();
        _ = services.AddSingleton<
            ICommandRouteResolver<GuessCommandKind, AppCommandRouteState>,
            GuessingCommandRouteResolver
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            StartGuessingCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            StopGuessingCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            WinGuessingCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            GuessCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            AvailableGuessesCommandStrategy
        >();
        _ = services.AddSingleton<GuessingCommandService>();
        _ = services.AddSingleton<GuessingConfigurationService>();
        _ = services.AddSingleton<GuessingDashboardService>();
        _ = services.AddSingleton<GuessingChangeNotifier>();
        _ = services.AddSingleton<GuessingRoundService>();
        _ = services.AddSingleton<GuessingVoteService>();
        _ = services.AddSingleton<GuessingHistoryService>();
        _ = services.AddSingleton<IBotHostSeeder, GuessingHostSeeder>();
        return services;
    }

    public static IServiceCollection AddBlokeBotPoints(
        this IServiceCollection services,
        PointsGiveawayNotificationMode notificationMode
    )
    {
        _ = services.AddSingleton<
            CommandStrategyCatalog<PointsCommandKind, AppCommandRouteState>
        >();
        _ = services.AddSingleton<
            CommandStrategyDispatcher<PointsCommandKind, AppCommandRouteState>
        >();
        _ = services.AddSingleton<
            ICommandRouteResolver<PointsCommandKind, AppCommandRouteState>,
            PointsCommandRouteResolver
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            PointsBalanceCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            GivePointsCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            AddPointsCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            RemovePointsCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            GambleCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            StartGiveawayCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            JoinGiveawayCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            EndGiveawayCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            CancelGiveawayCommandStrategy
        >();
        _ = services.AddSingleton<PointsCommandService>();
        _ = services.AddSingleton<PointBalanceService>();
        _ = services.AddSingleton<IPointTargetUserLookup, HelixPointTargetUserLookup>();
        _ = services.AddSingleton<PointsConfigurationService>();
        _ = services.AddSingleton<PointsDashboardService>();
        _ = services.AddSingleton<PointsGiveawayChangeNotifier>();
        _ = services.AddSingleton<
            IPointsGiveawayChangeNotification,
            PointsGiveawayChangeNotification
        >();
        _ = services.AddSingleton<
            IPointsGiveawaySchedulerOperations,
            PointsGiveawaySchedulerOperations
        >();
        _ = services.AddSingleton(
            new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.FromSeconds(30) }
        );
        switch (notificationMode)
        {
            case PointsGiveawayNotificationMode.ReplyOnly:
                _ = services.AddSingleton<
                    IPointsGiveawaySchedulerNotification,
                    ReplyOnlyPointsGiveawaySchedulerNotification
                >();
                break;
            case PointsGiveawayNotificationMode.PublicChat:
                _ = services.AddSingleton<
                    IPointsGiveawaySchedulerNotification,
                    PublicChatPointsGiveawaySchedulerNotification
                >();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(notificationMode),
                    notificationMode,
                    "Unknown points giveaway notification mode."
                );
        }

        _ = services.AddSingleton<PointsGiveawayScheduler>();
        _ = services.AddSingleton<IPointsGiveawayScheduler>(static sp =>
            sp.GetRequiredService<PointsGiveawayScheduler>()
        );
        _ = services.AddHostedService(static sp =>
            sp.GetRequiredService<PointsGiveawayScheduler>()
        );
        _ = services.AddSingleton<PointsGiveawayDrawService>();
        _ = services.AddSingleton<PointsGiveawayEligibilityPolicy>();
        _ = services.AddSingleton<PointsGiveawayMessageFormatter>();
        _ = services.AddSingleton<PointsGiveawayService>();
        _ = services.AddSingleton<IPointsRandom, PointsRandom>();
        _ = services.AddSingleton<PointsGamblingCooldownStore>();
        _ = services.AddSingleton<PointsChangeNotifier>();
        _ = services.AddSingleton<IBotHostSeeder, PointsHostSeeder>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
