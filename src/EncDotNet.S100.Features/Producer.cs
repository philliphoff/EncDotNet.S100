namespace EncDotNet.S100.Features;

/// <summary>
/// The organisation responsible for a <see cref="FeatureCatalogue"/>, parsed from its
/// <c>S100FC:producer</c> element (an S100CI responsible-party structure). Every member is
/// <see langword="null"/> when the corresponding element is absent.
/// </summary>
public sealed class Producer
{
    /// <summary>The party's role code (<c>S100CI:role</c>), e.g. <c>"pointOfContact"</c>.</summary>
    public string? Role { get; init; }

    /// <summary>Organisation name (<c>S100CI:party/S100CI:CI_Organisation/S100CI:name</c>).</summary>
    public string? OrganisationName { get; init; }

    /// <summary>State, province or other administrative area of the organisation's address (<c>contactInfo/address/administrativeArea</c>).</summary>
    public string? AdministrativeArea { get; init; }

    /// <summary>Country of the organisation's address (<c>contactInfo/address/country</c>).</summary>
    public string? Country { get; init; }

    /// <summary>Contact e-mail address (<c>contactInfo/address/electronicMailAddress</c>).</summary>
    public string? ElectronicMailAddress { get; init; }

    /// <summary>Online resource URL for the organisation (<c>contactInfo/onlineResource/linkage</c>).</summary>
    public string? Linkage { get; init; }
}
