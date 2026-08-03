namespace BlokeBot.Core.Features.Toasts;

public static class ToastServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotToasts(this IServiceCollection services)
    {
        _ = services.AddScoped<ToastService>();
        return services;
    }
}
