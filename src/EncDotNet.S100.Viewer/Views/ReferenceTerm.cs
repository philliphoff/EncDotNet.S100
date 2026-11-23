using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// A name that is a chart reference term (symbol dictionary design note
/// §4.2, §4.6, R8, #1651): text with a dotted underline in the text colour,
/// never blue, which shows a peek after <see cref="PeekDelay"/> of hover or
/// keyboard focus and opens its entry on click.
/// </summary>
/// <remarks>
/// <para>
/// Space pins the peek and moves focus to its first link, so its links can
/// be tabbed to; Tab past the last link leaves the peek for whatever follows
/// the term, and Shift+Tab before the first goes back to the term. Escape
/// closes it. A pinned peek traps nothing: a click elsewhere closes it too.
/// </para>
/// <para>
/// The term reads its entry through <see cref="TermsProperty"/>, which a
/// host sets once and its descendants inherit. A term whose entry the index
/// doesn't hold, or that has no <see cref="Key"/>, is plain text: no
/// underline, no focus, no peek.
/// </para>
/// </remarks>
internal sealed class ReferenceTerm : Button
{
    /// <summary>How long hover or keyboard focus rests on a term before its peek shows.</summary>
    internal static TimeSpan PeekDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a peek waits after the pointer leaves, so the pointer can reach it.</summary>
    internal static TimeSpan CloseDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>The clock the delays run on; tests replace it.</summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>The words shown.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ReferenceTerm, string?>(nameof(Text));

    /// <summary>The entry the term names; <see langword="null"/> for plain text.</summary>
    public static readonly StyledProperty<ReferenceKey?> KeyProperty =
        AvaloniaProperty.Register<ReferenceTerm, ReferenceKey?>(nameof(Key));

    /// <summary>The entry as a link or key text (<c>S-101/DisplaySetting/SimplifiedSymbols</c>), for XAML.</summary>
    public static readonly StyledProperty<string?> LinkProperty =
        AvaloniaProperty.Register<ReferenceTerm, string?>(nameof(Link));

    /// <summary>A listed value's code, when the term names a value of the attribute <see cref="Key"/> names.</summary>
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<ReferenceTerm, string?>(nameof(Value));

    /// <summary>Whether the host knows the entry exists; <see langword="false"/> keeps the term plain.</summary>
    public static readonly StyledProperty<bool> IsKnownProperty =
        AvaloniaProperty.Register<ReferenceTerm, bool>(nameof(IsKnown), defaultValue: true);

    /// <summary>How the words trim.</summary>
    public static readonly StyledProperty<TextTrimming> TextTrimmingProperty =
        TextBlock.TextTrimmingProperty.AddOwner<ReferenceTerm>();

    /// <summary>How the words wrap.</summary>
    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        TextBlock.TextWrappingProperty.AddOwner<ReferenceTerm>();

    /// <summary>
    /// The chart reference behind the terms of a subtree; a host sets it once
    /// and every term below inherits it.
    /// </summary>
    public static readonly AttachedProperty<ReferenceTerms?> TermsProperty =
        AvaloniaProperty.RegisterAttached<ReferenceTerm, Control, ReferenceTerms?>("Terms", inherits: true);

    /// <summary>Whether the index holds the entry, so the words are a term.</summary>
    public static readonly DirectProperty<ReferenceTerm, bool> IsTermProperty =
        AvaloniaProperty.RegisterDirect<ReferenceTerm, bool>(nameof(IsTerm), t => t.IsTerm);

    /// <summary>The peek's view model, made when it first shows.</summary>
    public static readonly DirectProperty<ReferenceTerm, ReferencePeekViewModel?> PeekProperty =
        AvaloniaProperty.RegisterDirect<ReferenceTerm, ReferencePeekViewModel?>(nameof(Peek), t => t.Peek);

