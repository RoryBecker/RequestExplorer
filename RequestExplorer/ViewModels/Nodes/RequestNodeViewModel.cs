using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RequestExplorer.Models;
using RequestExplorer.Services.KeyValue;
using RequestExplorer.Services.Undo;
using RequestExplorer.Services.Variables;
using RequestExplorer.ViewModels;

namespace RequestExplorer.ViewModels.Nodes;

public sealed partial class RequestNodeViewModel : TreeNodeViewModel, IKeyValueListSortHost
{
    private bool _trackDirty = true;
    private bool _ensuringSpare;
    private bool _syncingUrlAndParams;
    private bool _suppressFieldUndo;
    private IUndoRecorder? _undoRecorder;

    public RequestNodeViewModel(RequestDefinition model)
        : base(model.Name)
    {
        Model = model;
        _verb = model.Verb;
        _address = model.Address;
        _authType = model.Authentication.Type;
        _bearerToken = model.Authentication.BearerToken;
        _basicUsername = model.Authentication.BasicUsername;
        _basicPassword = model.Authentication.BasicPassword;
        _apiKeyName = model.Authentication.ApiKeyName;
        _apiKeyValue = model.Authentication.ApiKeyValue;
        _apiKeyLocation = model.Authentication.ApiKeyLocation;
        _bodyContent = model.Body.Content;
        _bodyContentType = model.Body.ContentType ?? string.Empty;
        _expectedStatusCode = model.ExpectedStatusCode is >= 100 and <= 599
            ? model.ExpectedStatusCode
            : 200;
        _expectBodyEnabled = model.ExpectBodyEnabled;
        _expectBodyContains = model.ExpectBodyContains;
        _expectedBody = model.ExpectedBody ?? string.Empty;
        if (!model.ExpectTimingEnabled && model.ExpectedTimingMilliseconds <= 0)
        {
            model.ExpectTimingEnabled = true;
            model.ExpectedTimingMilliseconds = 300;
        }
        else if (model.ExpectedTimingMilliseconds < 0)
        {
            model.ExpectedTimingMilliseconds = 300;
        }

        _expectTimingEnabled = model.ExpectTimingEnabled;
        _expectedTimingMilliseconds = model.ExpectedTimingMilliseconds;
        _optOutOfAutoRun = model.OptOutOfAutoRun;
        _description = model.Description ?? string.Empty;

        _trackDirty = false;
        foreach (var item in model.Params)
        {
            Params.Add(CreateParamVm(item));
        }

        foreach (var item in model.Headers)
        {
            Headers.Add(CreateHeaderVm(item));
        }

        EnsureParamSpareRow();
        EnsureHeaderSpareRow();

        // Prefer explicit params → URL; otherwise hydrate params from a query in the address.
        if (Params.Any(p => !string.IsNullOrWhiteSpace(p.Key)))
        {
            SyncAddressFromParams();
        }
        else
        {
            SyncParamsFromAddress(Address);
        }

        _trackDirty = true;

        Params.CollectionChanged += OnParamsCollectionChanged;
        Headers.CollectionChanged += OnHeadersCollectionChanged;
        LastResponse.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ResponseViewModel.CanCopyExpectationsFromLastRun)
                or nameof(ResponseViewModel.HasResult)
                or nameof(ResponseViewModel.IsSending)
                or nameof(ResponseViewModel.IsError)
                or nameof(ResponseViewModel.BodyText)
                or nameof(ResponseViewModel.TotalMilliseconds))
            {
                CopyExpectedBodyFromLastRunCommand.NotifyCanExecuteChanged();
                CopyExpectedTimingFromLastRunCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public void AttachUndoRecorder(IUndoRecorder recorder) => _undoRecorder = recorder;

    public override TreeNodeKind Kind => TreeNodeKind.Request;

    public RequestDefinition Model { get; }

    public override bool LocallyOptsOutOfAutoRun => OptOutOfAutoRun;

    public static Array VerbOptions { get; } = Enum.GetValues(typeof(HttpVerb));
    public static Array AuthTypeOptions { get; } = Enum.GetValues(typeof(AuthType));
    public static Array ApiKeyLocationOptions { get; } = Enum.GetValues(typeof(ApiKeyLocation));

    public ObservableCollection<KeyValueItemViewModel> Params { get; } = [];
    public ObservableCollection<KeyValueItemViewModel> Headers { get; } = [];

    /// <summary>
    /// Most recent send/diagnosis result for this request in the current session only.
    /// Not persisted to disk.
    /// </summary>
    public ResponseViewModel LastResponse { get; } = new();

    [ObservableProperty]
    private HttpVerb _verb;

    [ObservableProperty]
    private string _address;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthNone))]
    [NotifyPropertyChangedFor(nameof(IsAuthBearer))]
    [NotifyPropertyChangedFor(nameof(IsAuthBasic))]
    [NotifyPropertyChangedFor(nameof(IsAuthApiKey))]
    private AuthType _authType;

    [ObservableProperty]
    private string _bearerToken;

    [ObservableProperty]
    private string _basicUsername;

    [ObservableProperty]
    private string _basicPassword;

    [ObservableProperty]
    private string _apiKeyName;

    [ObservableProperty]
    private string _apiKeyValue;

    [ObservableProperty]
    private ApiKeyLocation _apiKeyLocation;

    [ObservableProperty]
    private string _bodyContent;

    [ObservableProperty]
    private string _bodyContentType;

    [ObservableProperty]
    private int _expectedStatusCode = 200;

    [ObservableProperty]
    private bool _expectBodyEnabled;

    [ObservableProperty]
    private bool _expectBodyContains;

    [ObservableProperty]
    private string _expectedBody = string.Empty;

    [ObservableProperty]
    private bool _expectTimingEnabled = true;

    [ObservableProperty]
    private double _expectedTimingMilliseconds = 300;

    [ObservableProperty]
    private bool _optOutOfAutoRun;

    [ObservableProperty]
    private string _description = string.Empty;

    public bool IsAuthNone => AuthType == AuthType.None;
    public bool IsAuthBearer => AuthType == AuthType.Bearer;
    public bool IsAuthBasic => AuthType == AuthType.Basic;
    public bool IsAuthApiKey => AuthType == AuthType.ApiKey;

    public void MarkDirty()
    {
        if (!_trackDirty || _ensuringSpare || _syncingUrlAndParams
            || _undoRecorder?.IsApplyingHistory == true)
        {
            return;
        }

        IsDirty = true;
    }

    public void ClearDirty()
    {
        IsDirty = false;
    }

    protected override void OnNameUpdated(string value)
    {
        Model.Name = value;
        MarkDirty();
    }

    partial void OnVerbChanged(HttpVerb value)
    {
        var previous = Model.Verb;
        if (previous == value)
        {
            return;
        }

        Model.Verb = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.Verb, previous, "Change verb");
    }

    protected override string? GetSearchUrl() => Address;

    /// <summary>Address with <c>{{variables}}</c> substituted from the parent chain (read-only preview).</summary>
    public string ResolvedAddress
    {
        get
        {
            var spans = ResolvedAddressSpans;
            if (spans.Count == 0)
            {
                return string.Empty;
            }

            if (spans.Count == 1)
            {
                return spans[0].Text;
            }

            return string.Concat(spans.Select(s => s.Text));
        }
    }

    /// <summary>Resolved address slices; <see cref="ResolvedTextSpan.IsFromVariable"/> marks substituted tokens.</summary>
    public IReadOnlyList<ResolvedTextSpan> ResolvedAddressSpans
    {
        get
        {
            var scope = VariableResolver.BuildScope(this);
            return VariableResolver.SubstituteWithSpans(Address, scope);
        }
    }

    /// <summary>True when resolution changed the address (tokens were substituted).</summary>
    public bool ShowsResolvedAddress =>
        !string.Equals(Address ?? string.Empty, ResolvedAddress, StringComparison.Ordinal);

    public void NotifyResolvedAddressChanged()
    {
        OnPropertyChanged(nameof(ResolvedAddressSpans));
        OnPropertyChanged(nameof(ResolvedAddress));
        OnPropertyChanged(nameof(ShowsResolvedAddress));
        CopyResolvedAddressCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCopyResolvedAddress))]
    private void CopyResolvedAddress()
    {
        var text = ResolvedAddress;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard can be locked by another process.
        }
    }

    private bool CanCopyResolvedAddress() => !string.IsNullOrEmpty(ResolvedAddress);

    partial void OnAddressChanged(string value)
    {
        value ??= string.Empty;
        var previous = Model.Address ?? string.Empty;
        Model.Address = value;
        OnPropertyChanged(nameof(SearchText));
        NotifyResolvedAddressChanged();
        if (_syncingUrlAndParams)
        {
            return;
        }

        MarkDirty();
        if (!string.Equals(previous, value, StringComparison.Ordinal))
        {
            RecordAddressUndo(previous);
        }

        SyncParamsFromAddress(value);
        RaiseSearchTextInvalidated();
    }

    partial void OnAuthTypeChanged(AuthType value)
    {
        var previous = Model.Authentication.Type;
        if (previous == value)
        {
            return;
        }

        Model.Authentication.Type = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.AuthType, previous, "Change auth type");
    }

    partial void OnBearerTokenChanged(string value)
    {
        var previous = Model.Authentication.BearerToken ?? string.Empty;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return;
        }

        Model.Authentication.BearerToken = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.BearerToken, previous, "Edit bearer token");
    }

    partial void OnBasicUsernameChanged(string value)
    {
        var previous = Model.Authentication.BasicUsername ?? string.Empty;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return;
        }

        Model.Authentication.BasicUsername = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.BasicUsername, previous, "Edit basic username");
    }

    partial void OnBasicPasswordChanged(string value)
    {
        var previous = Model.Authentication.BasicPassword ?? string.Empty;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return;
        }

        Model.Authentication.BasicPassword = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.BasicPassword, previous, "Edit basic password");
    }

    partial void OnApiKeyNameChanged(string value)
    {
        var previous = Model.Authentication.ApiKeyName ?? string.Empty;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return;
        }

        Model.Authentication.ApiKeyName = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.ApiKeyName, previous, "Edit API key name");
    }

    partial void OnApiKeyValueChanged(string value)
    {
        var previous = Model.Authentication.ApiKeyValue ?? string.Empty;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return;
        }

        Model.Authentication.ApiKeyValue = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.ApiKeyValue, previous, "Edit API key value");
    }

    partial void OnApiKeyLocationChanged(ApiKeyLocation value)
    {
        var previous = Model.Authentication.ApiKeyLocation;
        if (previous == value)
        {
            return;
        }

        Model.Authentication.ApiKeyLocation = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.ApiKeyLocation, previous, "Change API key location");
    }

    /// <summary>Sets description without recording a per-keystroke undo (caller records as needed).</summary>
    public void ApplyDescription(string value)
    {
        _suppressFieldUndo = true;
        try
        {
            Description = value ?? string.Empty;
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    partial void OnDescriptionChanged(string value)
    {
        var normalized = value ?? string.Empty;
        var previous = Model.Description ?? string.Empty;
        if (string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            return;
        }

        Model.Description = string.IsNullOrEmpty(normalized) ? null : normalized;
        MarkDirty();
        RecordDescriptionUndo(previous);
    }

    private void RecordDescriptionUndo(string previous)
    {
        if (_suppressFieldUndo || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        _undoRecorder.Record(
            new SetRequestDescriptionAction(this, previous, "Edit description"));
    }

    public void ApplyOptOutOfAutoRun(bool value)
    {
        _suppressFieldUndo = true;
        try
        {
            OptOutOfAutoRun = value;
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    partial void OnOptOutOfAutoRunChanged(bool value)
    {
        var previous = Model.OptOutOfAutoRun;
        if (previous != value)
        {
            Model.OptOutOfAutoRun = value;
            MarkDirty();
            RecordOptOutUndo(previous);
        }

        NotifyAutoRunExclusionChanged();
    }

    private void RecordOptOutUndo(bool previous)
    {
        if (_suppressFieldUndo || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        _undoRecorder.Record(
            new SetOptOutOfAutoRunAction(this, previous, "Toggle opt out of auto run"));
    }

    partial void OnExpectedStatusCodeChanged(int value)
    {
        var previous = Model.ExpectedStatusCode;
        if (previous == value)
        {
            return;
        }

        Model.ExpectedStatusCode = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.ExpectedStatusCode, previous, "Edit expected status");
    }

    partial void OnExpectBodyEnabledChanged(bool value)
    {
        var previous = Model.ExpectBodyEnabled;
        if (previous == value)
        {
            return;
        }

        Model.ExpectBodyEnabled = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.ExpectBodyEnabled, previous, "Toggle expected body");
    }

    partial void OnExpectBodyContainsChanged(bool value)
    {
        var previous = Model.ExpectBodyContains;
        if (previous == value)
        {
            return;
        }

        Model.ExpectBodyContains = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.ExpectBodyContains, previous, "Toggle expected body contains");
    }

    partial void OnExpectedBodyChanged(string value)
    {
        value ??= string.Empty;
        var previous = Model.ExpectedBody ?? string.Empty;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return;
        }

        Model.ExpectedBody = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.ExpectedBody, previous, "Edit expected body");
    }

    partial void OnExpectTimingEnabledChanged(bool value)
    {
        var previous = Model.ExpectTimingEnabled;
        if (previous == value)
        {
            return;
        }

        Model.ExpectTimingEnabled = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.ExpectTimingEnabled, previous, "Toggle expected timing");
    }

    partial void OnExpectedTimingMillisecondsChanged(double value)
    {
        if (value < 0)
        {
            ExpectedTimingMilliseconds = 0;
            return;
        }

        var previous = Model.ExpectedTimingMilliseconds;
        if (Math.Abs(previous - value) < 0.0001)
        {
            return;
        }

        Model.ExpectedTimingMilliseconds = value;
        MarkDirty();
        RecordEditorFieldUndo(RequestEditorField.ExpectedTimingMilliseconds, previous, "Edit expected timing");
    }

    [RelayCommand(CanExecute = nameof(CanCopyExpectationsFromLastRun))]
    private void CopyExpectedBodyFromLastRun()
    {
        if (!CanCopyExpectationsFromLastRun())
        {
            return;
        }

        var previousBody = ExpectedBody ?? string.Empty;
        var previousEnabled = ExpectBodyEnabled;
        var nextBody = LastResponse.BodyText ?? string.Empty;
        _suppressFieldUndo = true;
        try
        {
            ExpectedBody = nextBody;
            ExpectBodyEnabled = true;
        }
        finally
        {
            _suppressFieldUndo = false;
        }

        RecordCopiedExpectationsUndo(
            "Copy expected body from last run",
            (RequestEditorField.ExpectedBody, previousBody),
            (RequestEditorField.ExpectBodyEnabled, previousEnabled));
    }

    [RelayCommand(CanExecute = nameof(CanCopyExpectationsFromLastRun))]
    private void CopyExpectedTimingFromLastRun()
    {
        if (!CanCopyExpectationsFromLastRun() || LastResponse.TotalMilliseconds is not double ms)
        {
            return;
        }

        var previousTiming = ExpectedTimingMilliseconds;
        var previousEnabled = ExpectTimingEnabled;
        _suppressFieldUndo = true;
        try
        {
            ExpectedTimingMilliseconds = Math.Round(ms, 3);
            ExpectTimingEnabled = true;
        }
        finally
        {
            _suppressFieldUndo = false;
        }

        RecordCopiedExpectationsUndo(
            "Copy expected timing from last run",
            (RequestEditorField.ExpectedTimingMilliseconds, previousTiming),
            (RequestEditorField.ExpectTimingEnabled, previousEnabled));
    }

    private bool CanCopyExpectationsFromLastRun() => LastResponse.CanCopyExpectationsFromLastRun;

    partial void OnBodyContentChanged(string value)
    {
        var previous = Model.Body.Content;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return;
        }

        Model.Body.Content = value;
        MarkDirty();
        RecordBodyUndo(RequestBodyField.Content, previous, "Edit request body");
    }

    partial void OnBodyContentTypeChanged(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
        var previous = Model.Body.ContentType;
        if (string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            return;
        }

        Model.Body.ContentType = normalized;
        MarkDirty();
        RecordBodyUndo(RequestBodyField.ContentType, previous, "Edit body content type");
    }

    public string? GetBodyField(RequestBodyField field) =>
        field switch
        {
            RequestBodyField.Content => BodyContent,
            RequestBodyField.ContentType => string.IsNullOrWhiteSpace(BodyContentType) ? null : BodyContentType,
            _ => null
        };

    public void ApplyBodyField(RequestBodyField field, string? value)
    {
        _suppressFieldUndo = true;
        try
        {
            switch (field)
            {
                case RequestBodyField.Content:
                    BodyContent = value ?? string.Empty;
                    break;
                case RequestBodyField.ContentType:
                    BodyContentType = value ?? string.Empty;
                    break;
            }

            MarkDirty();
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    /// <summary>Sets the address without recording a per-keystroke undo (caller records as needed).</summary>
    public void ApplyAddress(string value)
    {
        _suppressFieldUndo = true;
        try
        {
            Address = value ?? string.Empty;
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    public object? GetEditorField(RequestEditorField field) =>
        field switch
        {
            RequestEditorField.Verb => Verb,
            RequestEditorField.AuthType => AuthType,
            RequestEditorField.BearerToken => BearerToken ?? string.Empty,
            RequestEditorField.BasicUsername => BasicUsername ?? string.Empty,
            RequestEditorField.BasicPassword => BasicPassword ?? string.Empty,
            RequestEditorField.ApiKeyName => ApiKeyName ?? string.Empty,
            RequestEditorField.ApiKeyValue => ApiKeyValue ?? string.Empty,
            RequestEditorField.ApiKeyLocation => ApiKeyLocation,
            RequestEditorField.ExpectedStatusCode => ExpectedStatusCode,
            RequestEditorField.ExpectBodyEnabled => ExpectBodyEnabled,
            RequestEditorField.ExpectBodyContains => ExpectBodyContains,
            RequestEditorField.ExpectedBody => ExpectedBody ?? string.Empty,
            RequestEditorField.ExpectTimingEnabled => ExpectTimingEnabled,
            RequestEditorField.ExpectedTimingMilliseconds => ExpectedTimingMilliseconds,
            _ => null
        };

    public void ApplyEditorField(RequestEditorField field, object? value)
    {
        _suppressFieldUndo = true;
        try
        {
            switch (field)
            {
                case RequestEditorField.Verb:
                    Verb = ParseEnum(value, Verb);
                    break;
                case RequestEditorField.AuthType:
                    AuthType = ParseEnum(value, AuthType);
                    break;
                case RequestEditorField.BearerToken:
                    BearerToken = ToStringValue(value);
                    break;
                case RequestEditorField.BasicUsername:
                    BasicUsername = ToStringValue(value);
                    break;
                case RequestEditorField.BasicPassword:
                    BasicPassword = ToStringValue(value);
                    break;
                case RequestEditorField.ApiKeyName:
                    ApiKeyName = ToStringValue(value);
                    break;
                case RequestEditorField.ApiKeyValue:
                    ApiKeyValue = ToStringValue(value);
                    break;
                case RequestEditorField.ApiKeyLocation:
                    ApiKeyLocation = ParseEnum(value, ApiKeyLocation);
                    break;
                case RequestEditorField.ExpectedStatusCode:
                    ExpectedStatusCode = ToInt32(value, ExpectedStatusCode);
                    break;
                case RequestEditorField.ExpectBodyEnabled:
                    ExpectBodyEnabled = value is true;
                    break;
                case RequestEditorField.ExpectBodyContains:
                    ExpectBodyContains = value is true;
                    break;
                case RequestEditorField.ExpectedBody:
                    ExpectedBody = ToStringValue(value);
                    break;
                case RequestEditorField.ExpectTimingEnabled:
                    ExpectTimingEnabled = value is true;
                    break;
                case RequestEditorField.ExpectedTimingMilliseconds:
                    ExpectedTimingMilliseconds = ToDouble(value, ExpectedTimingMilliseconds);
                    break;
            }
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    private void RecordAddressUndo(string previous)
    {
        if (_suppressFieldUndo || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        _undoRecorder.Record(new SetRequestAddressAction(this, previous, "Edit URL"));
    }

    private void RecordEditorFieldUndo(RequestEditorField field, object? previous, string description)
    {
        if (_suppressFieldUndo || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        _undoRecorder.Record(new SetRequestEditorFieldAction(this, field, previous, description));
    }

    private void RecordCopiedExpectationsUndo(
        string description,
        params (RequestEditorField Field, object? Previous)[] steps)
    {
        if (_undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        var undoSteps = new List<IUndoAction>();
        foreach (var (field, previous) in steps)
        {
            if (Equals(GetEditorField(field), previous))
            {
                continue;
            }

            undoSteps.Add(new SetRequestEditorFieldAction(this, field, previous, description));
        }

        if (undoSteps.Count == 0)
        {
            return;
        }

        _undoRecorder.Record(
            undoSteps.Count == 1
                ? undoSteps[0]
                : new CompositeUndoAction(description, undoSteps));
    }

    private static string ToStringValue(object? value) =>
        value as string ?? value?.ToString() ?? string.Empty;

    private static int ToInt32(object? value, int fallback)
    {
        if (value is int i)
        {
            return i;
        }

        return int.TryParse(ToStringValue(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static double ToDouble(object? value, double fallback)
    {
        if (value is double d)
        {
            return d;
        }

        if (value is int i)
        {
            return i;
        }

        return double.TryParse(ToStringValue(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static TEnum ParseEnum<TEnum>(object? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (value is TEnum typed)
        {
            return typed;
        }

        return Enum.TryParse(ToStringValue(value), ignoreCase: true, out TEnum parsed)
            ? parsed
            : fallback;
    }

    private void RecordBodyUndo(RequestBodyField field, string? oldValue, string description)
    {
        if (_suppressFieldUndo || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        _undoRecorder.Record(new SetRequestBodyFieldAction(this, field, oldValue, description));
    }

    [RelayCommand]
    private void RemoveParam(KeyValueItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Key) && string.IsNullOrWhiteSpace(item.Value)
            && Params.Count == 1)
        {
            return;
        }

        var index = Params.IndexOf(item);
        Model.Params.Remove(item.Model);
        Params.Remove(item);
        MarkDirty();
        EnsureParamSpareRow();
        SyncAddressFromParams();
        RecordRowRemoveUndo(KeyValueListKind.Params, item, index, $"Remove param '{item.Key}'");
    }

    [RelayCommand]
    private void RemoveHeader(KeyValueItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Key) && string.IsNullOrWhiteSpace(item.Value)
            && Headers.Count == 1)
        {
            return;
        }

        var index = Headers.IndexOf(item);
        Model.Headers.Remove(item.Model);
        Headers.Remove(item);
        MarkDirty();
        EnsureHeaderSpareRow();
        RecordRowRemoveUndo(KeyValueListKind.Headers, item, index, $"Remove header '{item.Key}'");
    }

    public void ApplyKeyValueField(
        KeyValueListKind listKind,
        KeyValueItemViewModel item,
        KeyValueField field,
        object? value)
    {
        _suppressFieldUndo = true;
        try
        {
            item.SetFieldSilent(field, value);
            MarkDirty();
            if (listKind == KeyValueListKind.Params)
            {
                EnsureParamSpareRow();
                SyncAddressFromParams();
            }
            else
            {
                EnsureHeaderSpareRow();
            }
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    public void RemoveKeyValueRow(KeyValueListKind listKind, KeyValueItemViewModel item)
    {
        _suppressFieldUndo = true;
        try
        {
            if (listKind == KeyValueListKind.Params)
            {
                Model.Params.Remove(item.Model);
                Params.Remove(item);
                MarkDirty();
                EnsureParamSpareRow();
                SyncAddressFromParams();
            }
            else
            {
                Model.Headers.Remove(item.Model);
                Headers.Remove(item);
                MarkDirty();
                EnsureHeaderSpareRow();
            }
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    public void InsertKeyValueRow(KeyValueListKind listKind, KeyValueItemViewModel item, int index)
    {
        _suppressFieldUndo = true;
        try
        {
            if (listKind == KeyValueListKind.Params)
            {
                if (Params.Contains(item))
                {
                    return;
                }

                index = Math.Clamp(index, 0, Params.Count);
                if (!Model.Params.Contains(item.Model))
                {
                    var modelIndex = Math.Clamp(index, 0, Model.Params.Count);
                    Model.Params.Insert(modelIndex, item.Model);
                }

                Params.Insert(index, item);
                MarkDirty();
                EnsureParamSpareRow();
                SyncAddressFromParams();
            }
            else
            {
                if (Headers.Contains(item))
                {
                    return;
                }

                index = Math.Clamp(index, 0, Headers.Count);
                if (!Model.Headers.Contains(item.Model))
                {
                    var modelIndex = Math.Clamp(index, 0, Model.Headers.Count);
                    Model.Headers.Insert(modelIndex, item.Model);
                }

                Headers.Insert(index, item);
                MarkDirty();
                EnsureHeaderSpareRow();
            }
        }
        finally
        {
            _suppressFieldUndo = false;
        }
    }

    public int IndexOfKeyValue(KeyValueListKind listKind, KeyValueItemViewModel item) =>
        listKind == KeyValueListKind.Params ? Params.IndexOf(item) : Headers.IndexOf(item);

    /// <summary>Pastes rows before the spare row. Returns how many rows were inserted.</summary>
    public int PasteKeyValueRows(
        KeyValueListKind listKind,
        IReadOnlyList<KeyValueClipboardRow> rows,
        string operation = "Paste")
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var list = listKind == KeyValueListKind.Params ? Params : Headers;
        var label = listKind == KeyValueListKind.Params ? "param" : "header";
        var undoRemoves = new List<IUndoAction>();
        var inserted = 0;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key) && string.IsNullOrWhiteSpace(row.Value))
            {
                continue;
            }

            var model = new KeyValueItem
            {
                Key = row.Key ?? string.Empty,
                Value = row.Value ?? string.Empty,
                IsEnabled = row.IsEnabled && !string.IsNullOrWhiteSpace(row.Key)
            };
            var vm = listKind == KeyValueListKind.Params
                ? CreateParamVm(model)
                : CreateHeaderVm(model);

            var index = GetInsertBeforeSpareIndex(list);
            InsertKeyValueRow(listKind, vm, index);
            undoRemoves.Add(
                new RemoveKeyValueRowAction(
                    this,
                    listKind,
                    vm,
                    index,
                    $"{operation} {label} '{vm.Key}'"));
            inserted++;
        }

        if (inserted == 0)
        {
            return 0;
        }

        // Undo must remove last-inserted first so indices stay valid.
        undoRemoves.Reverse();
        var description = inserted == 1
            ? $"{operation} {label} row"
            : $"{operation} {inserted} {label} rows";
        if (_undoRecorder is not null && !_undoRecorder.IsApplyingHistory)
        {
            _undoRecorder.Record(new CompositeUndoAction(description, undoRemoves));
        }

        return inserted;
    }

    private static int GetInsertBeforeSpareIndex(ObservableCollection<KeyValueItemViewModel> list) =>
        list.Count > 0 && string.IsNullOrWhiteSpace(list[^1].Key)
            ? list.Count - 1
            : list.Count;

    public KeyValueItemViewModel CreateBoundKeyValueItem(KeyValueListKind listKind, KeyValueItem item) =>
        listKind == KeyValueListKind.Params ? CreateParamVm(item) : CreateHeaderVm(item);

    private KeyValueItemViewModel CreateParamVm(KeyValueItem item) =>
        new(item, OnParamFieldChanged);

    private KeyValueItemViewModel CreateHeaderVm(KeyValueItem item) =>
        new(item, OnHeaderFieldChanged);

    private void OnParamFieldChanged(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? oldValue,
        object? newValue)
    {
        if (!_syncingUrlAndParams)
        {
            MarkDirty();
            SyncAddressFromParams();
        }

        EnsureParamSpareRow();
        RecordFieldUndo(KeyValueListKind.Params, item, field, oldValue, newValue);
    }

    private void OnHeaderFieldChanged(
        KeyValueItemViewModel item,
        KeyValueField field,
        object? oldValue,
        object? newValue)
    {
        MarkDirty();
        EnsureHeaderSpareRow();
        RecordFieldUndo(KeyValueListKind.Headers, item, field, oldValue, newValue);
    }

    private void RecordFieldUndo(
        KeyValueListKind listKind,
        KeyValueItemViewModel item,
        KeyValueField field,
        object? oldValue,
        object? newValue)
    {
        if (_suppressFieldUndo || _syncingUrlAndParams || _ensuringSpare
            || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        var label = listKind == KeyValueListKind.Params ? "param" : "header";
        var description = field switch
        {
            KeyValueField.IsEnabled => $"Toggle {label} '{item.Key}'",
            KeyValueField.Key => $"Edit {label} key",
            KeyValueField.Value => $"Edit {label} value",
            _ => $"Edit {label}"
        };

        _undoRecorder.Record(
            new SetKeyValueFieldAction(this, listKind, item, field, oldValue, description));
    }

    private void RecordRowRemoveUndo(
        KeyValueListKind listKind,
        KeyValueItemViewModel item,
        int index,
        string description)
    {
        if (_suppressFieldUndo || _undoRecorder is null || _undoRecorder.IsApplyingHistory)
        {
            return;
        }

        if (index < 0)
        {
            return;
        }

        _undoRecorder.Record(
            new InsertKeyValueRowAction(this, listKind, item, index, description));
    }

    private void SyncParamsFromAddress(string address)
    {
        if (_syncingUrlAndParams || _ensuringSpare)
        {
            return;
        }

        if (!TryGetQueryPairs(address, out var pairs))
        {
            return;
        }

        if (ParamsMatchQueryPairs(pairs))
        {
            return;
        }

        _syncingUrlAndParams = true;
        try
        {
            Params.Clear();
            Model.Params.Clear();

            foreach (var (key, value) in pairs)
            {
                var item = new KeyValueItem
                {
                    IsEnabled = true,
                    Key = key,
                    Value = value
                };
                Model.Params.Add(item);
                Params.Add(CreateParamVm(item));
            }

            EnsureParamSpareRow();
        }
        finally
        {
            _syncingUrlAndParams = false;
        }
    }

    private bool ParamsMatchQueryPairs(IReadOnlyList<(string Key, string Value)> pairs)
    {
        var existing = Params.Where(p => !string.IsNullOrWhiteSpace(p.Key)).ToList();
        if (existing.Count != pairs.Count)
        {
            return false;
        }

        for (var i = 0; i < pairs.Count; i++)
        {
            if (!string.Equals(existing[i].Key, pairs[i].Key, StringComparison.Ordinal)
                || !string.Equals(existing[i].Value, pairs[i].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void SyncAddressFromParams()
    {
        if (_syncingUrlAndParams)
        {
            return;
        }

        var current = Address ?? string.Empty;
        var baseAddress = StripQuery(current);
        if (string.IsNullOrWhiteSpace(baseAddress) && string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(baseAddress))
        {
            baseAddress = StripQuery(current);
        }

        var query = BuildQueryString(
            Params.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)));

        var next = string.IsNullOrEmpty(query)
            ? baseAddress
            : $"{baseAddress}?{query}";

        if (string.Equals(next, Address, StringComparison.Ordinal))
        {
            Model.Address = next;
            return;
        }

        _syncingUrlAndParams = true;
        try
        {
            Address = next;
            Model.Address = next;
        }
        finally
        {
            _syncingUrlAndParams = false;
        }
    }

    private static string BuildQueryString(IEnumerable<KeyValueItemViewModel> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            sb.Append(VariableResolver.EscapeDataStringPreservingTokens(item.Key));
            sb.Append('=');
            sb.Append(VariableResolver.EscapeDataStringPreservingTokens(item.Value ?? string.Empty));
        }

        return sb.ToString();
    }

    private static string StripQuery(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(address.Trim(), UriKind.Absolute, out var absolute))
        {
            var builder = new UriBuilder(absolute)
            {
                Query = string.Empty
            };
            // UriBuilder may append a trailing '?' or empty query quirks; normalize.
            var built = builder.Uri.GetLeftPart(UriPartial.Path);
            if (!string.IsNullOrEmpty(absolute.Fragment))
            {
                built += absolute.Fragment;
            }

            return built;
        }

        var text = address.Trim();
        var queryIndex = text.IndexOf('?');
        if (queryIndex < 0)
        {
            return text;
        }

        var fragmentIndex = text.IndexOf('#', queryIndex);
        if (fragmentIndex >= 0)
        {
            return text[..queryIndex] + text[fragmentIndex..];
        }

        return text[..queryIndex];
    }

    private static bool TryGetQueryPairs(string address, out List<(string Key, string Value)> pairs)
    {
        pairs = [];
        if (string.IsNullOrWhiteSpace(address))
        {
            return true;
        }

        string? query = null;
        var trimmed = address.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            query = absolute.Query;
        }
        else
        {
            var queryIndex = trimmed.IndexOf('?');
            if (queryIndex >= 0)
            {
                var fragmentIndex = trimmed.IndexOf('#', queryIndex);
                query = fragmentIndex >= 0
                    ? trimmed[queryIndex..fragmentIndex]
                    : trimmed[queryIndex..];
            }
        }

        if (string.IsNullOrEmpty(query) || query == "?")
        {
            return true;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                pairs.Add((Uri.UnescapeDataString(part.Replace('+', ' ')), string.Empty));
            }
            else
            {
                var key = Uri.UnescapeDataString(part[..eq].Replace('+', ' '));
                var value = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
                pairs.Add((key, value));
            }
        }

        return true;
    }

    /// <summary>Ensures params/headers each have a single trailing blank spare row.</summary>
    public void NormalizeSpareRows()
    {
        EnsureParamSpareRow();
        EnsureHeaderSpareRow();
    }

    public void OnKeyValueListReordered(ObservableCollection<KeyValueItemViewModel> list)
    {
        if (ReferenceEquals(list, Params))
        {
            SyncList(Model.Params, Params);
            MarkDirty();
            EnsureParamSpareRow();
            SyncAddressFromParams();
            return;
        }

        if (ReferenceEquals(list, Headers))
        {
            SyncList(Model.Headers, Headers);
            MarkDirty();
            EnsureHeaderSpareRow();
        }
    }

    public bool TryApplyKeyValueOrder(
        ObservableCollection<KeyValueItemViewModel> list,
        IReadOnlyList<KeyValueItemViewModel> newOrder)
    {
        KeyValueListKind listKind;
        string description;
        if (ReferenceEquals(list, Params))
        {
            listKind = KeyValueListKind.Params;
            description = "Reorder params";
        }
        else if (ReferenceEquals(list, Headers))
        {
            listKind = KeyValueListKind.Headers;
            description = "Reorder headers";
        }
        else
        {
            return false;
        }

        if (KeyValueListReorder.SameOrder(list, newOrder))
        {
            return false;
        }

        var previous = list.ToArray();
        KeyValueListReorder.ApplyOrder(list, newOrder);
        OnKeyValueListReordered(list);

        if (!_suppressFieldUndo && _undoRecorder is not null && !_undoRecorder.IsApplyingHistory)
        {
            _undoRecorder.Record(
                new ReorderKeyValueListAction(this, listKind, previous, description));
        }

        return true;
    }

    private void EnsureParamSpareRow()
    {
        if (!_ensuringSpare && KeyValueSpareRowHelper.HasValidTrailingSpare(Params))
        {
            return;
        }

        KeyValueSpareRowHelper.EnsureTrailingSpare(
            Params,
            Model.Params,
            CreateParamVm,
            ref _ensuringSpare);
    }

    private void EnsureHeaderSpareRow()
    {
        if (!_ensuringSpare && KeyValueSpareRowHelper.HasValidTrailingSpare(Headers))
        {
            return;
        }

        KeyValueSpareRowHelper.EnsureTrailingSpare(
            Headers,
            Model.Headers,
            CreateHeaderVm,
            ref _ensuringSpare);
    }

    private void OnParamsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncingUrlAndParams || _ensuringSpare)
        {
            return;
        }

        if (e.Action is NotifyCollectionChangedAction.Reset)
        {
            SyncList(Model.Params, Params);
            MarkDirty();
            EnsureParamSpareRow();
            SyncAddressFromParams();
        }
    }

    private void OnHeadersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Reset)
        {
            SyncList(Model.Headers, Headers);
            MarkDirty();
            EnsureHeaderSpareRow();
        }
    }

    private static void SyncList(List<KeyValueItem> modelList, ObservableCollection<KeyValueItemViewModel> viewModels)
    {
        modelList.Clear();
        foreach (var vm in viewModels)
        {
            modelList.Add(vm.Model);
        }
    }
}
