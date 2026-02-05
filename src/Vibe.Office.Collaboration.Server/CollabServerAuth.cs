using Microsoft.AspNetCore.Http;
using Vibe.Office.Collaboration.Protocol;

namespace Vibe.Office.Collaboration.Server;

/// <summary>
/// Authorization hook for collaboration server connections.
/// </summary>
public interface ICollabServerAuthenticator
{
    ValueTask<CollabAuthResult> AuthorizeHelloAsync(CollabServerHelloContext context);

    ValueTask<CollabAuthResult> AuthorizeJoinAsync(CollabServerJoinContext context);
}

public sealed record CollabAuthResult(bool IsAllowed, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static readonly CollabAuthResult Allow = new(true);
    public static CollabAuthResult Deny(string code, string message) => new(false, code, message);
}

public sealed record CollabServerHelloContext(HttpContext HttpContext, CollabEnvelope<HelloMessage> Envelope);

public sealed record CollabServerJoinContext(HttpContext HttpContext, CollabEnvelope<JoinMessage> Envelope);

internal sealed class AllowAllAuthenticator : ICollabServerAuthenticator
{
    public ValueTask<CollabAuthResult> AuthorizeHelloAsync(CollabServerHelloContext context)
    {
        return ValueTask.FromResult(CollabAuthResult.Allow);
    }

    public ValueTask<CollabAuthResult> AuthorizeJoinAsync(CollabServerJoinContext context)
    {
        return ValueTask.FromResult(CollabAuthResult.Allow);
    }
}
