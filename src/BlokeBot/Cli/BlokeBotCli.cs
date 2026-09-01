using BlokeBot.Hosting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BlokeBot.Cli;

internal static class BlokeBotCli
{
    internal static Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        IBlokeBotCommandRuntime? runtime = null,
        IAnsiConsole? console = null,
        CancellationToken cancellationToken = default
    )
    {
        runtime ??= new BlokeBotCommandRuntime();
        console ??= AnsiConsole.Console;

        var registrar = new BlokeBotTypeRegistrar();
        registrar.RegisterInstance(runtime);
        registrar.RegisterInstance(console);

        var app = new CommandApp(registrar);
        app.Configure(configuration =>
        {
            _ = configuration.SetApplicationName("blokebot");
            _ = configuration.SetApplicationVersion(BlokeBotVersion.Current);
            _ = configuration.ConfigureConsole(console);
            _ = configuration.UseStrictParsing();
            _ = configuration.SetExceptionHandler(
                (exception, _) =>
                {
                    BlokeBotHostLogging.HostFailure(exception);
                    console.MarkupLine(
                        $"[red]blokebot failed ({Markup.Escape(exception.GetType().Name)}).[/]"
                    );
                    return 1;
                }
            );
            _ = configuration
                .AddCommand<BlokeBotHelpCommand>("help")
                .WithDescription("Show help and exit.");
            _ = configuration
                .AddCommand<BlokeBotVersionCommand>("version")
                .WithDescription("Show version information and exit.");
            _ = configuration
                .AddCommand<BlokeBotServeCommand>("serve")
                .WithDescription("Start the bot and dashboard.");
            _ = configuration.AddBranch(
                "privacy",
                privacy =>
                {
                    privacy.SetDescription(
                        "Fulfil verified privacy requests against this deployment's data."
                    );
                    _ = privacy
                        .AddCommand<BlokeBotPrivacyExportCommand>("export")
                        .WithDescription("Export the stored data for one Twitch identity as JSON.");
                    _ = privacy
                        .AddCommand<BlokeBotPrivacyEraseCommand>("erase")
                        .WithDescription(
                            "Delete or de-identify the stored data for one Twitch identity."
                        );
                }
            );
            _ = configuration.AddBranch(
                "database",
                database =>
                {
                    database.SetDescription("Run offline main-database operations.");
                    _ = database
                        .AddCommand<BlokeBotDatabaseCutoverCommand>("cutover-postgresql")
                        .WithDescription(
                            "Copy the stopped SQLite main database to an empty PostgreSql target."
                        );
                }
            );
        });

        var normalizedArguments = arguments.Count == 0 ? ["help"] : arguments;
        return app.RunAsync(normalizedArguments, cancellationToken);
    }
}

internal sealed class BlokeBotHelpCommand(IAnsiConsole console) : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        console.WriteLine(
            $$"""
            blokebot {{BlokeBotVersion.Current}}
            BlokeBot is free, open-source, and easy to host.

            Usage:
              blokebot
              blokebot help
              blokebot version
              blokebot serve [--host HOST] [--port PORT] [--data-dir PATH] [--config PATH]
              blokebot privacy export [--login LOGIN] [--user-id ID] [--host-id ID]
                                      [--output FILE] [--data-dir PATH] [--config PATH]
              blokebot privacy erase --confirm [--login LOGIN] [--user-id ID] [--host-id ID]
                                     [--data-dir PATH] [--config PATH]
              blokebot database cutover-postgresql --postgresql-connection-string-file FILE
                                                    [--operation-id UUID] [--batch-size ROWS]
                                                    [--data-dir PATH] [--config PATH]

            Commands:
              help            Show this help and exit.
              version         Show version information and exit.
              serve           Start the bot and dashboard.
              privacy export  Export one Twitch identity's stored data as JSON, for
                              verified access and portability requests.
              privacy erase   Delete that identity's rows and strip its identity from
                              records kept for aggregate or audit integrity. Scope to
                              one channel with --host-id. Safe to re-run.
              database cutover-postgresql
                              Copy the stopped SQLite main database to PostgreSql and
                              verify it without changing active configuration.

            Serve options:
              --host HOST      Dashboard host. Default: 127.0.0.1.
              --port PORT      Dashboard port. Default: 8080.
              --data-dir PATH  Store blokebot.db and twitch.tokens.json in PATH unless
                               either path is set explicitly in configuration.
              --config PATH    Load an additional JSON configuration file.

            Required Twitch configuration for online mode:
              BotUsername  TwitchBot__Identity__BotUsername; the bot account login.
              ClientId     TwitchBot__Identity__ClientId; the Twitch application ID.
              ClientSecret TwitchBot__Identity__ClientSecret; keep this credential private.
              RedirectUri  TwitchBot__Identity__RedirectUri; the OAuth callback URL, which
                           must exactly match the callback registered with Twitch.

            Optional public URL configuration:
              PublicBaseUrl BlokeBot__PublicBaseUrl; the public dashboard URL used in chat links.
                            If unset, BlokeBot uses the protocol, host, and port from RedirectUri.

            Required privacy configuration for online mode:
              ControllerName BlokeBotPrivacy__ControllerName; who operates this deployment,
                             as named in its privacy notice.
              PrivacyContact BlokeBotPrivacy__PrivacyContact; a monitored email address for
                             privacy requests.
              NoticeUrl      BlokeBotPrivacy__NoticeUrl; the absolute HTTPS URL of this
                             deployment's privacy notice. Every deployment supplies its own
                             values; there are no defaults.

            State data defaults:
              Linux   $XDG_STATE_HOME/blokebot, or ~/.local/state/blokebot
              macOS   ~/Library/Application Support/BlokeBot
              Windows %LOCALAPPDATA%\BlokeBot

            Explicit database/token configuration overrides --data-dir, which overrides
            the platform default. The default dashboard URL is http://127.0.0.1:8080.

            Guides:
              User Guide: https://github.com/alsi-lawr/BlokeBot/wiki/User-Guide
              Server Owner Guide: https://github.com/alsi-lawr/BlokeBot/wiki/Server-Owner-Guide
            """
        );
        return 0;
    }
}

