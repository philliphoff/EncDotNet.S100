using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Secrets;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>An option on a live feed's settings page (<see cref="SourceOptionField"/>), being edited.</summary>
internal sealed class LiveFeedOptionViewModel : ViewModelBase
{
    private string _text;

    /// <summary>Creates the option.</summary>
    /// <param name="field">The option.</param>
    /// <param name="text">Its saved text; for a secret, empty (it's never shown).</param>
    /// <param name="hasSavedSecret">For a secret, whether one is saved.</param>
    /// <param name="environmentSet">For a secret, whether its environment variable is set.</param>
    public LiveFeedOptionViewModel(SourceOptionField field, string text, bool hasSavedSecret, bool environmentSet)
    {
        Field = field;
        _text = text;
        HasSavedSecret = hasSavedSecret;
        EnvironmentSet = environmentSet;
    }

    /// <summary>The option.</summary>
    public SourceOptionField Field { get; }

    /// <summary>Its label.</summary>
    public string Label => Field.Required ? Field.Label + " *" : Field.Label;

    /// <summary>What it does, or <see langword="null"/>.</summary>
    public string? Description => Field.Description;

    /// <summary>Whether it's a secret: masked, and never shown back.</summary>
    public bool IsSecret => Field.Kind == SourceOptionKind.Secret;

    /// <summary>Whether it's on or off.</summary>
    public bool IsBoolean => Field.Kind == SourceOptionKind.Boolean;

    /// <summary>Whether it's one of <see cref="Choices"/>.</summary>
    public bool IsChoice => Field.Kind == SourceOptionKind.Choice;

    /// <summary>Whether it's typed in.</summary>
    public bool IsText => !IsBoolean && !IsChoice;

    /// <summary>The values a choice can take.</summary>
    public IReadOnlyList<string> Choices => Field.Choices;

    /// <summary>For a secret, whether one is saved; typing replaces it.</summary>
    public bool HasSavedSecret { get; }

    /// <summary>For a secret, whether its environment variable supplies it.</summary>
    public bool EnvironmentSet { get; }

    /// <summary>The text being edited.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(IsChecked));
            }
        }
    }

    /// <summary>A boolean option's state.</summary>
    public bool IsChecked
    {
        get => string.Equals(_text, "true", StringComparison.OrdinalIgnoreCase);
        set => Text = value ? "true" : "false";
    }

    /// <summary>The hint under a secret: where it's kept, or where it comes from.</summary>
    public string? Hint => !IsSecret ? null
        : EnvironmentSet ? string.Format(CultureInfo.CurrentCulture, Strings.LiveFeed_SecretFromEnvironmentFormat, Field.EnvironmentVariable)
        : HasSavedSecret ? Strings.LiveFeed_SecretSaved
        : Field.EnvironmentVariable is { } variable ? string.Format(CultureInfo.CurrentCulture, Strings.LiveFeed_SecretOrEnvironmentFormat, variable)
        : null;

    /// <summary>Whether it has a hint.</summary>
    public bool HasHint => Hint is not null;

    /// <summary>Whether the text is a valid value. A saved or supplied secret counts as a value.</summary>
    public bool IsValid => IsSecret && string.IsNullOrWhiteSpace(_text)
        ? !Field.Required || HasSavedSecret || EnvironmentSet
        : SourceOptionsForm.IsValid(Field, _text);
}

/// <summary>
/// A live feed's settings page in the Library (data source design note D8,
/// slice 9b): its options marked <see cref="SourceOptionAttribute"/>, edited
/// and saved on its <see cref="LiveFeedSource"/>, with secrets in the D7
/// secret store. Saving updates the source, which puts the feed back on the
/// map with the new options.
/// </summary>
internal sealed class LiveFeedSettingsViewModel : ViewModelBase
{
    private readonly Func<string, SourceRegistration?> _find;
    private readonly Func<Guid, CollectionSource, bool> _update;
    private readonly ISecretStore? _secrets;
    private readonly Func<string, string?> _environment;
    private LibraryCollection? _collection;
    private LiveFeedSource? _feed;
    private SourceRegistration? _registration;
    private string? _message;

    /// <summary>Creates the page.</summary>
    /// <param name="find">Finds a feed's registration by id.</param>
    /// <param name="update">Replaces a source's definition in its collection (<see cref="CollectionLibrary.UpdateSource"/>).</param>
    /// <param name="secrets">The secret store, or <see langword="null"/> for none.</param>
    /// <param name="environment">Reads an environment variable; <see langword="null"/> for the process's.</param>
    public LiveFeedSettingsViewModel(
        Func<string, SourceRegistration?> find,
        Func<Guid, CollectionSource, bool> update,
        ISecretStore? secrets,
        Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(update);
        _find = find;
        _update = update;
        _secrets = secrets;
        _environment = environment ?? Environment.GetEnvironmentVariable;
        SaveCommand = new RelayCommand(Save, () => _feed is not null && Options.Count > 0);
        RevertCommand = new RelayCommand(Load, () => _feed is not null);
    }

    /// <summary>The feed shown, or null.</summary>
    public Guid? SourceId => _feed?.Id;

