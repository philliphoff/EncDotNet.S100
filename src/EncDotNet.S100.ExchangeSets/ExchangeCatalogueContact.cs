namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The producer contact declared by an exchange catalogue's <c>contact</c>
/// element (<see cref="ExchangeCatalogue.Contact"/>).
/// </summary>
/// <remarks>
/// Every property is <see langword="null"/> when the corresponding element is
/// absent. Values are read from the <c>gco:CharacterString</c> child when
/// present, otherwise from the element text. Address fields come from the
/// ISO 19115-3 <c>cit:</c> elements inside <c>contact/address</c>.
/// </remarks>
public sealed class ExchangeCatalogueContact
{
    /// <summary>The producing organization name (<c>contact/organization</c>).</summary>
    public string? Organization { get; init; }

    /// <summary>The telephone number (<c>contact/phone/cit:number</c>).</summary>
    public string? Phone { get; init; }

    /// <summary>The street address line (<c>contact/address/cit:deliveryPoint</c>).</summary>
    public string? DeliveryPoint { get; init; }

    /// <summary>The city (<c>contact/address/cit:city</c>).</summary>
    public string? City { get; init; }

    /// <summary>The state, province or similar (<c>contact/address/cit:administrativeArea</c>).</summary>
    public string? AdministrativeArea { get; init; }

    /// <summary>The postal code (<c>contact/address/cit:postalCode</c>).</summary>
    public string? PostalCode { get; init; }

    /// <summary>The country (<c>contact/address/cit:country</c>).</summary>
    public string? Country { get; init; }
}
