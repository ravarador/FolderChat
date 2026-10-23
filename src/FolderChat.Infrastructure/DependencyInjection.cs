using FolderChat.Application.Abstractions;
using FolderChat.Infrastructure.AI;
using FolderChat.Infrastructure.Documents;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FolderChat.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers storage, encryption, the provider adapters and the readers for attached files.</summary>
    public static IServiceCollection AddFolderChatInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FolderChatOptions>().Bind(configuration.GetSection(FolderChatOptions.SectionName));

        // Resolved when first needed, so tests and command-line overrides of the data folder always apply.
        services.AddSingleton(sp => AppPaths.FromOptions(sp.GetRequiredService<IOptions<FolderChatOptions>>().Value));

        services.AddDbContext<FolderChatDbContext>((sp, db) =>
            db.UseSqlite($"Data Source={sp.GetRequiredService<AppPaths>().DatabasePath}"));
        services.AddScoped<IProviderStore, EfProviderStore>();
        services.AddScoped<IConversationStore, EfConversationStore>();
        services.AddScoped<IAttachmentStore, EfAttachmentStore>();

        services.AddDataProtection().SetApplicationName("FolderChat");
        services.AddOptions<KeyManagementOptions>()
            .Configure<AppPaths, ILoggerFactory>((options, paths, loggerFactory) =>
            {
                options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(paths.KeysDirectory), loggerFactory);
                if (OperatingSystem.IsWindows())
                {
                    options.XmlEncryptor = new DpapiXmlEncryptor(protectToLocalMachine: false, loggerFactory);
                }
            });
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // The SDKs enforce their own timeouts, so HttpClient's 100-second default would only get in the way.
        services.AddHttpClient(HttpClientNames.Anthropic, http => http.Timeout = Timeout.InfiniteTimeSpan);
        services.AddHttpClient(HttpClientNames.OpenAI, http => http.Timeout = Timeout.InfiniteTimeSpan);
        services.AddTransient<ErrorBodyHandler>();
        services.AddHttpClient(HttpClientNames.Ollama, http => http.Timeout = ProviderClients.RequestTimeout)
            .AddHttpMessageHandler<ErrorBodyHandler>();

        services.AddSingleton<ProviderClients>();
        services.AddSingleton<IProviderErrorDescriber, ProviderErrorDescriber>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IModelCatalog, ModelCatalog>();

        services.AddSingleton<ITextExtractor, TextExtractor>();
        return services;
    }
}
