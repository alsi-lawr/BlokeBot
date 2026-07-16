using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Core.Features.Toasts;

public static class ToastServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotToasts(this IServiceCollection services)
    {
        services.AddScoped<ToastService>();
        return services;
    }
}