internal sealed class BlokeBotVersionCommand(IAnsiConsole console) : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        console.WriteLine($"blokebot {BlokeBotVersion.Current}");
        return 0;
    }
}

internal sealed class BlokeBotServeSettings : CommandSettings
{
    [CommandOption("--host <HOST>")]
    public string? Host { get; init; }

    [CommandOption("--port <PORT>")]
    public int? Port { get; init; }

    [CommandOption("--data-dir <PATH>")]
    public string? DataDirectory { get; init; }

    [CommandOption("--config <PATH>")]
    public string? ConfigurationPath { get; init; }
}

internal sealed class BlokeBotServeCommand(IBlokeBotCommandRuntime runtime, IAnsiConsole console)
    : AsyncCommand<BlokeBotServeSettings>
{
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        BlokeBotServeSettings settings,
        CancellationToken cancellationToken
    ) =>
        runtime.ServeAsync(
            new BlokeBotServeOptions(
                settings.Host,
                settings.Port,
                settings.DataDirectory,
                settings.ConfigurationPath
            ),
            console,
            cancellationToken
        );
}

internal class BlokeBotPrivacyCommandSettings : CommandSettings
{
    [CommandOption("--login <LOGIN>")]
    public string? Login { get; init; }

    [CommandOption("--user-id <ID>")]
    public string? UserId { get; init; }

    [CommandOption("--host-id <ID>")]
    public int? HostId { get; init; }

    [CommandOption("--data-dir <PATH>")]
    public string? DataDirectory { get; init; }

    [CommandOption("--config <PATH>")]
    public string? ConfigurationPath { get; init; }

    internal BlokeBotPrivacyOptions ToOptions() =>
        new(Login, UserId, HostId, DataDirectory, ConfigurationPath);
}

internal sealed class BlokeBotPrivacyExportSettings : BlokeBotPrivacyCommandSettings
{
    [CommandOption("--output <FILE>")]
    public string? Output { get; init; }
}

internal sealed class BlokeBotPrivacyEraseSettings : BlokeBotPrivacyCommandSettings
{
    [CommandOption("--confirm")]
    public bool Confirm { get; init; }
}

internal sealed class BlokeBotDatabaseCutoverSettings : CommandSettings
{
    [CommandOption("--postgresql-connection-string-file <FILE>")]
    public required string PostgreSqlConnectionStringFile { get; init; }

    [CommandOption("--operation-id <UUID>")]
    public Guid? OperationId { get; init; }

    [CommandOption("--batch-size <ROWS>")]
    public int BatchSize { get; init; } = 500;

    [CommandOption("--data-dir <PATH>")]
    public string? DataDirectory { get; init; }

    [CommandOption("--config <PATH>")]
    public string? ConfigurationPath { get; init; }
}

internal sealed class BlokeBotDatabaseCutoverCommand(IAnsiConsole console)
    : AsyncCommand<BlokeBotDatabaseCutoverSettings>
{
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        BlokeBotDatabaseCutoverSettings settings,
        CancellationToken cancellationToken
    ) => BlokeBotDatabaseCutoverActions.RunAsync(settings, console, cancellationToken);
}

internal sealed class BlokeBotPrivacyExportCommand(IAnsiConsole console)
    : AsyncCommand<BlokeBotPrivacyExportSettings>
{
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        BlokeBotPrivacyExportSettings settings,
        CancellationToken cancellationToken
    ) =>
        BlokeBotPrivacyActions.ExportAsync(
            settings.ToOptions(),
            settings.Output,
            console,
            cancellationToken
        );
}

internal sealed class BlokeBotPrivacyEraseCommand(IAnsiConsole console)
    : AsyncCommand<BlokeBotPrivacyEraseSettings>
{
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        BlokeBotPrivacyEraseSettings settings,
        CancellationToken cancellationToken
    ) =>
        BlokeBotPrivacyActions.EraseAsync(
            settings.ToOptions(),
            settings.Confirm,
            console,
            cancellationToken
        );
}

internal interface IBlokeBotCommandRuntime
{
    Task<int> ServeAsync(
        BlokeBotServeOptions options,
        IAnsiConsole console,
        CancellationToken cancellationToken
    );
}

internal sealed class BlokeBotCommandRuntime : IBlokeBotCommandRuntime
{
    public Task<int> ServeAsync(
        BlokeBotServeOptions options,
        IAnsiConsole console,
        CancellationToken cancellationToken
    ) => BlokeBotHost.RunAsync(options, console, cancellationToken);
}

internal sealed record BlokeBotServeOptions(
    string? Host,
    int? Port,
    string? DataDirectory,
    string? ConfigurationPath
);

internal sealed class BlokeBotTypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services = new ServiceCollection();

    public ITypeResolver Build() => new BlokeBotTypeResolver(_services.BuildServiceProvider());

    public void Register(Type service, Type implementation) =>
        _services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) =>
        _services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) =>
        _services.AddSingleton(service, _ => factory());

    internal void RegisterInstance<TService>(TService implementation)
        where TService : class => RegisterInstance(typeof(TService), implementation);
}

internal sealed class BlokeBotTypeResolver(IServiceProvider services) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type) => type is null ? null : services.GetService(type);

    public void Dispose()
    {
        if (services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
