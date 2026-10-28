using System.Text.Json.Serialization;
using FolderChat.Application;
using FolderChat.Application.Api;
using FolderChat.Infrastructure;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFolderChatApplication();
builder.Services.AddFolderChatInfrastructure(builder.Configuration);

// The REST API for other apps. Its extra addresses, if any, are added to the ones FolderChat already listens on.
var apiOptions = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
builder.Services.AddOptions<ApiOptions>().Bind(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.AddSingleton<ApiDefaults>(sp => sp.GetRequiredService<IOptions<ApiOptions>>().Value.Defaults);
var apiUrls = ApiOptions.SplitUrls(apiOptions.Urls);
var apiPorts = apiOptions.GetPorts();
if (apiUrls.Length > 0)
{
    var appUrls = ApiOptions.SplitUrls(builder.Configuration[WebHostDefaults.ServerUrlsKey]);
    if (appUrls.Length == 0)
    {
        throw new InvalidOperationException("FolderChat:Api:Urls needs the app's own address in Urls too, such as http://127.0.0.1:5080.");
    }

    builder.WebHost.UseUrls([.. appUrls, .. apiUrls]);
}

builder.Services.AddOpenApi("v1", options =>
{
    options.ShouldInclude = description => description.RelativePath?.StartsWith("api/v1/", StringComparison.Ordinal) == true;
    options.AddDocumentTransformer<ApiDocument>();
});

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(services.GetRequiredService<AppPaths>().LogsDirectory, "folderchat-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

builder.Services
    .AddControllersWithViews(options =>
    {
        // Every POST/PUT/PATCH/DELETE needs the anti-forgery token, so other websites
        // open in the same browser can't call this app's API.
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    })
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "FolderChat.Antiforgery";
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddSingleton<IFolderPicker, SystemFolderPicker>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<FolderChatDbContext>().Database;
    await database.MigrateAsync();

    // Write-ahead logging lets chats search while a folder scan writes. The setting is stored in the file.
    await database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
}

// Outside the exception handler, so each request is logged with the status the browser actually got.
app.UseSerilogRequestLogging(options => options.GetLevel = RequestLogLevel);
app.UseExceptionHandler();
if (apiPorts.Count > 0)
{
    app.UseMiddleware<ApiOnlyAddresses>(apiPorts);
}

app.UseRouting();

app.MapStaticAssets();
app.MapControllers();
app.MapOpenApi("/api/{documentName}/openapi.json");
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

var options = app.Services.GetRequiredService<IOptions<FolderChatOptions>>().Value;
if (options.OpenBrowserOnStart)
{
    app.Lifetime.ApplicationStarted.Register(() => BrowserLauncher.Open(app.Urls.FirstOrDefault()));
}

app.Lifetime.ApplicationStarted.Register(() =>
    app.Logger.LogInformation("FolderChat is ready at {Urls}. Data folder: {DataDirectory}",
        string.Join(", ", app.Urls),
        app.Services.GetRequiredService<AppPaths>().DataDirectory));

app.Run();

// 502 means a provider turned a request down (a rejected key, a stopped server): worth a warning, not an error.
static LogEventLevel RequestLogLevel(HttpContext context, double elapsedMs, Exception? exception) => context.Response.StatusCode switch
{
    _ when exception is not null => LogEventLevel.Error,
    StatusCodes.Status502BadGateway => LogEventLevel.Warning,
    >= 500 => LogEventLevel.Error,
    >= 400 => LogEventLevel.Warning,
    _ => LogEventLevel.Information,
};

/// <summary>Exposed for the integration tests' WebApplicationFactory.</summary>
public partial class Program;
