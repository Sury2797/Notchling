namespace Notch.Core;

public enum ConnectionState { NotChecked, Checking, Ready, Connected, NeedsSetup, Unavailable, Failed }

/// <summary>Read-only connection health. Configured is never treated as a successful provider read.</summary>
public sealed record ToolConnectionStatus(string Id, string Name, ConnectionState State, string Detail, DateTimeOffset? CheckedAt = null);
