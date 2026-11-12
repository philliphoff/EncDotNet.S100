using System.Globalization;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Navigational warnings panel's route scope (#959 E3, #1397): while
/// Check route is on, each part's relation to the checked route, and the
/// "All N · On {route} N" switch that lists only the parts on or near it.
/// Without route findings (a host with no route check) the scope never shows.
/// </summary>
internal sealed class RouteScopeViewModel : ViewModelBase, IDisposable
{
    private readonly NavigationalWarningsService _warnings;
    private readonly RouteFindingsService? _findings;
    private NavigationalWarningsScope _scope = NavigationalWarningsScope.All;
    private Dictionary<(string DatasetId, string FeatureId), RouteFinding> _onRoute = [];
    private string _onRouteKey = string.Empty;

    /// <param name="warnings">The listed warnings, counted on the route.</param>
    /// <param name="findings">The checked route's findings; <see langword="null"/> where the host has no route check.</param>
    public RouteScopeViewModel(NavigationalWarningsService warnings, RouteFindingsService? findings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        _warnings = warnings;
        _findings = findings;
        if (_findings is not null)
            _findings.Changed += OnFindingsChanged;
        Update(force: true, raise: false);
    }

    /// <summary>Raised when what the scope shows of the route changed: the panel lists again, and open Warning cards update their route row.</summary>
    public event EventHandler? RouteChanged;

    /// <summary>Raised when the user switched the scope: the panel lists again.</summary>
    public event EventHandler? ScopeSwitched;

    /// <summary>Whether the scope shows: Check route is on.</summary>
    public bool IsRouteScopeAvailable => _findings?.IsChecking == true;

    /// <summary>Which warnings are listed; always <see cref="NavigationalWarningsScope.All"/> while Check route is off.</summary>
    public NavigationalWarningsScope Scope
    {
        get => IsRouteScopeAvailable ? _scope : NavigationalWarningsScope.All;
        set
        {
            if (_scope == value)
                return;
            _scope = value;
            RaiseScopeChanged();
            ScopeSwitched?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether every part is listed (the scope's "All N").</summary>
    public bool IsAllScope
    {
        get => Scope == NavigationalWarningsScope.All;
        set { if (value) Scope = NavigationalWarningsScope.All; }
    }

    /// <summary>Whether only the parts on or near the checked route are listed (the scope's "On {route} N").</summary>
    public bool IsRouteScope
    {
        get => Scope == NavigationalWarningsScope.Route;
        set { if (value) Scope = NavigationalWarningsScope.Route; }
    }

    /// <summary>The checked route's name, or <c>null</c> while Check route is off.</summary>
    public string? RouteName => IsRouteScopeAvailable ? _findings!.CheckedRouteName : null;

    /// <summary>The checked route's departure, when it has one.</summary>
    public DateTimeOffset? Departure => _findings?.Departure;

    /// <summary>Number of parts with a finding on the checked route.</summary>
    public int OnRouteCount => IsRouteScopeAvailable ? _warnings.Warnings.Count(w => _onRoute.ContainsKey(Key(w))) : 0;

    /// <summary>"All N".</summary>
    public string AllScopeText => string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_ScopeAllFormat, _warnings.Warnings.Count);

    /// <summary>"On {route} N".</summary>
    public string RouteScopeText => string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_ScopeRouteFormat, RouteName, OnRouteCount);

    /// <summary>Whether the route scope lists nothing: no warning is on or near the route.</summary>
    public bool IsNoneOnRouteState => IsRouteScope && OnRouteCount == 0;

    /// <summary>The checked route's finding for a part, or <c>null</c> when it's off the route or Check route is off.</summary>
    public RouteFinding? RouteFindingFor(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return IsRouteScopeAvailable && _onRoute.TryGetValue(Key(entry), out var finding) ? finding : null;
    }

    /// <summary>Selects <paramref name="finding"/> in the Findings list and brings the list forward.</summary>
    /// <returns>Whether it opened.</returns>
    public bool OpenRouteFinding(RouteFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return IsRouteScopeAvailable && _findings!.Open(finding);
    }

    /// <summary>Lists every part again, as opening a part the route scope hides does; the caller lists again.</summary>
    internal void ShowAll()
    {
        if (!IsRouteScope)
            return;
        _scope = NavigationalWarningsScope.All;
        RaiseScopeChanged();
    }

    /// <summary>The counts change with the listed warnings.</summary>
    internal void RaiseCounts()
    {
        OnPropertyChanged(nameof(OnRouteCount));
        OnPropertyChanged(nameof(AllScopeText));
        OnPropertyChanged(nameof(RouteScopeText));
        OnPropertyChanged(nameof(IsNoneOnRouteState));
    }

    public void Dispose()
    {
        if (_findings is not null)
            _findings.Changed -= OnFindingsChanged;
    }

    private static (string, string) Key(NavigationalWarningEntry entry) => (entry.DatasetId, entry.FeatureRef);

    private void OnFindingsChanged(object? sender, EventArgs e) => Update(force: false, raise: true);

    // The findings service raises Changed for selections and view changes
    // too; tell the panel only when what it shows of the route changes.
    private void Update(bool force, bool raise)
    {
        var findings = _findings?.IsChecking == true ? _findings.Findings : [];
        var onRoute = new Dictionary<(string, string), RouteFinding>();
        foreach (var finding in findings)
        {
            if (finding is { Category: RouteFindingCategory.Warning, Details: WarningDetails, Feature: { } feature })
                onRoute.TryAdd((feature.DatasetId, feature.FeatureId), finding);
        }

        var key = string.Join(
            "|",
            onRoute.Values.Select(f => f.Details is WarningDetails w ? $"{f.Id}:{w.Relation}:{w.Status}:{w.From:O}:{w.AtEta}" : f.Id)
                .Prepend($"{IsRouteScopeAvailable}:{RouteName}:{_findings?.Departure:O}"));
        if (!force && key == _onRouteKey)
            return;
        _onRouteKey = key;
        _onRoute = onRoute;
        RaiseScopeChanged();
        if (raise)
            RouteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseScopeChanged()
    {
        OnPropertyChanged(nameof(IsRouteScopeAvailable));
        OnPropertyChanged(nameof(Scope));
        OnPropertyChanged(nameof(IsAllScope));
        OnPropertyChanged(nameof(IsRouteScope));
        OnPropertyChanged(nameof(RouteName));
    }
}
