using System.Globalization;

namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// A NetCDF attribute: text, or one or more numbers. Global attributes belong
/// to the file; variable attributes to a variable.
/// </summary>
public sealed class NetCdfAttribute
{
    private readonly double[] _numbers;

    /// <summary>Creates a text attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="text">Its text.</param>
    public NetCdfAttribute(string name, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(text);
        Name = name;
        DataType = NetCdfDataType.Char;
        Text = text;
        _numbers = [];
    }

    /// <summary>Creates a numeric attribute.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="dataType">The type the file stores the numbers as.</param>
    /// <param name="numbers">The numbers.</param>
    public NetCdfAttribute(string name, NetCdfDataType dataType, IReadOnlyList<double> numbers)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(numbers);
        Name = name;
        DataType = dataType;
        _numbers = numbers.ToArray();
    }

    /// <summary>The attribute's name, for example <c>"units"</c>.</summary>
    public string Name { get; }

    /// <summary>The type the file stores the values as.</summary>
    public NetCdfDataType DataType { get; }

    /// <summary>The text of a text attribute; otherwise <see langword="null"/>.</summary>
    public string? Text { get; }

    /// <summary>The numbers of a numeric attribute; empty for a text attribute.</summary>
    public IReadOnlyList<double> Numbers => _numbers;

    /// <summary>Whether the attribute holds text.</summary>
    public bool IsText => Text is not null;

    /// <summary>The first number, or <see langword="null"/> for a text or empty attribute.</summary>
    public double? FirstNumber => _numbers.Length > 0 ? _numbers[0] : null;

    /// <summary>Returns the text, or the numbers separated by commas.</summary>
    public override string ToString() =>
        Text ?? string.Join(", ", _numbers.Select(n => n.ToString("R", CultureInfo.InvariantCulture)));
}
