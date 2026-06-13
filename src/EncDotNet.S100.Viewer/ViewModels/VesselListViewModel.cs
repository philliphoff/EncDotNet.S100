using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View-model backing the Vessels activity panel. Lists the live AIS
/// targets published by the <c>"vessel.ais"</c> dynamic feature source,
/// ordered nearest-first relative to the own ship. Each row shows the
/// vessel name, a ship-type pictogram, navigation state, range and
/// bearing from own ship, and — while travelling — the voyage
/// destination. Selecting a row recentres the map on the vessel,
/// preserving the current zoom level.
/// </summary>
/// <remarks>
/// <para>
/// The AIS source's <c>Changed</c> event and the own-ship source's
/// <c>Changed</c> event may fire on background threads. Both handlers do
/// nothing but flip an atomic dirty flag; a 1 Hz <see cref="DispatcherTimer"/>
/// performs all <see cref="ObservableCollection{T}"/> mutation on the UI
/// thread. In unit tests (no Avalonia dispatcher) the handlers refresh
/// synchronously, matching the pattern used by other viewer list panels.
/// </para>
/// <para>
/// Refreshes update existing <see cref="VesselListItem"/> instances in
/// place and reconcile the collection order with <c>Move</c>, so the
/// user's selection (held by reference) survives the nearest-first
/// re-sort as vessels move.
/// </para>
/// <para>
/// Range and bearing are computed relative to the own ship and are only
/// shown when the own-ship overlay is enabled — i.e. when the
/// <c>"ownship"</c> dynamic source is publishing a position feature.
/// When it is off, rows omit the range/bearing line and the list is
/// ordered nearest-first relative to the current map viewport centre
/// (falling back to name ordering only when no laid-out viewport is
/// available). Selecting a vessel recentres the map on it, so the
/// selection naturally floats to the top of the viewport-ordered list.
/// </para>
/// </remarks>
internal sealed class VesselListViewModel : ViewModelBase
{
    /// <summary>
    /// Renderer key identifying the AIS dynamic feature source among the
    /// registered <see cref="IDynamicFeatureSource"/> singletons.
    /// </summary>
    private const string AisRendererKey = "vessel.ais";

    /// <summary>
    /// Renderer key identifying the own-ship dynamic feature source. Its
    /// published feature (if any) is the reference point for range and
    /// bearing; an empty snapshot means the own-ship overlay is off.
    /// </summary>
    private const string OwnShipRendererKey = OwnShipSource.FeatureKind;

    /// <summary>
    /// Speed-over-ground threshold (knots) above which a vessel is
    /// considered to be travelling when no explicit "under way"
    /// navigation status is reported.
    /// </summary>
    private const double TravellingSpeedThresholdKn = 0.2;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IDynamicFeatureSource? _ais;
    private readonly IDynamicFeatureSource? _ownShip;
    private readonly IHelmStatusProvider? _helmStatus;
    private readonly IDynamicFeatureSource? _helmTargetSource;
    private readonly IMapHostAccessor _mapHostAccessor;
    private readonly DispatcherTimer? _timer;
    private readonly Dictionary<string, VesselListItem> _itemsById = new(StringComparer.Ordinal);

    private VesselListItem? _ownShipItem;
    private bool _wasHelmActive;
    private int _dirty;
    private bool _suppressSelectionWrite;

    public VesselListViewModel(
        IEnumerable<IDynamicFeatureSource> sources,
        IMapHostAccessor mapHostAccessor)
        : this(sources, mapHostAccessor, helmStatus: null, helmTargetSource: null)
    {
    }

    /// <param name="sources">All registered dynamic feature sources.</param>
    /// <param name="mapHostAccessor">Accessor for the live map host.</param>
    /// <param name="helmStatus">
    /// Read-only pirate-mode status used to label the own-ship row and gate
    /// the release-helm command. <see langword="null"/> in tests / when no
    /// helm is wired.
    /// </param>
    /// <param name="helmTargetSource">
    /// The <em>raw</em> (undecorated) AIS source — typically
    /// <see cref="ExcludingAisFeatureSource.Inner"/> — used to resolve the
    /// impersonated target's display name, since the decorated source hides
    /// it. <see langword="null"/> when unavailable.
    /// </param>
    public VesselListViewModel(
        IEnumerable<IDynamicFeatureSource> sources,
        IMapHostAccessor mapHostAccessor,
        IHelmStatusProvider? helmStatus,
        IDynamicFeatureSource? helmTargetSource)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(mapHostAccessor);

