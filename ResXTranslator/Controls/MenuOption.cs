namespace ResXTranslator.Controls;

/// <summary>
/// One row in a <see cref="MenuButton"/>'s native pull-down.
/// </summary>
/// <param name="Title">The row's label.</param>
/// <param name="Tag">Caller payload; <see langword="null"/> is a legitimate value
/// and is how the "All languages" row carries "no single selection".</param>
/// <param name="Subtitle">Secondary line, shown where the platform supports one.</param>
/// <param name="IsEnabled">A disabled row still renders, which is the point: an
/// already-translated language should be visible and unpickable rather than absent.</param>
sealed record MenuOption(
    string Title,
    object? Tag = null,
    string? Subtitle = null,
    bool IsEnabled = true);
