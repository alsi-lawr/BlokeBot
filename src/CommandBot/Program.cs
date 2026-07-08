using System.Net;
using BlokeBot.Commands;
using BlokeBot.Twitch.Runtime;
using CommandBot.Store;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder
    .Services.AddOptions<DeathCounterOptions>()
    .BindConfiguration("TwitchBot:Counters")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder
    .Services.AddOptions<AllowedLoginOptions>()
    .BindConfiguration("TwitchBot:Filters")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContextFactory<CounterDbContext>(
    (sp, db) =>
    {
        var counters = sp.GetRequiredService<IOptions<DeathCounterOptions>>().Value;
        var fullPath = Path.GetFullPath(counters.DatabasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
        }.ToString();
        db.UseSqlite(connectionString);
    }
);
builder.Services.AddSingleton<ICounterStore, EfCounterStore>();
builder.Services.AddSingleton<AllowedUsersFilter>();
builder
    .Services.AddTwitchBot(builder.Configuration.GetSection("TwitchBot"))
    .AddCommands(commands => commands.UseFilter<AllowedUsersFilter>())
    .AddCommandModule<DeathCounterCommandModule>();

var app = builder.Build();

await app.Services.GetRequiredService<ICounterStore>().EnsureCreatedAsync(CancellationToken.None);

app.UseForwardedHeaders(
    new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    }
);
app.UsePathBase("/twitch");
app.MapGet("/health", () => Results.Ok("ok"));
app.MapGet(
    "/deaths",
    async (ICounterStore store, CancellationToken ct) =>
        Results.Ok(new { deaths = await store.LoadAsync(CounterKeys.Deaths, ct) })
);
app.MapGet(
    "/oauth/start",
    (ITwitchOAuthFlow oauth) => Results.Redirect(oauth.CreateAuthorizationUri().ToString())
);

app.MapGet(
    "/oauth/callback",
    async (
        string? code,
        string? state,
        string? error,
        ITwitchOAuthFlow oauth,
        CancellationToken ct
    ) =>
    {
        if (!string.IsNullOrWhiteSpace(error))
            return Results.Content($"OAuth error: {WebUtility.HtmlEncode(error)}", "text/plain");

        if (string.IsNullOrWhiteSpace(code))
            return Results.BadRequest("Missing code");

        if (string.IsNullOrWhiteSpace(state))
            return Results.BadRequest("Invalid state");

        try
        {
            await oauth.CompleteAuthorizationAsync(code, state, ct);
            return Results.Content("OK. Tokens saved. You can close this window.", "text/plain");
        }
        catch (InvalidOperationException)
        {
            return Results.BadRequest("Invalid state");
        }
    }
);
app.Run();