        _mapHostAccessor = mapHostAccessor;
        _helmStatus = helmStatus;
        _helmTargetSource = helmTargetSource;
        Vessels = new ObservableCollection<VesselListItem>();

        TakeHelmCommand = new RelayCommand(ExecuteTakeHelm, CanTakeHelm);
        ReleaseHelmCommand = new RelayCommand(ExecuteReleaseHelm, CanReleaseHelm);

        // Resolve the dynamic sources by renderer key. There is at most
        // one of each; FirstOrDefault keeps the panel inert (empty, no
        // crash) when a source is disabled, misconfigured, or absent.
        var sourceList = sources.ToArray();
        _ais = sourceList.FirstOrDefault(
            s => string.Equals(s.Metadata.RendererKey, AisRendererKey, StringComparison.Ordinal));
        _ownShip = sourceList.FirstOrDefault(
            s => string.Equals(s.Metadata.RendererKey, OwnShipRendererKey, StringComparison.Ordinal));

        if (_ais is not null)
        {
            _ais.Changed += OnSourceChanged;
        }
        if (_ownShip is not null)
        {
            _ownShip.Changed += OnSourceChanged;
        }

        if (Avalonia.Application.Current is not null)
        {
            _timer = new DispatcherTimer { Interval = RefreshInterval };
            _timer.Tick += (_, _) =>
            {
                if (Interlocked.Exchange(ref _dirty, 0) == 1)
                {
                    Refresh();
                }
            };
            _timer.Start();
        }

