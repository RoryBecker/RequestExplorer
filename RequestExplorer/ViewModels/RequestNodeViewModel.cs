using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

public sealed partial class RequestNodeViewModel : TreeNodeViewModel
{
    private bool _trackDirty = true;
    private bool _ensuringSpare;
    private bool _syncingUrlAndParams;

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

    partial void OnAddressChanged(string value)
    {
        Model.Address = value;
        if (_syncingUrlAndParams)
        {
            return;
        }

        MarkDirty();
        SyncParamsFromAddress(value);
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
        Model.Body.Content = value;
        MarkDirty();
    }

    partial void OnBodyContentTypeChanged(string value)
    {
        Model.Body.ContentType = string.IsNullOrWhiteSpace(value) ? null : value;
        MarkDirty();
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

        Model.Params.Remove(item.Model);
        Params.Remove(item);
        MarkDirty();
        EnsureParamSpareRow();
        SyncAddressFromParams();
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

        Model.Headers.Remove(item.Model);
        Headers.Remove(item);
        MarkDirty();
        EnsureHeaderSpareRow();
    }

    private KeyValueItemViewModel CreateParamVm(KeyValueItem item) =>
        new(item, OnParamChanged);

    private KeyValueItemViewModel CreateHeaderVm(KeyValueItem item) =>
        new(item, OnHeaderChanged);

    private void OnParamChanged()
    {
        if (!_syncingUrlAndParams)
        {
            MarkDirty();
            SyncAddressFromParams();
        }

        EnsureParamSpareRow();
    }

    private void OnHeaderChanged()
    {
        MarkDirty();
        EnsureHeaderSpareRow();
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

            sb.Append(Uri.EscapeDataString(item.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(item.Value ?? string.Empty));
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
            var item = new KeyValueItem();
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
            var item = new KeyValueItem();
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