    /// <summary>The feed's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What the feed shows.</summary>
    public string? Description => _registration?.Description;

    /// <summary>The options.</summary>
    public ObservableCollection<LiveFeedOptionViewModel> Options { get; } = [];

    /// <summary>Whether the feed has options to set.</summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>Shown for a feed with nothing to set.</summary>
    public bool HasNoOptions => _feed is not null && Options.Count == 0;

    /// <summary>Whether a required option has no value: the feed can't run ("Key needed").</summary>
    public bool NeedsSettings => Options.Any(o => o.Field.Required && !o.IsValid);

    /// <summary>Why the feed can't run, or the result of saving; <see langword="null"/> for nothing to say.</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    /// <summary>Whether there's a <see cref="Message"/>.</summary>
    public bool HasMessage => _message is not null;

    /// <summary>Saves the options.</summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>Puts the saved options back.</summary>
    public RelayCommand RevertCommand { get; }

    /// <summary>Shows <paramref name="source"/> of <paramref name="collection"/> when it's a live feed, else nothing.</summary>
    public void Show(LibraryCollection? collection, LibrarySource? source)
    {
        var feed = source?.Definition as LiveFeedSource;
        if (feed is not null && _feed is not null && feed.Id == _feed.Id && collection?.Id == _collection?.Id)
        {
            // The same feed, perhaps renamed or saved: keep any edits.
            _feed = feed;
            _collection = collection;
            return;
        }
        _collection = collection;
        _feed = feed;
        _registration = feed is null ? null : _find(feed.RegistrationId);
        Load();
    }

    private void Load()
    {
        Options.Clear();
        Message = null;
        if (_feed is { } feed && _collection is not null)
        {
            Name = feed.DisplayName ?? _registration?.DisplayName ?? feed.RegistrationId;
            if (_registration is null)
            {
                Message = string.Format(CultureInfo.CurrentCulture, Strings.LiveFeed_UnknownFormat, feed.RegistrationId);
            }
            else
            {
                var saved = feed.Options ?? SourceOptionsForm.Read(_registration, null);
                foreach (var field in SourceOptionsForm.Fields(_registration))
                {
                    saved.TryGetValue(field.Name, out var text);
                    if (field.Kind == SourceOptionKind.Secret)
                    {
                        var hasSaved = !string.IsNullOrWhiteSpace(text);
                        var environmentSet = field.EnvironmentVariable is { } variable && !string.IsNullOrWhiteSpace(_environment(variable));
                        Options.Add(new LiveFeedOptionViewModel(field, string.Empty, hasSaved, environmentSet));
                    }
                    else
                    {
                        Options.Add(new LiveFeedOptionViewModel(field, text ?? string.Empty, false, false));
                    }
                }
                if (NeedsSettings)
                    Message = Strings.LiveFeed_NeedsSettings;
            }
        }
        else
        {
            Name = string.Empty;
        }
        RaiseAll();
    }

    private void Save()
    {
        if (_feed is not { } feed || _collection is not { } collection || _registration is not { } registration)
            return;
        if (Options.FirstOrDefault(o => !o.IsValid && !(o.Field.Required && IsBlank(o))) is { } bad)
        {
            Message = string.Format(CultureInfo.CurrentCulture, Strings.LiveFeed_InvalidFormat, bad.Field.Label);
            return;
        }

        var saved = feed.Options ?? SourceOptionsForm.Read(registration, null);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in Options)
        {
            var name = option.Field.Name;
            if (option.IsSecret)
            {
                if (IsBlank(option))
                {
                    if (saved.TryGetValue(name, out var kept))
                        values[name] = kept;
                    continue;
                }
                values[name] = StoreSecret(registration, option.Field, option.Text.Trim());
            }
            else if (!IsBlank(option))
            {
                values[name] = option.Text.Trim();
            }
        }

        _update(collection.Id, feed with { Options = values });
        Load();
        Message ??= Strings.LiveFeed_Saved;
        OnPropertyChanged(nameof(NeedsSettings));
    }

    /// <summary>The secret's name in the store: the feed's id and the option's, such as <c>ais.aisstream.apiKey</c>.</summary>
    /// <param name="registration">The feed.</param>
    /// <param name="field">The option.</param>
    internal static string SecretName(SourceRegistration registration, SourceOptionField field) =>
        registration.Id + "." + char.ToLowerInvariant(field.Name[0]) + field.Name[1..];

    /// <summary>Stores the secret and returns its reference, or the secret itself where there's no store (D7 as built).</summary>
    private string StoreSecret(SourceRegistration registration, SourceOptionField field, string value)
    {
        if (_secrets is not { IsAvailable: true } store)
            return value;
        var name = SecretName(registration, field);
        try
        {
            store.Write(name, value);
            return SecretReference.For(name);
        }
        catch (SecretStoreException)
        {
            return value;
        }
    }

    private static bool IsBlank(LiveFeedOptionViewModel option) => string.IsNullOrWhiteSpace(option.Text);

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(SourceId));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(HasNoOptions));
        OnPropertyChanged(nameof(NeedsSettings));
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }
}
