using System.Net;
using CommandBot.Store;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder
    .Services.AddOptions<TwitchBotOptions>()
    .BindConfiguration("TwitchBot")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient("twitch-oauth");
builder.Services.AddDbContextFactory<CounterDbContext>(
    (sp, db) =>
    {
        var counters = sp.GetRequiredService<IOptions<TwitchBotOptions>>().Value.Counters;
        var fullPath = Path.GetFullPath(counters.DatabasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        db.UseSqlite(connectionString);
    }
);
builder.Services.AddSingleton<ICounterStore, EfCounterStore>();
builder.Services.AddSingleton<ITokenCache, JsonTokenCache>();
builder.Services.AddSingleton<ITwitchOAuthClient, TwitchOAuthClient>();
builder.Services.AddSingleton<IAccessTokenProvider, AccessTokenProvider>();
builder.Services.AddSingleton<IOAuthStateStore, InMemoryOAuthStateStore>();

builder.Services.AddHostedService<TwitchBotWorker>();

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
    (ITwitchOAuthClient oauth, IOAuthStateStore states) =>
    {
        var state = states.Issue();
        return Results.Redirect(oauth.BuildAuthorizeUri(state).ToString());
    }
);

app.MapGet(
    "/oauth/callback",
    async (
        string? code,
        string? state,
        string? error,
        ITwitchOAuthClient oauth,
        IOAuthStateStore states,
        ITokenCache cache,
        IOptions<TwitchBotOptions> opts,
        CancellationToken ct
    ) =>
    {
        if (!string.IsNullOrWhiteSpace(error))
            return Results.Content($"OAuth error: {WebUtility.HtmlEncode(error)}", "text/plain");

        if (string.IsNullOrWhiteSpace(code))
            return Results.BadRequest("Missing code");

        if (string.IsNullOrWhiteSpace(state) || !states.Consume(state))
            return Results.BadRequest("Invalid state");

        var token = await oauth.ExchangeCodeAsync(code, ct);
        cache.Save(opts.Value.Identity.TokenCachePath, token);

        return Results.Content("OK. Tokens saved. You can close this window.", "text/plain");
    }
);
app.Run();
