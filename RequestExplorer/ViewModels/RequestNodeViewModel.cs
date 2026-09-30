using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RequestExplorer.Models;
using RequestExplorer.Services;
using RequestExplorer.Services.Undo;

namespace RequestExplorer.ViewModels;

public sealed partial class RequestNodeViewModel : TreeNodeViewModel
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
    }

    public void AttachUndoRecorder(IUndoRecorder recorder) => _undoRecorder = recorder;

    public override TreeNodeKind Kind => TreeNodeKind.Request;

    public RequestDefinition Model { get; }

    public static Array VerbOptions { get; } = Enum.GetValues(typeof(HttpVerb));
    public static Array AuthTypeOptions { get; } = Enum.GetValues(typeof(AuthType));
    public static Array ApiKeyLocationOptions { get; } = Enum.GetValues(typeof(ApiKeyLocation));

    public ObservableCollection<KeyValueItemViewModel> Params { get; } = [];
    public ObservableCollection<KeyValueItemViewModel> Headers { get; } = [];

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

    public bool IsAuthNone => AuthType == AuthType.None;
    public bool IsAuthBearer => AuthType == AuthType.Bearer;
    public bool IsAuthBasic => AuthType == AuthType.Basic;
    public bool IsAuthApiKey => AuthType == AuthType.ApiKey;

    public void MarkDirty()
    {
        if (!_trackDirty || _ensuringSpare || _syncingUrlAndParams)
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
        Model.Verb = value;
        MarkDirty();
    }

    protected override string? GetSearchUrl() => Address;

    partial void OnAddressChanged(string value)
    {
        Model.Address = value;
        OnPropertyChanged(nameof(SearchText));
        if (_syncingUrlAndParams)
        {
            return;
        }

        MarkDirty();
        SyncParamsFromAddress(value);
        RaiseSearchTextInvalidated();
    }

    partial void OnAuthTypeChanged(AuthType value)
    {
        Model.Authentication.Type = value;
        MarkDirty();
    }

    partial void OnBearerTokenChanged(string value)
    {
        Model.Authentication.BearerToken = value;
        MarkDirty();
    }

    partial void OnBasicUsernameChanged(string value)
    {
        Model.Authentication.BasicUsername = value;
        MarkDirty();
    }

    partial void OnBasicPasswordChanged(string value)
    {
        Model.Authentication.BasicPassword = value;
        MarkDirty();
    }

    partial void OnApiKeyNameChanged(string value)
    {
        Model.Authentication.ApiKeyName = value;
        MarkDirty();
    }

    partial void OnApiKeyValueChanged(string value)
    {
        Model.Authentication.ApiKeyValue = value;
        MarkDirty();
    }

    partial void OnApiKeyLocationChanged(ApiKeyLocation value)
    {
        Model.Authentication.ApiKeyLocation = value;
        MarkDirty();
    }

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

    private void EnsureParamSpareRow()
    {
        if (_ensuringSpare)
        {
            return;
        }

        if (Params.Count > 0 && string.IsNullOrWhiteSpace(Params[^1].Key))
        {
            return;
        }

        _ensuringSpare = true;
        try
        {
            var item = new KeyValueItem { IsEnabled = false };
            Model.Params.Add(item);
            Params.Add(CreateParamVm(item));
        }
        finally
        {
            _ensuringSpare = false;
        }
    }

    private void EnsureHeaderSpareRow()
    {
        if (_ensuringSpare)
        {
            return;
        }

        if (Headers.Count > 0 && string.IsNullOrWhiteSpace(Headers[^1].Key))
        {
            return;
        }

        _ensuringSpare = true;
        try
        {
            var item = new KeyValueItem { IsEnabled = false };
            Model.Headers.Add(item);
            Headers.Add(CreateHeaderVm(item));
        }
        finally
        {
            _ensuringSpare = false;
        }
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
