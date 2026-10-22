namespace FolderChat.Application.Common;

/// <summary>The request is invalid. The message is safe to show to the user.</summary>
public sealed class ValidationFailedException(string message) : Exception(message);

/// <summary>The requested item does not exist.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>A provider rejected a request or could not be reached. The message is safe to show to the user.</summary>
public sealed class ProviderCallFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
