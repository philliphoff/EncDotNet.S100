namespace EncDotNet.S100.Viewer.Routing;

// Received routes, display mode and the check-settings layer (#960, #391).
// A received route is a read-only view of an exchange file: its waypoints,
// legs, info and route values (speed, cross-track) come from the file and
// can't change. Its check settings (draft, squat, required UKC, departure)
// are a local layer over the file's values, so the route can be checked
// without editing it. On an owned route the check settings are simply its
// passage values.
public sealed partial class Route
{
    private RoutePassage? _fileDefaults;
    private Dictionary<RoutePassageField, double?[]>? _fileLegValues;

    /// <summary>
    /// Where a received route came from, or <c>null</c> for an owned route.
    /// </summary>
    public RouteSource? Source { get; private set; }

    /// <summary>
    /// Whether this is a received route: read-only, not persisted in
    /// <c>routes.json</c>, and edited only through its check settings.
    /// Editing it through <see cref="History"/> throws
    /// <see cref="RouteReadOnlyException"/>.
    /// </summary>
    public bool IsReadOnly => Source is not null;

    /// <summary>
    /// How the map draws the route: <see cref="RouteDisplayMode.Planning"/>
    /// (the route renderer) or <see cref="RouteDisplayMode.Native"/> (its
    /// dataset's own portrayal). Owned routes are always in Planning mode.
    /// </summary>
    public RouteDisplayMode DisplayMode { get; private set; } = RouteDisplayMode.Planning;

    /// <summary>
    /// The check settings set locally on a received route; a <c>null</c>
    /// value uses the file's value. Always empty on an owned route, whose
    /// check settings are its <see cref="Passage"/> values.
    /// </summary>
    public RouteCheckSettings LocalCheckSettings { get; } = new();

    /// <summary>
    /// Makes this route a received route read from <paramref name="source"/>.
    /// The route's current passage values and leg overrides become the
    /// file's values, which the check settings fall back to.
    /// </summary>
    /// <param name="source">Where the route came from.</param>
    /// <param name="displayMode">The initial display mode.</param>
    internal void MarkReceived(RouteSource source, RouteDisplayMode displayMode)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        DisplayMode = displayMode;
        _fileDefaults = Passage.Clone();
        _fileLegValues = RoutePassageFields.CheckSettings
            .Where(RoutePassageFields.IsLegOverridable)
            .ToDictionary(f => f, f => _legs.Select(l => l.GetOverride(f)).ToArray());
    }

    /// <summary>Sets how the map draws the route.</summary>
    /// <param name="mode">The display mode.</param>
    /// <returns><c>true</c> when the mode changed.</returns>
    public bool SetDisplayMode(RouteDisplayMode mode)
    {
        if (DisplayMode == mode)
            return false;
        DisplayMode = mode;
        OnChanged();
        return true;
    }

    /// <summary>
    /// The value a check setting has in the file (its passage value), or
    /// <c>null</c> on an owned route or when the file has none.
    /// </summary>
    /// <param name="field">A check-setting field (<see cref="RoutePassageFields.IsCheckSetting"/>).</param>
    /// <returns>The file's route-level value.</returns>
    public double? GetFileValue(RoutePassageField field) => _fileDefaults?.Get(field);

    /// <summary>
    /// The value the file gives leg <paramref name="legIndex"/> for a check
    /// setting, ignoring the local layer: its leg value, else the file's
    /// route-level value. <c>null</c> on an owned route.
    /// </summary>
    /// <param name="legIndex">Index of the leg in <c>[0, Legs.Count)</c>.</param>
    /// <param name="field">A check-setting field.</param>
    /// <returns>The file's value.</returns>
    public double? GetFileLegValue(int legIndex, RoutePassageField field)
    {
        if (!IsReadOnly)
            return null;
        LegAt(legIndex);
        var leg = _fileLegValues!.TryGetValue(field, out var legs) && legIndex < legs.Length ? legs[legIndex] : null;
        return leg ?? _fileDefaults!.Get(field);
    }

    /// <summary>The departure the file gives, or <c>null</c> on an owned route.</summary>
    public DateTimeOffset? FileDeparture => _fileDefaults?.Departure;

    /// <summary>
    /// Whether the check setting <paramref name="field"/> of a received route
    /// comes from the file: no local value is set and the file has one, on
    /// the route or on any leg.
    /// </summary>
    /// <param name="field">A check-setting field.</param>
    /// <returns><c>true</c> when the value shown is the file's.</returns>
    public bool IsCheckSettingFromFile(RoutePassageField field)
    {
        if (!IsReadOnly || LocalCheckSettings.Get(field) is not null)
            return false;
        return _fileDefaults!.Get(field) is not null
            || (_fileLegValues!.TryGetValue(field, out var legs) && legs.Any(v => v is not null));
    }

    /// <summary>Whether a received route's departure comes from the file.</summary>
    public bool IsDepartureFromFile => IsReadOnly && LocalCheckSettings.Departure is null && _fileDefaults!.Departure is not null;

    /// <summary>
    /// Sets one check setting (draft, squat or required UKC). On an owned
    /// route this sets the passage value; on a received route it sets the
    /// local layer, where the value applies to every leg and <c>null</c>
    /// returns to the file's values. A received route's change isn't an
    /// undoable route edit, so call it outside <see cref="History"/>.
    /// </summary>
    /// <param name="field">A check-setting field.</param>
    /// <param name="value">The value, or <c>null</c> to unset it (or use the file's).</param>
    /// <exception cref="ArgumentException"><paramref name="field"/> isn't a check setting.</exception>
    public void SetCheckSetting(RoutePassageField field, double? value)
    {
        if (!RoutePassageFields.IsCheckSetting(field))
            throw new ArgumentException($"{field} is a route value, not a check setting.", nameof(field));
        if (!IsReadOnly)
        {
            SetPassageValue(field, value);
            return;
        }

        ThrowIfNotFinite(value);
        if (LocalCheckSettings.Get(field) == value)
            return;
        LocalCheckSettings.Set(field, value);
        ApplyCheckSetting(field);
        OnChanged();
    }

    /// <summary>
    /// Sets the departure check setting: the passage departure on an owned
    /// route, the local layer on a received one (<c>null</c> returns to the
    /// file's departure).
    /// </summary>
    /// <param name="departure">The departure, or <c>null</c>.</param>
    public void SetCheckDeparture(DateTimeOffset? departure)
    {
        if (!IsReadOnly)
        {
            SetDeparture(departure);
            return;
        }

        if (LocalCheckSettings.Departure == departure)
            return;
        LocalCheckSettings.Departure = departure;
        Passage.Departure = departure ?? _fileDefaults!.Departure;
        OnChanged();
    }

    /// <summary>
    /// Runs an edit of check settings (<see cref="SetCheckSetting"/>,
    /// <see cref="SetCheckDeparture"/>): on an owned route as one undoable
    /// <see cref="History"/> step labelled <paramref name="label"/>, on a
    /// received route directly on its local layer.
    /// </summary>
    /// <param name="label">The undo label on an owned route.</param>
    /// <param name="edit">The edit.</param>
    public void EditCheckSettings(string label, Action edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (IsReadOnly)
            edit();
        else
            History.Execute(label, edit);
    }

    /// <summary>
    /// Opens a gesture of check-setting edits (a departure drag): one undo
    /// step on an owned route; on a received route the edits apply directly
    /// and the scope does nothing.
    /// </summary>
    /// <param name="label">The undo label on an owned route.</param>
    /// <returns>The scope to dispose when the gesture ends.</returns>
    public IDisposable BeginCheckSettingsGesture(string label)
        => IsReadOnly ? NoGesture.Instance : History.BeginGesture(label);

    private sealed class NoGesture : IDisposable
    {
        public static readonly NoGesture Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Replaces a received route's local check settings with
    /// <paramref name="settings"/>'s (for example when they're loaded from
    /// the local store), raising <see cref="Changed"/> once.
    /// </summary>
    /// <param name="settings">The settings to apply.</param>
    internal void ApplyLocalCheckSettings(RouteCheckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsReadOnly)
            return;
        LocalCheckSettings.CopyFrom(settings);
        foreach (var field in RoutePassageFields.CheckSettings)
            ApplyCheckSetting(field);
        Passage.Departure = LocalCheckSettings.Departure ?? _fileDefaults!.Departure;
        OnChanged();
    }

    // Pushes the layered value of one field into the passage and legs: the
    // local value on the route with no leg overrides, else the file's values.
    private void ApplyCheckSetting(RoutePassageField field)
    {
        var local = LocalCheckSettings.Get(field);
        Passage.Set(field, local ?? _fileDefaults!.Get(field));
        if (!_fileLegValues!.TryGetValue(field, out var fileLegs))
            return;
        for (var i = 0; i < _legs.Count; i++)
            _legs[i].SetOverride(field, local is null && i < fileLegs.Length ? fileLegs[i] : null);
    }
}

