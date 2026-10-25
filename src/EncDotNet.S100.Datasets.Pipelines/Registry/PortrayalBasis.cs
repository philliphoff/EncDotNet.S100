namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// What a product is drawn from: an S-100 portrayal catalogue
/// (<see cref="Catalogue"/>), or a portrayal this library defines in code
/// because the product's specification defines none
/// (<see cref="Application(string)"/>). A
/// <see cref="ProductRegistration.Portrayal"/> trait, so hosts label the
/// portrayal from the registry rather than from a list of spec names.
/// </summary>
public abstract record PortrayalBasis
{
    private PortrayalBasis()
    {
    }

    /// <summary>The product is drawn from an S-100 portrayal catalogue.</summary>
    public static PortrayalBasis Catalogue { get; } = new CatalogueBasis();

    /// <summary>
    /// The product is drawn by a portrayal defined in code, because
    /// <paramref name="basis"/> defines none.
    /// </summary>
    /// <param name="basis">
    /// The specification and edition that define no portrayal, e.g.
    /// <c>"S-104 Ed 2.0.0"</c>, or the format for a product that isn't S-100.
    /// </param>
    public static PortrayalBasis Application(string basis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basis);
        return new ApplicationBasis(basis.Trim());
    }

    /// <summary>
    /// The application portrayal, or <see langword="null"/> when the product is
    /// drawn from its catalogue.
    /// </summary>
    public ApplicationBasis? AsApplication => this as ApplicationBasis;

    /// <summary>A product drawn from its S-100 portrayal catalogue.</summary>
    public sealed record CatalogueBasis : PortrayalBasis;

    /// <summary>
    /// A product drawn by a portrayal SoundCharts defines itself, because the
    /// specification defines none, so it isn't IHO symbology.
    /// </summary>
    /// <param name="Basis">The specification and edition, e.g. <c>"S-104 Ed 2.0.0"</c>.</param>
    public sealed record ApplicationBasis(string Basis) : PortrayalBasis
    {
        /// <summary>
        /// The English label, e.g. <c>"SoundCharts portrayal: S-104 Ed 2.0.0
        /// defines no symbology"</c>. The viewer localises its own copy.
        /// </summary>
        public string Note => $"SoundCharts portrayal: {Basis} defines no symbology";
    }
}