    /// <summary>Whether the peek shows.</summary>
    public static readonly DirectProperty<ReferenceTerm, bool> IsPeekOpenProperty =
        AvaloniaProperty.RegisterDirect<ReferenceTerm, bool>(nameof(IsPeekOpen), t => t.IsPeekOpen, (t, v) => t.IsPeekOpen = v);

    /// <summary>Whether the peek is pinned: Space pinned it, so its links take focus.</summary>
    public static readonly DirectProperty<ReferenceTerm, bool> IsPinnedProperty =
        AvaloniaProperty.RegisterDirect<ReferenceTerm, bool>(nameof(IsPinned), t => t.IsPinned);

    private readonly Delay _openTimer;
    private readonly Delay _closeTimer;
    private ReferenceTermInfo? _info;
    private ReferencePeekViewModel? _peek;
    private Popup? _popup;
    private ReferencePeekView? _peekView;
    private TextBlock? _text;
    private bool _isTerm;
    private bool _isPeekOpen;
    private bool _isPinned;
    private bool _focusLinkOnSpaceUp;
    private int _version;

    static ReferenceTerm()
    {
        FocusableProperty.OverrideDefaultValue<ReferenceTerm>(false);
        KeyProperty.Changed.AddClassHandler<ReferenceTerm>((t, _) => t.Resolve());
        ValueProperty.Changed.AddClassHandler<ReferenceTerm>((t, _) => t.Resolve());
        IsKnownProperty.Changed.AddClassHandler<ReferenceTerm>((t, _) => t.Resolve());
        TermsProperty.Changed.AddClassHandler<ReferenceTerm>((t, _) => t.Resolve());
        ForegroundProperty.Changed.AddClassHandler<ReferenceTerm>((t, _) => t.Underline());
        LinkProperty.Changed.AddClassHandler<ReferenceTerm>((t, e) =>
            t.Key = ReferenceNavigator.TryParse(e.NewValue as string, out var key) ? key : null);
    }

    /// <summary>Creates a term.</summary>
    public ReferenceTerm()
    {
        _openTimer = new Delay(() => ShowPeek(pin: false));
        _closeTimer = new Delay(() =>
        {
            if (!_isPinned && !IsPointerOver && !(_peekView?.IsPointerOver ?? false) && !IsKeyboardFocusWithin)
                ClosePeek();
        });
    }

    /// <inheritdoc cref="TextProperty" />
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc cref="KeyProperty" />
    public ReferenceKey? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    /// <inheritdoc cref="LinkProperty" />
    public string? Link
    {
        get => GetValue(LinkProperty);
        set => SetValue(LinkProperty, value);
    }

    /// <inheritdoc cref="ValueProperty" />
    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <inheritdoc cref="IsKnownProperty" />
    public bool IsKnown
    {
        get => GetValue(IsKnownProperty);
        set => SetValue(IsKnownProperty, value);
    }

