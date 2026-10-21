namespace RequestExplorer.Services.Tree;

/// <summary>Session-only back/forward history of tree node ids (browser-style).</summary>
public sealed class TreeNavigationHistory
{
    private const int MaxEntries = 100;
    private readonly List<Guid> _entries = [];
    private int _index = -1;

    public bool CanGoBack => _index > 0;

    public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;

    /// <summary>Records a user navigation to <paramref name="id"/>, clearing any forward entries.</summary>
    public void NavigateTo(Guid id)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        if (_index >= 0 && _index < _entries.Count && _entries[_index] == id)
        {
            return;
        }

        if (_index < _entries.Count - 1)
        {
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        }

        _entries.Add(id);
        _index = _entries.Count - 1;
        TrimFront();
    }

    public bool TryGoBack(out Guid id)
    {
        id = default;
        if (!CanGoBack)
        {
            return false;
        }

        _index--;
        id = _entries[_index];
        return true;
    }

    public bool TryGoForward(out Guid id)
    {
        id = default;
        if (!CanGoForward)
        {
            return false;
        }

        _index++;
        id = _entries[_index];
        return true;
    }

    /// <summary>Removes the entry at the current index (e.g. deleted node) and clamps the cursor.</summary>
    public void RemoveCurrent()
    {
        if (_index < 0 || _index >= _entries.Count)
        {
            return;
        }

        _entries.RemoveAt(_index);
        if (_entries.Count == 0)
        {
            _index = -1;
            return;
        }

        if (_index >= _entries.Count)
        {
            _index = _entries.Count - 1;
        }
    }

    public bool TryGetCurrent(out Guid id)
    {
        id = default;
        if (_index < 0 || _index >= _entries.Count)
        {
            return false;
        }

        id = _entries[_index];
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        _index = -1;
    }

    private void TrimFront()
    {
        while (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(0);
            _index--;
        }

        if (_index < 0 && _entries.Count > 0)
        {
            _index = 0;
        }
    }
}
