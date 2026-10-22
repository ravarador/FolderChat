using FolderChat.Application.Chat;
using FolderChat.Application.Conversations;
using FolderChat.Application.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FolderChat.Application;

public static class DependencyInjection
{
    /// <summary>Registers the use cases. The ports they need come from the infrastructure layer.</summary>
    public static IServiceCollection AddFolderChatApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ProviderService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<ChatService>();
        return services;
    }
}
