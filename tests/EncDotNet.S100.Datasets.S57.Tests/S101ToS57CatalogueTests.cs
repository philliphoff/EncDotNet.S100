using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// The reverse translation takes S-57 codes and attribute types from the
/// upstream IHO S-57 Edition 3.1 Object Catalogue (<see cref="S57ObjectCatalogue.Iho"/>).
/// These pin the values it relies on, which were checked against OpenCPN's
/// s57objectclasses.csv / s57attributes.csv when the local tables were removed (#1284).
/// </summary>
public class S101ToS57CatalogueTests
{
    // S-57 Ed 3.1 Appendix A attributes of type List.
    private static readonly string[] ListAttributes =
    [
        "CATACH", "CATAIR", "CATBRG", "CATDPG", "CATHAF", "CATHLK", "CATLMK", "CATLND",
        "CATLIT", "CATMPA", "CATOFP", "CATPIP", "CATREA", "CATROS", "CATRSC", "CATSCF",
        "CATSIT", "CATSIW", "CATSPM", "CATVEG", "COLOUR", "COLPAT", "FUNCTN", "LITVIS",
        "NATCON", "NATQUA", "NATSUR", "PRODCT", "QUASOU", "RESTRN", "STATUS", "SURTYP",
        "TECSOU",
    ];

    [Fact]
    public void ListAttributes_AreTheS57ListTypedAttributes()
    {
        var list = S57ObjectCatalogue.Iho.Attributes.Select(a => a.Acronym).Where(S101ToS57Rules.IsS57ListAttribute);
        Assert.Equal(ListAttributes.Order(StringComparer.Ordinal), list.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("C_AGGR", 400)]
    [InlineData("C_ASSO", 401)]
    [InlineData("M_CSCL", 301)]
    [InlineData("TOPMAR", 144)]
    [InlineData("TS_PAD", 138)]
    [InlineData("ARCSLN", 161)]
    [InlineData("ASLXIS", 162)]
    [InlineData("NEWOBJ", 163)]
    public void Objl_ComesFromTheCatalogue(string acronym, int objl)
        => Assert.Equal(objl, S101ToS57Rules.S57Objl(acronym));

    [Theory]
    [InlineData("CATCOV", 18)]
    [InlineData("CSCALE", 80)]
    [InlineData("INFORM", 102)]
    [InlineData("POSACC", 401)]
    [InlineData("SOUACC", 144)]
    [InlineData("QUAPOS", 402)]
    [InlineData("CLSDEF", 190)]
    [InlineData("CLSNAM", 191)]
    [InlineData("SYMINS", 192)]
    [InlineData("NINFOM", 300)]
    [InlineData("NOBJNM", 301)]
    [InlineData("NPLDST", 302)]
    [InlineData("NTXTDS", 304)]
    public void Attl_ComesFromTheCatalogue(string acronym, int attl)
    {
        Assert.Equal(attl, S101ToS57Rules.S57Attl(acronym));
        Assert.Equal(acronym, S101ToS57Rules.S57AttributeAcronym(attl));
    }
}
