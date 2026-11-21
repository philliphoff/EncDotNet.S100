using System.Globalization;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

// The AML exchange-set header (#1371, decision 7): an AML badge in the
// signature badge's place (both show when the set is signed), the highest
// marking in the set and "highest of N".
internal sealed partial class ExchangeSetHeader
{
    private bool _isAml;
    private int _amlCount;
    private string? _amlHighestMarking;

    /// <summary>Whether the set holds AML cells: the header shows the AML badge.</summary>
    public bool IsAml
    {
        get => _isAml;
        private set => SetProperty(ref _isAml, value);
    }

    /// <summary>How many of the set's datasets are AML cells.</summary>
    public int AmlCount
    {
        get => _amlCount;
        private set
        {
            if (SetProperty(ref _amlCount, value))
                OnPropertyChanged(nameof(AmlHighestOfText));
        }
    }

    /// <summary>
    /// The highest protective marking of the set's loaded AML cells ("highest
    /// wins", "+N" on a tie), or <see langword="null"/> before any loads.
    /// </summary>
    public string? AmlHighestMarking
    {
        get => _amlHighestMarking;
        private set
        {
            if (SetProperty(ref _amlHighestMarking, value))
                OnPropertyChanged(nameof(HasAmlHighestMarking));
        }
    }

    /// <summary>Whether <see cref="AmlHighestMarking"/> has a value to show.</summary>
    public bool HasAmlHighestMarking => _amlHighestMarking is not null;

    /// <summary>"highest of 3": the marking chip ranks the set's AML cells.</summary>
    public string AmlHighestOfText =>
        string.Format(CultureInfo.CurrentCulture, Strings.Aml_Header_HighestOfFormat, _amlCount);

    /// <summary>The AML badge's tooltip.</summary>
    public static string AmlBadgeToolTip => Strings.Aml_Heading;

    /// <summary>
    /// The loaded AML cells of the set, in panel order: what the first-open
    /// notification and the MARKING pill's flyout describe.
    /// </summary>
    internal IReadOnlyList<DatasetEntry> AmlDatasets => Datasets.Where(d => d.IsAml).ToArray();

    /// <summary>Raised when the set's AML facts change: a cell loaded, or the members changed.</summary>
    internal event EventHandler? AmlChanged;

    private void UpdateAml()
    {
        var aml = Datasets.Where(d => d.IsAml).ToArray();
        for (var i = 0; i < aml.Length; i++)
            aml[i].IsFirstAmlInSet = i == 0;

        IsAml = aml.Length > 0;
        AmlCount = aml.Length;
        AmlHighestMarking = AmlProtectiveMarking.Highest(aml.Select(d => d.AmlMarking))?.DisplayText;
        AmlChanged?.Invoke(this, EventArgs.Empty);
    }
}