/// <summary>
/// The check settings of a route: the values used to check it that are the
/// mariner's, not route content (decision 16). Every value is optional.
/// </summary>
public sealed class RouteCheckSettings
{
    /// <summary>Vessel draft, in metres.</summary>
    public double? DraftMeters { get; set; }

    /// <summary>Squat allowance, in metres.</summary>
    public double? SquatMeters { get; set; }

    /// <summary>Required static under-keel clearance, in metres.</summary>
    public double? RequiredUkcMeters { get; set; }

    /// <summary>Planned departure.</summary>
    public DateTimeOffset? Departure { get; set; }

    /// <summary>Whether no value is set.</summary>
    public bool IsEmpty => DraftMeters is null && SquatMeters is null && RequiredUkcMeters is null && Departure is null;

    /// <summary>The value of a numeric check setting.</summary>
    /// <param name="field">A check-setting field.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    public double? Get(RoutePassageField field) => field switch
    {
        RoutePassageField.Draft => DraftMeters,
        RoutePassageField.Squat => SquatMeters,
        RoutePassageField.RequiredUkc => RequiredUkcMeters,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    internal void Set(RoutePassageField field, double? value)
    {
        switch (field)
        {
            case RoutePassageField.Draft: DraftMeters = value; break;
            case RoutePassageField.Squat: SquatMeters = value; break;
            case RoutePassageField.RequiredUkc: RequiredUkcMeters = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    internal void CopyFrom(RouteCheckSettings other)
    {
        DraftMeters = other.DraftMeters;
        SquatMeters = other.SquatMeters;
        RequiredUkcMeters = other.RequiredUkcMeters;
        Departure = other.Departure;
    }

    /// <summary>Returns a copy of these settings.</summary>
    public RouteCheckSettings Clone() => (RouteCheckSettings)MemberwiseClone();
}
