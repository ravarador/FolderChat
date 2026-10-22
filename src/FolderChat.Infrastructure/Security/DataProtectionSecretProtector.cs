using FolderChat.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace FolderChat.Infrastructure.Security;

/// <summary>
/// Encrypts API keys with ASP.NET Core Data Protection. The key ring lives in the data folder;
/// on Windows it is itself encrypted with DPAPI, so it only works for the current Windows user.
/// </summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("FolderChat.ProviderApiKeys.v1");

    public string Protect(string plaintext) => protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => protector.Unprotect(protectedValue);
}
