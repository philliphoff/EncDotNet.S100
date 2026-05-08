namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Combo-box item for the National Language picker. <see cref="Code"/> is the
/// stored ISO 639-2/B value (or empty for "system"); <see cref="DisplayName"/>
/// is the localised label shown to the user.
/// </summary>
internal sealed record LanguageOption(string Code, string DisplayName);
