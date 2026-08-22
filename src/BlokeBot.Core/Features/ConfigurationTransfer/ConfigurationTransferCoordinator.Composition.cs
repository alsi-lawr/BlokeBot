using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationTransferCoordinator
{
    private readonly IDbContextFactory<BlokeBotDbContext> _dbFactory;
    private readonly CustomCommandConfigurationTransferAdapter _customCommands;
    private readonly IModeratorAuthorityService _moderatorAuthority;
    private readonly ConfigurationActivationQueue _activationQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConfigurationTransferCoordinator> _logger;
    private readonly ConfigurationImportPreviewService _previews;
    private readonly IOverlayConfigurationTransferAdapter _overlays;
    private readonly IAutomationConfigurationTransferAdapter _automations;
    private readonly IConfigurationImportObserverDispatcher _importObservers;
    private readonly SemaphoreSlim _overlayMediaGate;

    public ConfigurationTransferCoordinator(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        CustomCommandConfigurationTransferAdapter customCommands,
        IModeratorAuthorityService moderatorAuthority,
        ConfigurationActivationQueue activationQueue,
        TimeProvider timeProvider,
        ILogger<ConfigurationTransferCoordinator> logger
    )
        : this(
            dbFactory,
            customCommands,
            moderatorAuthority,
            activationQueue,
            timeProvider,
            logger,
            new(dbFactory),
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            UnavailableAutomationConfigurationTransferAdapter.Instance,
            UnavailableConfigurationImportObserverDispatcher.Instance,
            new(1, 1)
        ) { }

    internal ConfigurationTransferCoordinator(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        CustomCommandConfigurationTransferAdapter customCommands,
        IModeratorAuthorityService moderatorAuthority,
        ConfigurationActivationQueue activationQueue,
        TimeProvider timeProvider,
        ILogger<ConfigurationTransferCoordinator> logger,
        ConfigurationImportPreviewService previews,
        IOverlayConfigurationTransferAdapter overlays,
        IAutomationConfigurationTransferAdapter automations,
        IConfigurationImportObserverDispatcher importObservers,
        SemaphoreSlim overlayMediaGate
    )
    {
        _dbFactory = dbFactory;
        _customCommands = customCommands;
        _moderatorAuthority = moderatorAuthority;
        _activationQueue = activationQueue;
        _timeProvider = timeProvider;
        _logger = logger;
        _previews = previews;
        _overlays = overlays;
        _automations = automations;
        _importObservers = importObservers;
        _overlayMediaGate = overlayMediaGate;
    }
}