    /// <inheritdoc cref="TextTrimmingProperty" />
    public TextTrimming TextTrimming
    {
        get => GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    /// <inheritdoc cref="TextWrappingProperty" />
    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    /// <inheritdoc cref="IsTermProperty" />
    public bool IsTerm
    {
        get => _isTerm;
        private set
        {
            if (!SetAndRaise(IsTermProperty, ref _isTerm, value))
                return;
            PseudoClasses.Set(":term", value);
            Underline();
            Focusable = value;
            Cursor = value ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            if (!value)
                ClosePeek();
        }
    }

    /// <inheritdoc cref="PeekProperty" />
    public ReferencePeekViewModel? Peek
    {
        get => _peek;
        private set => SetAndRaise(PeekProperty, ref _peek, value);
    }

    /// <inheritdoc cref="IsPeekOpenProperty" />
    public bool IsPeekOpen
    {
        get => _isPeekOpen;
        private set
        {
            if (SetAndRaise(IsPeekOpenProperty, ref _isPeekOpen, value))
                PseudoClasses.Set(":peek", value);
        }
    }

    /// <inheritdoc cref="IsPinnedProperty" />
    public bool IsPinned
    {
        get => _isPinned;
        private set
        {
            if (!SetAndRaise(IsPinnedProperty, ref _isPinned, value))
                return;
            PseudoClasses.Set(":pinned", value);
            if (_popup is not null)
                _popup.IsLightDismissEnabled = value;
        }
    }

    /// <summary>What the term says: its peek's text and accessible description; <see langword="null"/> for plain text.</summary>
    internal ReferenceTermInfo? Info => _info;

    /// <summary>The peek's view, once the template is applied.</summary>
    internal ReferencePeekView? PeekView => _peekView;

    /// <summary>Gets the terms a control's subtree reads.</summary>
    /// <param name="control">The control.</param>
    public static ReferenceTerms? GetTerms(Control control) => control.GetValue(TermsProperty);

    /// <summary>Sets the terms a control's subtree reads.</summary>
    /// <param name="control">The control.</param>
    /// <param name="value">The terms.</param>
    public static void SetTerms(Control control, ReferenceTerms? value) => control.SetValue(TermsProperty, value);

    /// <summary>Shows the peek at once; <paramref name="pin"/> also pins it and moves focus to its first link.</summary>
    /// <param name="pin">Whether to pin it.</param>
    internal void ShowPeek(bool pin) => ShowPeek(pin, focusLink: pin);

    private void ShowPeek(bool pin, bool focusLink)
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        if (!_isTerm || _info is null || GetTerms(this) is not { } terms)
            return;
        Peek ??= new ReferencePeekViewModel(_info, terms, a => Dispatcher.UIThread.Post(a), ClosePeek);
        IsPeekOpen = true;
        if (!pin)
            return;
        IsPinned = true;
        if (focusLink)
            Dispatcher.UIThread.Post(() => _peekView?.FocusFirstLink(), DispatcherPriority.Loaded);
    }

