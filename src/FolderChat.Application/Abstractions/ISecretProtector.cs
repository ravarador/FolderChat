namespace FolderChat.Application.Abstractions;

/// <summary>Encrypts secrets, such as API keys, before they are stored.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
