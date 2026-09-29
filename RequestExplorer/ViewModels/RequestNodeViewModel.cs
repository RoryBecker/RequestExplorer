using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

public sealed partial class RequestNodeViewModel : TreeNodeViewModel
{
    private bool _trackDirty = true;

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
            Params.Add(new KeyValueItemViewModel(item, MarkDirty));
        }

        foreach (var item in model.Headers)
        {
            Headers.Add(new KeyValueItemViewModel(item, MarkDirty));
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
        if (!_trackDirty)
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
        MarkDirty();
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
    private void AddParam()
    {
        var item = new KeyValueItem();
        Model.Params.Add(item);
        Params.Add(new KeyValueItemViewModel(item, MarkDirty));
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveParam(KeyValueItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Model.Params.Remove(item.Model);
        Params.Remove(item);
        MarkDirty();
    }

    [RelayCommand]
    private void AddHeader()
    {
        var item = new KeyValueItem();
        Model.Headers.Add(item);
        Headers.Add(new KeyValueItemViewModel(item, MarkDirty));
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveHeader(KeyValueItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Model.Headers.Remove(item.Model);
        Headers.Remove(item);
        MarkDirty();
    }

    private void OnParamsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Add/Remove commands keep Model in sync; this covers unexpected mutations.
        if (e.Action is NotifyCollectionChangedAction.Reset)
        {
            SyncList(Model.Params, Params);
            MarkDirty();
        }
    }

    private void OnHeadersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Reset)
        {
            SyncList(Model.Headers, Headers);
            MarkDirty();
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
