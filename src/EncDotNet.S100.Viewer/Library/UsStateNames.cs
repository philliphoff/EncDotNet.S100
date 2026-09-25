namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Display names for the two-letter state and territory codes used by the
/// NOAA ENC product catalogue.
/// </summary>
internal static class UsStateNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AK"] = "Alaska",
        ["AL"] = "Alabama",
        ["AR"] = "Arkansas",
        ["AS"] = "American Samoa",
        ["AZ"] = "Arizona",
        ["CA"] = "California",
        ["CO"] = "Colorado",
        ["CT"] = "Connecticut",
        ["DC"] = "District of Columbia",
        ["DE"] = "Delaware",
        ["FL"] = "Florida",
        ["FM"] = "Micronesia",
        ["GA"] = "Georgia",
        ["GU"] = "Guam",
        ["HI"] = "Hawaii",
        ["IA"] = "Iowa",
        ["ID"] = "Idaho",
        ["IL"] = "Illinois",
        ["IN"] = "Indiana",
        ["KS"] = "Kansas",
        ["KY"] = "Kentucky",
        ["LA"] = "Louisiana",
        ["MA"] = "Massachusetts",
        ["MD"] = "Maryland",
        ["ME"] = "Maine",
        ["MH"] = "Marshall Islands",
        ["MI"] = "Michigan",
        ["MN"] = "Minnesota",
        ["MO"] = "Missouri",
        ["MP"] = "Northern Mariana Islands",
        ["MS"] = "Mississippi",
        ["MT"] = "Montana",
        ["NC"] = "North Carolina",
        ["ND"] = "North Dakota",
        ["NE"] = "Nebraska",
        ["NH"] = "New Hampshire",
        ["NJ"] = "New Jersey",
        ["NM"] = "New Mexico",
        ["NV"] = "Nevada",
        ["NY"] = "New York",
        ["OH"] = "Ohio",
        ["OK"] = "Oklahoma",
        ["OR"] = "Oregon",
        ["PA"] = "Pennsylvania",
        ["PO"] = "Pacific Ocean",
        ["PR"] = "Puerto Rico",
        ["PW"] = "Palau",
        ["RI"] = "Rhode Island",
        ["SC"] = "South Carolina",
        ["SD"] = "South Dakota",
        ["TN"] = "Tennessee",
        ["TX"] = "Texas",
        ["UT"] = "Utah",
        ["VA"] = "Virginia",
        ["VI"] = "U.S. Virgin Islands",
        ["VT"] = "Vermont",
        ["WA"] = "Washington",
        ["WI"] = "Wisconsin",
        ["WV"] = "West Virginia",
        ["WY"] = "Wyoming",
    };

    /// <summary>Returns "Name (CODE)", or the code alone when it is unknown.</summary>
    public static string Describe(string code) =>
        Names.TryGetValue(code, out var name) ? $"{name} ({code.ToUpperInvariant()})" : code;

    /// <summary>Returns the name for sorting, or the code when unknown.</summary>
    public static string SortKey(string code) => Names.TryGetValue(code, out var name) ? name : code;
}
