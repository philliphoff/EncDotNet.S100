using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Library;

/// <summary>A SECOM source that uses an identity (#883).</summary>
/// <param name="CollectionId">The source's collection.</param>
/// <param name="Source">The source.</param>
/// <param name="ViaDefault">True when it follows the default identity; false when its setting names the identity.</param>
public sealed record SecomIdentityUser(Guid CollectionId, LibrarySource Source, bool ViaDefault)
{
    /// <summary>The source's definition.</summary>
    public SecomSource Definition => (SecomSource)Source.Definition;
}

/// <summary>
/// Which SECOM sources use which identity (#883): for re-reading them when it
/// changes, the Identities tab's "Used by" list, and what removing it does.
/// </summary>
public static class SecomIdentityUsage
{
    /// <summary>
    /// The sources that resolve to <paramref name="id"/>: those whose setting
    /// names it, and, when it is <paramref name="defaultId"/>, those that
    /// follow the default. Session catalogues hold no SECOM sources.
    /// </summary>
    public static IReadOnlyList<SecomIdentityUser> UsersOf(IEnumerable<LibraryCollection> collections, string id, string? defaultId)
    {
        ArgumentNullException.ThrowIfNull(collections);
        ArgumentException.ThrowIfNullOrEmpty(id);
        var isDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
        return
        [
            .. Secom(collections)
                .Select(u => (u.CollectionId, u.Source, Setting: u.Definition.Identity))
                .Where(u => string.Equals(u.Setting, id, StringComparison.OrdinalIgnoreCase) || (isDefault && FollowsDefault(u.Setting)))
                .Select(u => new SecomIdentityUser(u.CollectionId, u.Source, FollowsDefault(u.Setting))),
        ];
    }

    /// <summary>The sources set to <see cref="SecomIdentitySetting.Default"/>.</summary>
    public static IReadOnlyList<SecomIdentityUser> FollowingDefault(IEnumerable<LibraryCollection> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        return [.. Secom(collections).Where(u => FollowsDefault(u.Definition.Identity)).Select(u => u with { ViaDefault = true })];
    }

    /// <summary>
    /// Sets every source whose setting names <paramref name="id"/> to
    /// <paramref name="setting"/> (e.g. <c>none</c> when the identity is
    /// removed, or the new reference when it is replaced). Each source changed
    /// is read again. Sources that follow the default are left alone.
    /// </summary>
    /// <returns>The sources changed.</returns>
    public static IReadOnlyList<SecomIdentityUser> Reassign(CollectionLibrary library, string id, string setting)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (!SecomIdentitySetting.TryParse(setting, out var canonical))
            throw new ArgumentException($"'{setting}' is not an identity setting.", nameof(setting));

        var changed = new List<SecomIdentityUser>();
        foreach (var user in Secom(library.Collections).Where(u => string.Equals(u.Definition.Identity, id, StringComparison.OrdinalIgnoreCase)))
        {
            if (library.UpdateSource(user.CollectionId, user.Definition with { Identity = canonical }))
                changed.Add(user);
        }

        return changed;
    }

    /// <summary>True when <paramref name="setting"/> follows the default (sources saved before the setting existed have none).</summary>
    public static bool FollowsDefault(string? setting) =>
        setting is null || string.Equals(setting, SecomIdentitySetting.Default, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<SecomIdentityUser> Secom(IEnumerable<LibraryCollection> collections) =>
        collections
            .Where(c => !c.IsSession)
            .SelectMany(c => c.Sources.Where(s => s.Definition is SecomSource).Select(s => new SecomIdentityUser(c.Id, s, false)));
}
