namespace EncDotNet.S100.Cli.Web.Shell;

/// <summary>An item of the rail (or the phone sheet's tabs).</summary>
/// <param name="Panel">The panel it opens.</param>
/// <param name="Label">The label under the icon, and the panel's title.</param>
/// <param name="Icon">The <see cref="Icon"/> name.</param>
/// <param name="Badge">A red count on the icon (the datasets not served, D31); 0 for none.</param>
public sealed record RailItem(ServePanel Panel, string Label, string Icon, int Badge = 0);
