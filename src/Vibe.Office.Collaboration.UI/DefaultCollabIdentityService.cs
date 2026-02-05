namespace Vibe.Office.Collaboration.UI;

/// <summary>
/// Default identity provider that uses environment information.
/// </summary>
public sealed class DefaultCollabIdentityService : ICollabIdentityService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultCollabIdentityService"/> class.
    /// </summary>
    /// <param name="displayName">Optional display name override.</param>
    public DefaultCollabIdentityService(string? displayName = null)
    {
        UserId = Guid.NewGuid();
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? Environment.UserName
            : displayName;
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = "User";
        }

        Color = CollabColorPalette.ResolveColor(UserId);
    }

    /// <inheritdoc />
    public Guid UserId { get; }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public string Color { get; }
}