        // Surface any targets that already exist before the panel was
        // constructed (the sources are long-lived singletons).
        Refresh();
    }

    /// <summary>Vessels ordered nearest-first.</summary>
    public ObservableCollection<VesselListItem> Vessels { get; }

    private VesselListItem? _selectedVessel;
    /// <summary>
    /// Currently-selected vessel row. Setting it to a non-null value
    /// recentres the map on that vessel at the current zoom level.
    /// </summary>
    public VesselListItem? SelectedVessel
    {
        get => _selectedVessel;
        set
        {
            // While the list is being reconciled (rows moved/inserted to
            // re-sort), the ListBox can transiently clear its selection and
            // write null back through this two-way binding. Ignore those
            // writes so a re-sort never drops the user's selection; the
            // real value is re-asserted once the reconcile completes.
            if (_suppressSelectionWrite)
            {
                return;
            }

            if (SetProperty(ref _selectedVessel, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                TakeHelmCommand.NotifyCanExecuteChanged();
                ReleaseHelmCommand.NotifyCanExecuteChanged();
                if (value is not null)
                {
                    _mapHostAccessor.Current?.CenterOn(value.Latitude, value.Longitude);
                }
            }
        }
    }

    /// <summary>
    /// Whether a vessel is currently selected. Drives the master/detail
    /// split: the properties sub-pane shows the selection's details and
    /// the placeholder otherwise.
    /// </summary>
    public bool HasSelection => _selectedVessel is not null;

    /// <summary>
    /// Takes the helm of the selected AIS target (engages pirate mode).
    /// Enabled only when a non-own-ship row with a known MMSI is selected.
    /// The view-model owns no helm services; it raises
    /// <see cref="TakeHelmRequested"/> so the app can engage the
    /// pirate-mode coordinator.
    /// </summary>
    public RelayCommand TakeHelmCommand { get; }

    /// <summary>
    /// Releases the helm (disengages pirate mode). Enabled only while a
    /// target is being followed. Raises <see cref="ReleaseHelmRequested"/>.
    /// </summary>
    public RelayCommand ReleaseHelmCommand { get; }

    /// <summary>
    /// Raised when <see cref="TakeHelmCommand"/> runs, carrying the MMSI of
    /// the target to impersonate.
    /// </summary>
    public event EventHandler<uint>? TakeHelmRequested;

    /// <summary>
    /// Raised when <see cref="ReleaseHelmCommand"/> runs.
    /// </summary>
    public event EventHandler? ReleaseHelmRequested;

    private bool CanTakeHelm()
        => _helmStatus is not null
            && _selectedVessel is { IsOwnShip: false, Mmsi: not null };

    private void ExecuteTakeHelm()
    {
        if (_selectedVessel is { IsOwnShip: false, Mmsi: { } mmsi })
        {
            TakeHelmRequested?.Invoke(this, mmsi);
        }
    }

    private bool CanReleaseHelm() => _helmStatus?.IsActive == true;

    private void ExecuteReleaseHelm()
    {
        if (CanReleaseHelm())
        {
            ReleaseHelmRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Refreshes the list immediately and, when the own-ship row is
    /// present, selects it. Called by the app right after the pirate-mode
    /// coordinator engages from this panel so the own-ship detail pane (and
    /// the "Release the helm" button) is reachable without the user hunting
    /// for the row. Runs on the UI thread.
    /// </summary>
    internal void HandleHelmEngaged()
    {
        Refresh();
        if (_ownShipItem is not null)
        {
            SelectedVessel = _ownShipItem;
        }
    }

    /// <summary>
    /// Refreshes the list immediately after the pirate-mode coordinator
    /// disengages from this panel so the own-ship row drops its "helming"
    /// label and the command states update. Runs on the UI thread.
    /// </summary>
    internal void HandleHelmDisengaged() => Refresh();

    private bool _isEmpty = true;
    /// <summary>
    /// Whether the list is currently empty. Drives the centred
    /// placeholder (vs. the list) in the view.
    /// </summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    private string _emptyTitle = string.Empty;
    /// <summary>
    /// Bold primary placeholder text shown while <see cref="IsEmpty"/> is
    /// <see langword="true"/>. Distinguishes the AIS overlay being switched
    /// off from it being on but not yet populated.
    /// </summary>
    public string EmptyTitle
    {
        get => _emptyTitle;
        private set => SetProperty(ref _emptyTitle, value);
    }

    private string _emptyDescription = string.Empty;
    /// <summary>
    /// Secondary descriptive placeholder text shown beneath
    /// <see cref="EmptyTitle"/> while <see cref="IsEmpty"/> is
    /// <see langword="true"/>.
    /// </summary>
    public string EmptyDescription
    {
        get => _emptyDescription;
        private set => SetProperty(ref _emptyDescription, value);
    }

    /// <summary>
    /// Whether an AIS overlay is actively configured for this session.
    /// The overlay singleton is fixed at startup, so a
    /// <see cref="DisabledAisFeatureSource"/> (or no AIS source at all)
    /// means the overlay is off and the panel can never populate until
    /// the user enables it and restarts; any other source — including the
    /// zoom-gated <c>DeferredAisFeatureSource</c> — is "active" and merely
    /// awaiting data.
    /// </summary>
    private bool IsAisActive => _ais is not null and not DisabledAisFeatureSource;

    private void OnSourceChanged(object? sender, DynamicFeaturesChanged e) => MarkDirty();

    private void MarkDirty()
    {
        if (_timer is null)
        {
            // Test / headless path: no dispatcher to coalesce against, so
            // refresh inline on the caller's thread.
            Refresh();
            return;
        }
        Interlocked.Exchange(ref _dirty, 1);
    }

    /// <summary>
    /// Rebuilds the vessel rows from the current AIS snapshot and own-ship
    /// fix. Always runs on the UI thread (timer tick / construction) in the
    /// app; synchronously in tests.
    /// </summary>
    internal void Refresh()
    {
        var features = _ais?.CurrentFeatures.ToArray() ?? Array.Empty<DynamicFeature>();
        var ownFeature = ResolveOwnShipFeature();
        var own = ownFeature is { } f ? (f.Coordinates[0].Latitude, f.Coordinates[0].Longitude) : ((double, double)?)null;

        // When the own-ship overlay is on it is the reference point for
        // both range/bearing and list ordering. When it is off, fall back
        // to the current viewport centre so the list is ordered by what the
        // user is actually looking at (rather than by MMSI, whose order is
        // meaningless to the navigator). Either may be null — e.g. no
        // laid-out map yet — in which case ordering falls back to name.
        var sortOrigin = own ?? _mapHostAccessor.Current?.TryGetViewportCenterWgs84();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in features)
        {
            if (feature.GeometryType != GeometryType.Point || feature.Coordinates.Count < 1)
            {
                continue;
            }

            var (lat, lon) = feature.Coordinates[0];
            if (!IsValidLatLon(lat, lon))
            {
                continue;
            }

            seen.Add(feature.Id);

            if (!_itemsById.TryGetValue(feature.Id, out var item))
            {
                item = new VesselListItem { Id = feature.Id };
                _itemsById[feature.Id] = item;
            }

            UpdateItem(item, feature, lat, lon, own, sortOrigin);
        }

        // The own ship (the vessel the user is helming) is shown as a
        // pinned top row whenever the overlay is publishing a fix — even
        // when no AIS targets are present — so it stays selectable and the
        // helm can be released from the list.
        UpdateOwnShipRow(ownFeature, seen);

        RemoveVanished(seen);

        // Reordering the collection (Move/Insert) can make the ListBox drop
        // its visual selection mid-reconcile. Suppress selection write-back
        // during the churn so the VM keeps the selection (held by reference),
        // then force the control to re-sync afterwards.
        var retained = _selectedVessel;
        _suppressSelectionWrite = true;
        try
        {
            Resort();
        }
        finally
        {
            _suppressSelectionWrite = false;
        }

        // Re-assert the retained selection. Avalonia won't re-push a
        // reference-equal value through the SelectedItem binding, so a plain
        // notification leaves the ListBox highlight cleared. Briefly clear
        // and restore the backing field — bypassing the setter so no map
        // recentre fires — which makes the binding push null and then the
        // item back, restoring the highlight. This is synchronous, so
        // HasSelection never visibly flips.
        if (retained is not null && ReferenceEquals(_selectedVessel, retained))
        {
            _selectedVessel = null;
            OnPropertyChanged(nameof(SelectedVessel));
            _selectedVessel = retained;
            OnPropertyChanged(nameof(SelectedVessel));
        }

        // When pirate mode has just engaged, the followed AIS target is now
        // hidden (its row removed, dropping any selection on it). Auto-select
        // the own-ship row so the user keeps a detail pane — and the
        // "Release the helm" button — without hunting for it.
        var helmActive = _helmStatus?.IsActive == true;
        if (helmActive && !_wasHelmActive && _ownShipItem is not null && _selectedVessel is null)
        {
            SelectedVessel = _ownShipItem;
        }
        _wasHelmActive = helmActive;

        TakeHelmCommand.NotifyCanExecuteChanged();
        ReleaseHelmCommand.NotifyCanExecuteChanged();

        UpdateEmptyState();
    }

    /// <summary>
    /// Returns the own-ship feature currently published by the own-ship
    /// source (a single point with a valid position), or
    /// <see langword="null"/> when the overlay is off / has no fix yet.
    /// "Off" is signalled by the source publishing no feature —
    /// <c>OwnShipSource</c> only empties its snapshot when disabled, never
    /// mid-update, so this is a stable enabled/visible signal.
    /// </summary>
    private DynamicFeature? ResolveOwnShipFeature()
    {
        if (_ownShip is null)
        {
            return null;
        }

        foreach (var feature in _ownShip.CurrentFeatures)
        {
            if (feature.GeometryType != GeometryType.Point || feature.Coordinates.Count < 1)
            {
                continue;
            }

            var (lat, lon) = feature.Coordinates[0];
            if (IsValidLatLon(lat, lon))
            {
                return feature;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates / updates / removes the pinned own-ship row from the
    /// <paramref name="ownFeature"/> fix. Adds its id to
    /// <paramref name="seen"/> so the shared <see cref="RemoveVanished"/>
    /// path drops it (and clears any selection on it) when the overlay
    /// turns off — no duplicated removal logic. Also resolves the
    /// "helming / waiting" label from the pirate-mode status.
    /// </summary>
    private void UpdateOwnShipRow(DynamicFeature? ownFeature, HashSet<string> seen)
    {
        if (ownFeature is null)
        {
            _ownShipItem = null;
            return;
        }

        seen.Add(OwnShipRendererKey);

        if (!_itemsById.TryGetValue(OwnShipRendererKey, out var item))
        {
            item = new VesselListItem { Id = OwnShipRendererKey };
            _itemsById[OwnShipRendererKey] = item;
        }

        _ownShipItem = item;

        var (lat, lon) = ownFeature.Coordinates[0];
        item.IsOwnShip = true;
        item.Mmsi = null;
        item.Latitude = lat;
        item.Longitude = lon;
        // The own ship is the reference point, so it carries no range or
        // bearing and always sorts first (see Resort).
        item.SortDistanceMetres = null;
        item.HasRangeBearing = false;
        item.DistanceMetres = null;
        item.DistanceText = string.Empty;
        item.BearingText = string.Empty;
        item.RangeBearingText = string.Empty;

        item.Name = Strings.Vessels_OwnShip_Name;
        item.ShipTypeClass = AisShipTypeClass.Unknown;
        item.ShipTypeText = string.Empty;

        var sogKn = ownFeature.Motion?.SpeedOverGroundKn;
        UpdateMotion(item, ownFeature, sogKn);
        UpdateOwnShipHelmLabel(item);

        item.StateText = item.HasHelmingText ? item.HelmingText! : Strings.Vessels_OwnShip_Name;
        item.HeaderSubtitle = item.HasHelmingText ? item.HelmingText! : Strings.Vessels_OwnShip_Name;

        // Identity / voyage / dimensions are not meaningful for the
        // simulated own ship; clear any stale flags.
        item.MmsiText = string.Empty;
        item.HasCallSign = false;
        item.HasImo = false;
        item.HasVoyage = false;
        item.HasDestination = false;
        item.HasEta = false;
        item.HasDimensionsSection = false;
        item.HasDimensions = false;
        item.HasDraught = false;
    }

    /// <summary>
    /// Sets the own-ship row's "Helming &lt;target&gt;" / "Waiting for
    /// &lt;target&gt;" subtitle from the pirate-mode status. Distinguishes
    /// a fix-adopted follow (helming) from an armed-but-waiting follow.
    /// </summary>
    private void UpdateOwnShipHelmLabel(VesselListItem item)
    {
        if (_helmStatus is not { IsActive: true } status || status.FollowedMmsi is not { } mmsi)
        {
            item.IsHelming = false;
            item.HelmingText = null;
            item.HasHelmingText = false;
            return;
        }

        var targetLabel = ResolveFollowedTargetLabel(mmsi);
        var helming = status.LastFixUtc is not null;
        item.IsHelming = helming;
        item.HelmingText = string.Format(
            CultureInfo.CurrentCulture,
            helming ? Strings.Vessels_OwnShip_HelmingFormat : Strings.Vessels_OwnShip_WaitingFormat,
            targetLabel);
        item.HasHelmingText = true;
    }

    /// <summary>
    /// Resolves the impersonated target's display name from the raw AIS
    /// feed (the decorated source hides it), falling back to the MMSI.
    /// </summary>
    private string ResolveFollowedTargetLabel(uint mmsi)
    {
        var mmsiText = mmsi.ToString(CultureInfo.InvariantCulture);
        if (_helmTargetSource is null)
        {
            return mmsiText;
        }

        var featureId = AisDynamicFeatureSource.FeatureIdForMmsi(mmsi);
        foreach (var feature in _helmTargetSource.CurrentFeatures)
        {
            if (string.Equals(feature.Id, featureId, StringComparison.Ordinal))
            {
                return GetString(feature, "vesselName") ?? mmsiText;
            }
        }

        return mmsiText;
    }

    private void UpdateItem(
        VesselListItem item,
        DynamicFeature feature,
        double lat,
        double lon,
        (double Latitude, double Longitude)? own,
        (double Latitude, double Longitude)? sortOrigin)
    {
        item.Latitude = lat;
        item.Longitude = lon;
        item.SortDistanceMetres = sortOrigin is { } origin
            ? VesselGeoMath.DistanceMetres(origin.Latitude, origin.Longitude, lat, lon)
            : null;
        item.Name = ResolveName(feature);
        item.ShipTypeClass = GetAttribute<AisShipTypeClass>(feature, "shipTypeClass")
            ?? AisShipTypeClass.Unknown;
        item.ShipTypeText = ResolveShipType(item.ShipTypeClass);

        var navStatus = GetAttribute<AisNavigationStatus>(feature, "navigationStatus");
        var sogKn = feature.Motion?.SpeedOverGroundKn;
        item.StateText = ResolveState(navStatus, sogKn);
        item.HeaderSubtitle = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Vessels_HeaderSubtitleFormat,
            item.ShipTypeText,
            item.StateText);

        UpdateIdentity(item, feature);
        UpdateMotion(item, feature, sogKn);
        UpdateVoyage(item, feature);
        UpdateDimensions(item, feature);
        UpdateRangeBearing(item, lat, lon, own);
    }

    private static void UpdateIdentity(VesselListItem item, DynamicFeature feature)
    {
        if (feature.Attributes.TryGetValue("mmsi", out var mmsiObj) && mmsiObj is uint mmsi)
        {
            item.Mmsi = mmsi;
            item.MmsiText = mmsi.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            item.Mmsi = null;
            item.MmsiText = string.Empty;
        }

        var callSign = GetString(feature, "callSign");
        item.CallSign = callSign;
        item.HasCallSign = callSign is not null;

        var imo = GetAttribute<uint>(feature, "imoNumber");
        item.HasImo = imo is { } imoValue && imoValue != 0;
        item.ImoText = item.HasImo ? imo!.Value.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static void UpdateMotion(VesselListItem item, DynamicFeature feature, double? sogKn)
    {
        var heading = feature.Motion?.HeadingDeg;
        item.HasHeading = heading is { } h && !double.IsNaN(h);
        item.HeadingText = item.HasHeading
            ? string.Format(CultureInfo.CurrentCulture, Strings.Vessels_BearingFormat, NormaliseDegrees(heading!.Value))
            : null;

        var course = feature.Motion?.CourseOverGroundDeg;
        item.HasCourse = course is { } c && !double.IsNaN(c);
        item.CourseText = item.HasCourse
            ? string.Format(CultureInfo.CurrentCulture, Strings.Vessels_BearingFormat, NormaliseDegrees(course!.Value))
            : null;

        item.HasSpeed = sogKn is { } s && !double.IsNaN(s);
        item.SpeedText = item.HasSpeed
            ? string.Format(CultureInfo.CurrentCulture, Strings.Vessels_SpeedFormat, sogKn!.Value)
            : null;

        var rot = GetAttribute<double>(feature, "rateOfTurnDegPerMin");
        item.HasRateOfTurn = rot is { } r && !double.IsNaN(r);
        item.RateOfTurnText = item.HasRateOfTurn
            ? string.Format(CultureInfo.CurrentCulture, Strings.Vessels_RateOfTurnFormat, rot!.Value)
            : null;

        item.HasMotion = item.HasHeading || item.HasCourse || item.HasSpeed || item.HasRateOfTurn;
    }

    private static void UpdateVoyage(VesselListItem item, DynamicFeature feature)
    {
        var destination = GetString(feature, "destination");
        item.DestinationText = destination;
        item.HasDestination = destination is not null;

        var eta = GetAttribute<DateTimeOffset>(feature, "eta");
        item.HasEta = eta is not null;
        item.EtaText = eta is { } etaValue
            ? string.Format(CultureInfo.CurrentCulture, Strings.Vessels_EtaFormat, etaValue.UtcDateTime)
            : null;

        item.HasVoyage = item.HasDestination || item.HasEta;
    }

    private static void UpdateDimensions(VesselListItem item, DynamicFeature feature)
    {
        var geometry = feature.VesselGeometry;
        if (geometry is not null && geometry.LengthMetres > 0.0 && geometry.BeamMetres > 0.0)
        {
            item.HasDimensions = true;
            item.DimensionsText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Vessels_DimensionsFormat,
                geometry.LengthMetres,
                geometry.BeamMetres);
        }
        else
        {
            item.HasDimensions = false;
            item.DimensionsText = null;
        }

        var draught = GetAttribute<double>(feature, "draughtMetres");
        item.HasDraught = draught is { } d && !double.IsNaN(d) && d > 0.0;
        item.DraughtText = item.HasDraught
            ? string.Format(CultureInfo.CurrentCulture, Strings.Vessels_DraughtFormat, draught!.Value)
            : null;

        item.HasDimensionsSection = item.HasDimensions || item.HasDraught;
    }

    private static void UpdateRangeBearing(
        VesselListItem item,
        double lat,
        double lon,
        (double Latitude, double Longitude)? own)
    {
        if (own is { } ownPos)
        {
            var distance = VesselGeoMath.DistanceMetres(ownPos.Latitude, ownPos.Longitude, lat, lon);
            var bearing = VesselGeoMath.InitialBearingDegrees(ownPos.Latitude, ownPos.Longitude, lat, lon);
            item.DistanceMetres = distance;
            item.DistanceText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Vessels_DistanceFormat,
                distance / VesselGeoMath.MetresPerNauticalMile);
            item.BearingText = string.Format(
                CultureInfo.CurrentCulture, Strings.Vessels_BearingFormat, NormaliseDegrees(bearing));
            item.RangeBearingText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Vessels_DistanceBearingFormat,
                item.DistanceText,
                item.BearingText);
            item.HasRangeBearing = true;
        }
        else
        {
            // Own-ship overlay is off — range/bearing have no reference
            // point, so hide them rather than showing placeholder dashes.
            item.DistanceMetres = null;
            item.DistanceText = string.Empty;
            item.BearingText = string.Empty;
            item.RangeBearingText = string.Empty;
            item.HasRangeBearing = false;
        }
    }

    /// <summary>
    /// Rounds a compass value to whole degrees and folds 360 back to 0 so
    /// the UI never shows "360°".
    /// </summary>
    private static long NormaliseDegrees(double degrees) => ((long)Math.Round(degrees)) % 360;

    private void RemoveVanished(HashSet<string> seen)
    {
        if (_itemsById.Count == seen.Count)
        {
            return;
        }

        var stale = _itemsById.Keys.Where(id => !seen.Contains(id)).ToArray();
        foreach (var id in stale)
        {
            var removed = _itemsById[id];
            _itemsById.Remove(id);
            if (ReferenceEquals(removed, _ownShipItem))
            {
                _ownShipItem = null;
            }
            if (ReferenceEquals(removed, _selectedVessel))
            {
                // Clear via the field (not the setter) so dropping a stale
                // selection never triggers a map recentre.
                _selectedVessel = null;
                OnPropertyChanged(nameof(SelectedVessel));
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>
    /// Reconciles <see cref="Vessels"/> with the desired ordering of
    /// <see cref="_itemsById"/> — the own-ship row pinned first, then AIS
    /// targets nearest-first (by <see cref="VesselListItem.SortDistanceMetres"/>,
    /// i.e. distance from the own ship or, when it is off, the viewport
    /// centre) — moving existing rows rather than replacing them so item
    /// identity (and thus selection) is preserved.
    /// </summary>
    private void Resort()
    {
        var ordered = _itemsById.Values
            .OrderByDescending(v => v.IsOwnShip)
            .ThenBy(v => v.SortDistanceMetres ?? double.PositiveInfinity)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(v => v.Id, StringComparer.Ordinal)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var target = ordered[i];
            if (i >= Vessels.Count)
            {
                Vessels.Add(target);
                continue;
            }

            if (!ReferenceEquals(Vessels[i], target))
            {
                var currentIndex = Vessels.IndexOf(target);
                if (currentIndex >= 0)
                {
                    Vessels.Move(currentIndex, i);
                }
                else
                {
                    Vessels.Insert(i, target);
                }
            }
        }

        while (Vessels.Count > ordered.Count)
        {
            Vessels.RemoveAt(Vessels.Count - 1);
        }
    }

    private void UpdateEmptyState()
    {
        var empty = Vessels.Count == 0;
        IsEmpty = empty;
        if (empty)
        {
            EmptyTitle = IsAisActive
                ? Strings.Vessels_Empty_NoDataTitle
                : Strings.Vessels_Empty_DisabledTitle;
            EmptyDescription = IsAisActive
                ? Strings.Vessels_Empty_NoDataDescription
                : Strings.Vessels_Empty_DisabledDescription;
        }
    }

    private static string ResolveName(DynamicFeature feature)
    {
        var name = GetString(feature, "vesselName");
        if (name is not null)
        {
            return name;
        }

        if (feature.Attributes.TryGetValue("mmsi", out var mmsiObj) && mmsiObj is uint mmsi)
        {
            return mmsi.ToString(CultureInfo.InvariantCulture);
        }

        return Strings.Vessels_Unnamed;
    }

    private static string ResolveState(AisNavigationStatus? navStatus, double? sogKn)
    {
        if (navStatus is { } status)
        {
            switch (status)
            {
                case AisNavigationStatus.UnderWayUsingEngine:
                    return Strings.Vessels_State_UnderWayUsingEngine;
                case AisNavigationStatus.AtAnchor:
                    return Strings.Vessels_State_AtAnchor;
                case AisNavigationStatus.NotUnderCommand:
                    return Strings.Vessels_State_NotUnderCommand;
                case AisNavigationStatus.RestrictedManoeuvrability:
                    return Strings.Vessels_State_RestrictedManoeuvrability;
                case AisNavigationStatus.ConstrainedByDraught:
                    return Strings.Vessels_State_ConstrainedByDraught;
                case AisNavigationStatus.Moored:
                    return Strings.Vessels_State_Moored;
                case AisNavigationStatus.Aground:
                    return Strings.Vessels_State_Aground;
                case AisNavigationStatus.EngagedInFishing:
                    return Strings.Vessels_State_EngagedInFishing;
                case AisNavigationStatus.UnderWaySailing:
                    return Strings.Vessels_State_UnderWaySailing;
                case AisNavigationStatus.AisSart:
                    return Strings.Vessels_State_AisSart;
            }
        }

        // No (meaningful) navigation status: derive from speed.
        if (sogKn is { } sog)
        {
            return sog > TravellingSpeedThresholdKn
                ? Strings.Vessels_State_Moving
                : Strings.Vessels_State_Stopped;
        }

        return Strings.Vessels_State_Unknown;
    }

    private static string ResolveShipType(AisShipTypeClass shipTypeClass) => shipTypeClass switch
    {
        AisShipTypeClass.Cargo => Strings.Vessels_ShipType_Cargo,
        AisShipTypeClass.Tanker => Strings.Vessels_ShipType_Tanker,
        AisShipTypeClass.Passenger => Strings.Vessels_ShipType_Passenger,
        AisShipTypeClass.HighSpeedCraft => Strings.Vessels_ShipType_HighSpeedCraft,
        AisShipTypeClass.Pleasure => Strings.Vessels_ShipType_Pleasure,
        AisShipTypeClass.Fishing => Strings.Vessels_ShipType_Fishing,
        AisShipTypeClass.Tug => Strings.Vessels_ShipType_Tug,
        AisShipTypeClass.SearchAndRescue => Strings.Vessels_ShipType_SearchAndRescue,
        AisShipTypeClass.LawEnforcement => Strings.Vessels_ShipType_LawEnforcement,
        AisShipTypeClass.Military => Strings.Vessels_ShipType_Military,
        AisShipTypeClass.Sailing => Strings.Vessels_ShipType_Sailing,
        AisShipTypeClass.PilotVessel => Strings.Vessels_ShipType_PilotVessel,
        AisShipTypeClass.Other => Strings.Vessels_ShipType_Other,
        _ => Strings.Vessels_ShipType_Unknown,
    };

    private static bool IsValidLatLon(double lat, double lon)
        => !double.IsNaN(lat) && !double.IsNaN(lon)
            && !double.IsInfinity(lat) && !double.IsInfinity(lon)
            && lat >= -90.0 && lat <= 90.0
            && lon >= -180.0 && lon <= 180.0;

    private static string? GetString(DynamicFeature feature, string key)
    {
        if (feature.Attributes.TryGetValue(key, out var value)
            && value is string s
            && !string.IsNullOrWhiteSpace(s))
        {
            return s.Trim();
        }
        return null;
    }

    private static T? GetAttribute<T>(DynamicFeature feature, string key) where T : struct
    {
        if (feature.Attributes.TryGetValue(key, out var value) && value is T typed)
        {
            return typed;
        }
        return null;
    }
}