    /// <summary>Closes the peek, pinned or not.</summary>
    internal void ClosePeek()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        IsPinned = false;
        IsPeekOpen = false;
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ReferenceTerm);

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_popup is not null)
            _popup.Closed -= OnPopupClosed;
        if (_peekView is not null)
        {
            _peekView.PointerEntered -= OnPeekPointerEntered;
            _peekView.PointerExited -= OnPeekPointerExited;
            _peekView.LeaveRequested -= OnPeekLeaveRequested;
        }

        base.OnApplyTemplate(e);
        _text = e.NameScope.Find<TextBlock>("PART_Text");
        Underline();
        _popup = e.NameScope.Find<Popup>("PART_Peek");
        _peekView = e.NameScope.Find<ReferencePeekView>("PART_PeekView");
        if (_popup is not null)
        {
            _popup.Closed += OnPopupClosed;
            _popup.IsLightDismissEnabled = _isPinned;
        }

        if (_peekView is not null)
        {
            _peekView.PointerEntered += OnPeekPointerEntered;
            _peekView.PointerExited += OnPeekPointerExited;
            _peekView.LeaveRequested += OnPeekLeaveRequested;
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Resolve();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClosePeek();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _closeTimer.Stop();
        if (_isTerm && !_isPeekOpen)
            Restart(_openTimer, PeekDelay);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _openTimer.Stop();
        if (_isPeekOpen && !_isPinned)
            Restart(_closeTimer, CloseDelay);
    }

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _closeTimer.Stop();
        if (_isTerm && !_isPeekOpen)
            Restart(_openTimer, PeekDelay);
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _openTimer.Stop();
        // A pinned peek keeps focus in its links; an unpinned one closes
        // unless the pointer holds it.
        if (_isPeekOpen && !_isPinned)
            Restart(_closeTimer, CloseDelay);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_isTerm && e.Key == Avalonia.Input.Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            // Space pins the peek rather than clicking. Focus moves to its
            // first link once Space is up, so the key-up doesn't click the link.
            ShowPeek(pin: true, focusLink: false);
            _focusLinkOnSpaceUp = _isPinned;
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.Escape && _isPeekOpen)
        {
            ClosePeek();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_isTerm && e.Key == Avalonia.Input.Key.Space)
        {
            if (_focusLinkOnSpaceUp && _isPinned)
                Dispatcher.UIThread.Post(() => _peekView?.FocusFirstLink(), DispatcherPriority.Loaded);
            _focusLinkOnSpaceUp = false;
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    /// <inheritdoc />
    protected override void OnClick()
    {
        if (!_isTerm || Key is not { } key)
            return;
        ClosePeek();
        if (Command is not null)
            base.OnClick();
        else
            GetTerms(this)?.Open(key);
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new ReferenceTermAutomationPeer(this);

    private static void Restart(Delay timer, TimeSpan interval) => timer.Start(interval);

    // A term's dotted underline, in the text's own colour, never blue (§4.6):
    // a decoration without a stroke isn't drawn, so it takes the foreground.
    private void Underline()
    {
        if (_text is null)
            return;
        _text.TextDecorations = _isTerm
            ?
            [
                new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Stroke = Foreground,
                    StrokeDashArray = [1, 2],
                },
            ]
            : null;
    }

    private void Resolve()
    {
        var version = ++_version;
        Peek = null;
        Apply(null);
        if (!IsKnown || Key is not { } key || GetTerms(this) is not { } terms || VisualRoot is null)
            return;
        var value = Value;
        if (terms.TryDescribe(key, value, out var info))
        {
            Apply(info);
            return;
        }

        _ = DescribeLaterAsync(terms, key, value, version);
    }

    private async Task DescribeLaterAsync(ReferenceTerms terms, ReferenceKey key, string? value, int version)
    {
        var info = await terms.DescribeAsync(key, value).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            if (version == _version)
                Apply(info);
        });
    }

    private void Apply(ReferenceTermInfo? info)
    {
        _info = info;
        IsTerm = info is not null;
        AutomationProperties.SetHelpText(this, info?.Description);
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        // A light dismiss (a click elsewhere) closes a pinned peek.
        if (_isPeekOpen)
            ClosePeek();
    }

    private void OnPeekPointerEntered(object? sender, PointerEventArgs e) => _closeTimer.Stop();

    private void OnPeekPointerExited(object? sender, PointerEventArgs e)
    {
        if (_isPeekOpen && !_isPinned)
            Restart(_closeTimer, CloseDelay);
    }

    // Tab past the peek's last link goes on to whatever follows the term;
    // Shift+Tab before its first, or Escape, comes back to the term.
    private void OnPeekLeaveRequested(object? sender, PeekLeaveEventArgs e)
    {
        ClosePeek();
        Focus(NavigationMethod.Tab);
        if (e.Forward)
            TopLevel.GetTopLevel(this)?.FocusManager?.TryMoveFocus(NavigationDirection.Next);
    }

    /// <summary>A one-shot delay on <see cref="Clock"/> that runs its action on the UI thread unless stopped first.</summary>
    private sealed class Delay(Action due)
    {
        private ITimer? _timer;
        private int _version;

        public void Start(TimeSpan after)
        {
            Stop();
            var version = _version;
            _timer = Clock.CreateTimer(
                _ => Dispatcher.UIThread.Post(() =>
                {
                    if (version == _version)
                    {
                        Stop();
                        due();
                    }
                }),
                null,
                after,
                Timeout.InfiniteTimeSpan);
        }

        public void Stop()
        {
            _version++;
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>A term reads as a hyperlink named by its words, described by its peek's text.</summary>
    private sealed class ReferenceTermAutomationPeer(ReferenceTerm owner) : ButtonAutomationPeer(owner)
    {
        private ReferenceTerm Term => (ReferenceTerm)Owner;

        protected override string? GetNameCore() =>
            AutomationProperties.GetName(Owner) is { Length: > 0 } name ? name : Term.Text;

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            Term.IsTerm ? AutomationControlType.Hyperlink : AutomationControlType.Text;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
