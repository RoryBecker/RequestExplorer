using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using RequestExplorer.Services.Tree;

namespace RequestExplorer.ViewModels.Nodes;

public enum TreeNodeKind
{
    Collection,
    Folder,
    Request
}

public enum TreeDropIndicator
{
    None,
    Before,
    After,
    Into
}

public abstract partial class TreeNodeViewModel : ObservableObject
{
    /// <summary>Raised when a node's name/URL (or similar) may change filter matching.</summary>
    public static event Action? SearchTextInvalidated;

    /// <summary>Active scope used when building <see cref="SearchText"/>.</summary>
    public static TreeSearchScope SearchScope { get; set; } = TreeSearchScope.All;

    protected TreeNodeViewModel(string name)
    {
        _name = name;
        Children = [];
    }

    public abstract TreeNodeKind Kind { get; }

    /// <summary>Ancestor chain including this node, e.g. <c>Collection / Folder / Request</c>.</summary>
    public string HierarchyPath => string.Join(" / ", EnumeratePathNames());

    public TreeNodeViewModel? Parent
    {
        get => _parent;
        set
        {
            if (ReferenceEquals(_parent, value))
            {
                return;
            }

            _parent = value;
            OnPropertyChanged(nameof(Parent));
            OnPropertyChanged(nameof(HierarchyPath));
            // Indent / row width depends on parent chain; force row layout refresh.
            OnPropertyChanged(nameof(RowLayoutToken));
            OnPropertyChanged(nameof(SearchText));
            NotifyAutoRunExclusionChanged();
        }
    }

    private TreeNodeViewModel? _parent;

    /// <summary>Changes when parentage changes so row-width bindings re-evaluate.</summary>
    public object? RowLayoutToken => Parent;

    public void NotifyRowLayoutChanged() => OnPropertyChanged(nameof(RowLayoutToken));

    public ObservableCollection<TreeNodeViewModel> Children { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True when this node is part of the multi-selection set (including the primary).</summary>
    [ObservableProperty]
    private bool _isInSelection;

    [ObservableProperty]
    private TreeDropIndicator _dropIndicator;

    /// <summary>False when a tree filter is active and this node (and its descendants) do not match.</summary>
    [ObservableProperty]
    private bool _isVisibleInFilter = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    private string _name;

    /// <summary>True when this request (or a descendant) has unsaved edits. Wired fully in Phase 3.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(ShowsDirtyMarker))]
    private bool _isDirty;

    public bool ContainsDirtyDescendant =>
        Children.Any(c => c.IsDirty || c.ContainsDirtyDescendant);

    public bool ShowsDirtyMarker => IsDirty || ContainsDirtyDescendant;

    public string DisplayName => ShowsDirtyMarker ? $"{Name} *" : Name;

    /// <summary>True when this collection/folder/request itself opts out of Run All.</summary>
    public virtual bool LocallyOptsOutOfAutoRun => false;

    /// <summary>
    /// True when this node or an ancestor collection/folder/request opts out of Run All.
    /// </summary>
    public bool IsExcludedFromAutoRun
    {
        get
        {
            for (var node = this; node is not null; node = node.Parent)
            {
                if (node.LocallyOptsOutOfAutoRun)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public string? AutoRunExclusionToolTip =>
        IsExcludedFromAutoRun ? "Opted out of auto run" : null;

    public void NotifyAutoRunExclusionChanged()
    {
        OnPropertyChanged(nameof(LocallyOptsOutOfAutoRun));
        OnPropertyChanged(nameof(IsExcludedFromAutoRun));
        OnPropertyChanged(nameof(AutoRunExclusionToolTip));
        foreach (var child in Children)
        {
            child.NotifyAutoRunExclusionChanged();
        }
    }

    /// <summary>
    /// Filterable text built from the active <see cref="SearchScope"/> components
    /// (ancestor path, name, and/or URL).
    /// </summary>
    public string SearchText
    {
        get
        {
            var scope = SearchScope;
            var builder = new StringBuilder();

            if (scope.IncludeAncestors || scope.IncludeName)
            {
                var path = BuildScopedPath(scope);
                if (!string.IsNullOrEmpty(path))
                {
                    builder.Append(path);
                }
            }

            if (scope.IncludeUrl)
            {
                var url = GetSearchUrl();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(url.Trim());
                }
            }

            return builder.ToString();
        }
    }

    public void NotifyDirtyPresentationChanged()
    {
        OnPropertyChanged(nameof(ContainsDirtyDescendant));
        OnPropertyChanged(nameof(ShowsDirtyMarker));
        OnPropertyChanged(nameof(DisplayName));
        Parent?.NotifyDirtyPresentationChanged();
    }

    partial void OnIsDirtyChanged(bool value) => NotifyDirtyPresentationChanged();

    partial void OnNameChanged(string value)
    {
        OnNameUpdated(value);
        NotifySearchTextChanged();
        RaiseSearchTextInvalidated();
    }

    protected virtual void OnNameUpdated(string value)
    {
    }

    /// <summary>Optional URL (or similar) appended to <see cref="SearchText"/>.</summary>
    protected virtual string? GetSearchUrl() => null;

    public void NotifySearchTextChanged()
    {
        OnPropertyChanged(nameof(SearchText));
        foreach (var child in Children)
        {
            child.NotifySearchTextChanged();
        }
    }

    protected static void RaiseSearchTextInvalidated() => SearchTextInvalidated?.Invoke();

    private string BuildScopedPath(TreeSearchScope scope)
    {
        if (scope.IncludeAncestors && scope.IncludeName)
        {
            return string.Join('/', EnumeratePathNames());
        }

        if (scope.IncludeAncestors)
        {
            return string.Join('/', EnumerateAncestorNames());
        }

        return Name ?? string.Empty;
    }

    private IEnumerable<string> EnumerateAncestorNames()
    {
        if (Parent is null)
        {
            yield break;
        }

        foreach (var name in Parent.EnumeratePathNames())
        {
            yield return name;
        }
    }

    private IEnumerable<string> EnumeratePathNames()
    {
        if (Parent is not null)
        {
            foreach (var name in Parent.EnumeratePathNames())
            {
                yield return name;
            }
        }

        yield return Name ?? string.Empty;
    }

    public void AddChild(TreeNodeViewModel child)
    {
        child.Parent = this;
        Children.Add(child);
        NotifyDirtyPresentationChanged();
        RaiseSearchTextInvalidated();
    }

    public void InsertChild(TreeNodeViewModel child, int index)
    {
        child.Parent = this;
        if (index < 0 || index > Children.Count)
        {
            index = Children.Count;
        }

        Children.Insert(index, child);
        NotifyDirtyPresentationChanged();
        RaiseSearchTextInvalidated();
    }

    public bool RemoveChild(TreeNodeViewModel child)
    {
        if (!Children.Remove(child))
        {
            return false;
        }

        child.Parent = null;
        NotifyDirtyPresentationChanged();
        RaiseSearchTextInvalidated();
        return true;
    }

    public int CountDescendants()
    {
        var count = 0;
        foreach (var child in Children)
        {
            count += 1 + child.CountDescendants();
        }

        return count;
    }
}
