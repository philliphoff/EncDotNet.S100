namespace EncDotNet.S100.Cli.Web.Shell;

/// <summary>A choice in a <see cref="Segmented"/> control.</summary>
/// <param name="Value">The value the control reports.</param>
/// <param name="Label">The text.</param>
/// <param name="Icon">An <see cref="Icon"/> name, or <see langword="null"/>.</param>
public sealed record SegmentedOption(string Value, string Label, string? Icon = null);
