using RequestExplorer.Models;

namespace RequestExplorer.ViewModels;

public sealed class RequestNodeViewModel : TreeNodeViewModel
{
    public RequestNodeViewModel(RequestDefinition model)
        : base(model.Name)
    {
        Model = model;
    }

    public override TreeNodeKind Kind => TreeNodeKind.Request;

    public RequestDefinition Model { get; }

    public HttpVerb Verb
    {
        get => Model.Verb;
        set
        {
            if (Model.Verb == value)
            {
                return;
            }

            Model.Verb = value;
            OnPropertyChanged();
        }
    }

    public string Address
    {
        get => Model.Address;
        set
        {
            if (Model.Address == value)
            {
                return;
            }

            Model.Address = value;
            OnPropertyChanged();
        }
    }

    protected override void OnNameUpdated(string value) => Model.Name = value;
}
