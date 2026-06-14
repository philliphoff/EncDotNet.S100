using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using ShadUI;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View-model backing the "Report Feedback" modal dialog. Collects a
/// diagnostic snapshot plus an optional screenshot, lets the user review
/// and amend it, and submits via <see cref="IFeedbackService"/>.
/// </summary>
/// <remarks>
/// The dialog is hosted by ShadUI's <see cref="DialogManager"/>; the
/// view-model closes itself through that manager on submit or cancel.
/// </remarks>
internal sealed class FeedbackDialogViewModel : ViewModelBase
{
    private readonly DialogManager _dialogManager;
    private readonly IFeedbackService _feedbackService;
    private readonly IToastService _toasts;

    private FeedbackReport? _report;
    private byte[]? _screenshotPng;

    public FeedbackDialogViewModel(
        DialogManager dialogManager,
        IFeedbackService feedbackService,
        IToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(dialogManager);
        ArgumentNullException.ThrowIfNull(feedbackService);
        ArgumentNullException.ThrowIfNull(toasts);

        _dialogManager = dialogManager;
        _feedbackService = feedbackService;
        _toasts = toasts;

        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>Human-readable description of exactly what is collected.</summary>
    public string DataDescription => Strings.Feedback_DataDescription;

    private string _userMessage = string.Empty;
    /// <summary>The free-form feedback the user typed.</summary>
    public string UserMessage
    {
        get => _userMessage;
        set => SetProperty(ref _userMessage, value);
    }

    private bool _includeScreenshot = true;
    /// <summary>Whether the captured screenshot is included on submit.</summary>
    public bool IncludeScreenshot
    {
        get => _includeScreenshot;
        set => SetProperty(ref _includeScreenshot, value);
    }

    private Bitmap? _screenshotImage;
    /// <summary>Decoded screenshot for preview, or <see langword="null"/>
    /// when none was captured.</summary>
    public Bitmap? ScreenshotImage
    {
        get => _screenshotImage;
        private set
        {
            if (SetProperty(ref _screenshotImage, value))
                OnPropertyChanged(nameof(HasScreenshot));
        }
    }

    /// <summary>True when a screenshot was captured and can be previewed.</summary>
    public bool HasScreenshot => _screenshotImage is not null;

    private string _rawDiagnostics = string.Empty;
    /// <summary>The exact diagnostics JSON that will be sent.</summary>
    public string RawDiagnostics
    {
        get => _rawDiagnostics;
        private set => SetProperty(ref _rawDiagnostics, value);
    }

    private bool _isRawDataExpanded;
    /// <summary>Whether the raw-data disclosure section is expanded.</summary>
    public bool IsRawDataExpanded
    {
        get => _isRawDataExpanded;
        set => SetProperty(ref _isRawDataExpanded, value);
    }

    private bool _isBusy;
    /// <summary>True while a submission is in flight.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                ((AsyncRelayCommand)SubmitCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>Submits the feedback (saves a bundle + opens a GitHub issue).</summary>
    public ICommand SubmitCommand { get; }

    /// <summary>Dismisses the dialog without submitting.</summary>
    public ICommand CancelCommand { get; }

    /// <summary>
    /// Gathers diagnostics and the screenshot. Call once before showing
    /// the dialog.
    /// </summary>
    public async Task InitializeAsync()
    {
        var result = await _feedbackService.CollectAsync().ConfigureAwait(true);
        _report = result.Report;
        _screenshotPng = result.ScreenshotPng;
        RawDiagnostics = result.Report.ToJson();

        if (_screenshotPng is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(_screenshotPng);
                ScreenshotImage = new Bitmap(stream);
            }
            catch
            {
                ScreenshotImage = null;
            }
        }
    }

    private async Task SubmitAsync()
    {
        if (_report is null)
            return;

        IsBusy = true;
        try
        {
            var screenshot = IncludeScreenshot ? _screenshotPng : null;
            var result = await _feedbackService
                .SubmitAsync(new FeedbackSubmitRequest(_report, UserMessage, screenshot))
                .ConfigureAwait(true);

            _toasts.ShowSuccess(
                Strings.Feedback_SubmittedTitle,
                string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    result.ScreenshotOnClipboard
                        ? Strings.Feedback_SubmittedBodyClipboard
                        : Strings.Feedback_SubmittedBody,
                    result.BundlePath));

            _dialogManager.Close(this, new CloseDialogOptions { Success = true });
        }
        catch (Exception ex)
        {
            _toasts.ShowError(Strings.Feedback_SubmitFailedTitle, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Cancel() => _dialogManager.Close(this);
}
