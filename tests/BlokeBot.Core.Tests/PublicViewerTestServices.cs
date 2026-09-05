using System.Net;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Core.Features.ViewerPortal.Boundary;
using BlokeBot.Persistence;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Tests;

internal static class PublicViewerTestServices
{
    internal static void AddPublicViewerBoundary(
        this BunitContext context,
        IDbContextFactory<BlokeBotDbContext> database
    )
    {
        context.Services.TryAddSingleton(database);
        context.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        context.Services.TryAddSingleton<IHttpContextAccessor, TestRequest>();
        context.Services.TryAddSingleton<PublicLeaderboardHostLookup>();
        context.Services.TryAddSingleton<PublicViewerAdmission>();
        context.Services.TryAddScoped<PublicViewerCircuit>();
        context.Services.TryAddScoped<PublicViewerGate>();
    }

    private sealed class TestRequest(AuthenticationStateProvider authentication)
        : IHttpContextAccessor
    {
        public HttpContext? HttpContext
        {
            get
            {
                if (field is not null)
                {
                    field.Connection.RemoteIpAddress = IPAddress.Loopback;
                    field.User = authentication
                        .GetAuthenticationStateAsync()
                        .GetAwaiter()
                        .GetResult()
                        .User;
                }
                return field;
            }
            set;
        } = new DefaultHttpContext();
    }
}
