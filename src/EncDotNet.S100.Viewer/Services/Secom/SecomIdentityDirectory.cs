using System.Globalization;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels.Keys;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>One identity a SECOM source can choose (#883), as the picker and the Library show it.</summary>
/// <param name="Id">The reference id (<c>sc-ident:…</c>).</param>
/// <param name="Name">The name the user gave it.</param>
/// <param name="Kind">What it identifies, as the import classified it ("Vessel", "Organisation", …).</param>
/// <param name="Mrn">The MRN it was issued to, if any.</param>
/// <param name="NotAfter">When it ends.</param>
/// <param name="Identity">The identity registered with the trust, or <see langword="null"/> when its key couldn't be opened.</param>
internal sealed record SecomIdentityEntry(string Id, string Name, string Kind, string? Mrn, DateTimeOffset NotAfter, SecomClientIdentity? Identity);

/// <summary>
/// The identities SECOM sources choose from (#883), and the words for a
/// source's identity setting: the picker's closed value and items, and the
/// Library row's identity label. Built over the <see cref="SecomIdentityStore"/>.
/// </summary>
internal sealed class SecomIdentityDirectory
{
    /// <summary>Within this many days, an identity's end is shown ("ends in 12 days").</summary>
    public const int EndingSoonDays = 30;

    private readonly SecomIdentityStore _store;
    private readonly TimeProvider _time;

    public SecomIdentityDirectory(SecomIdentityStore store, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _time = time ?? TimeProvider.System;
        _store.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _store.Trust.IdentitiesChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the identities or the default change (on any thread).</summary>
    public event EventHandler? Changed;

    /// <summary>The trust anchors identities' chains are found through, for hints.</summary>
    public SecomTrustAnchors Anchors => _store.Trust.Anchors;

    /// <summary>The identities a source can choose: the stored ones not revoked, in import order.</summary>
    public IReadOnlyList<SecomIdentityEntry> Entries =>
    [
        .. _store.Identities.Where(r => r.RevokedAt is null).Select(r => new SecomIdentityEntry(
            r.Id, r.DisplayName, r.Kind, r.Mrn, r.NotAfter, _store.Trust.Identities.GetValueOrDefault(r.Id))),
    ];

    /// <summary>True when there is at least one identity to choose.</summary>
    public bool HasIdentities => Entries.Count > 0;

    /// <summary>The default identity, when one is set and stored.</summary>
    public SecomIdentityEntry? Default => _store.Trust.DefaultIdentityId is { } id ? Find(id) : null;

    /// <summary>The entry with <paramref name="id"/>, or <see langword="null"/>.</summary>
    public SecomIdentityEntry? Find(string? id) =>
        id is null ? null : Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The entry <paramref name="setting"/> resolves to: the default for
    /// <c>default</c>, the named one for a reference; <see langword="null"/>
    /// for <c>none</c>, an unset default and a missing reference.
    /// </summary>
    public SecomIdentityEntry? Resolve(string? setting) =>
        SecomIdentitySetting.TryParse(setting ?? SecomIdentitySetting.Default, out var canonical)
            ? canonical switch
            {
                SecomIdentitySetting.Default => Default,
                SecomIdentitySetting.None => null,
                _ => Find(canonical),
            }
            : null;

    /// <summary>The name of the entry with <paramref name="id"/>, or the id itself.</summary>
    public string NameOf(string id) => Find(id)?.Name ?? id;

    /// <summary>
    /// The Library row's identity label (D1): "Default (Bridge PC)" or
    /// "Default (not set)", "None", or the identity's name.
    /// </summary>
    public string RowLabel(string? setting)
    {
        if (SecomIdentitySetting.TryParse(setting ?? SecomIdentitySetting.Default, out var canonical) is false || canonical == SecomIdentitySetting.None)
            return Strings.Secom_Identity_None;
        if (canonical == SecomIdentitySetting.Default)
        {
            return Default is { } chosen
                ? string.Format(CultureInfo.CurrentCulture, Strings.Secom_Identity_RowDefaultFormat, chosen.Name)
                : Strings.Secom_Identity_RowDefaultNotSet;
        }

        return Find(canonical)?.Name ?? string.Format(CultureInfo.CurrentCulture, Strings.Secom_Identity_MissingFormat, canonical);
    }

    /// <summary>The picker's closed name (B1): "None · anonymous", "Default · {name}" or the identity's name.</summary>
    public string ClosedName(string? setting)
    {
        if (!HasIdentities || !SecomIdentitySetting.TryParse(setting ?? SecomIdentitySetting.Default, out var canonical) || canonical == SecomIdentitySetting.None)
            return Strings.Secom_Picker_None;
        if (canonical == SecomIdentitySetting.Default)
            return DefaultName();
        return Find(canonical)?.Name ?? string.Format(CultureInfo.CurrentCulture, Strings.Secom_Identity_MissingFormat, canonical);
    }

    /// <summary>
    /// The picker's closed sub-line (B1): "{Kind} · {instance}[ · ends in N days] · {sc-ident}",
    /// or "No certificate is presented", or "No default identity set".
    /// </summary>
    public string ClosedSub(string? setting)
    {
        if (Resolve(setting) is { } entry)
            return string.Format(CultureInfo.CurrentCulture, Strings.Secom_Picker_ClosedSubFormat, ItemSub(entry), entry.Id);
        return SecomIdentitySetting.TryParse(setting ?? SecomIdentitySetting.Default, out var canonical) && canonical == SecomIdentitySetting.Default && HasIdentities
            ? Strings.Secom_Picker_NoDefault
            : Strings.Secom_Picker_NoCertificate;
    }

    /// <summary>"Default · {name}", or "Default · not set".</summary>
    public string DefaultName() =>
        string.Format(CultureInfo.CurrentCulture, Strings.Secom_Picker_DefaultFormat, Default?.Name ?? Strings.Secom_Picker_DefaultNotSet);

    /// <summary>An identity's sub-line in the picker (B2): "{Kind} · {instance}[ · ends in N days]".</summary>
    public string ItemSub(SecomIdentityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var parts = new List<string> { KindText(entry.Kind) };
        if (InstanceText(entry.Mrn) is { } instance)
            parts.Add(instance);
        if (EndsInDays(entry) is { } days)
            parts.Add(CertificateText.EndsIn(days).ToLower(CultureInfo.CurrentCulture));
        return string.Join(" · ", parts);
    }

    /// <summary>Days until <paramref name="entry"/> ends, when that is within <see cref="EndingSoonDays"/>; otherwise <see langword="null"/>.</summary>
    public int? EndsInDays(SecomIdentityEntry entry)
    {
        var days = CertificateText.DaysUntil(entry.NotAfter, _time.GetUtcNow());
        return days <= EndingSoonDays ? days : null;
    }

    /// <summary>"Vessel", "Organisation", … for a stored kind.</summary>
    public static string KindText(string? kind) => kind switch
    {
        "Vessel" => Strings.Secom_Kind_Vessel,
        "Organisation" => Strings.Secom_Kind_Organisation,
        "Test" => Strings.Secom_Kind_Test,
        "Service" => Strings.Secom_Kind_Service,
        "Person" => Strings.Secom_Kind_Person,
        _ => Strings.Secom_Kind_Device,
    };

    /// <summary>The MCP instance an MRN was issued under, as shown: "MCC", "MCC Test", else the instance as written.</summary>
    public static string? InstanceText(string? mrn) => SecomIdentityHints.Instance(mrn)?.ToLowerInvariant() switch
    {
        null => null,
        "mcc" => Strings.Secom_Instance_Mcc,
        "mcc-test" => Strings.Secom_Instance_MccTest,
        var other => other.ToUpperInvariant(),
    };
}
