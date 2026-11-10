namespace RequestExplorer.Services.Preferences;

/// <summary>
/// Live bridge so editor view-models can show empty Inherited panels without a Window binding.
/// </summary>
public static class InheritedGridPreferences
{
    private static bool _showEmptyInheritedGrids;

    public static event Action? Changed;

    public static bool ShowEmptyInheritedGrids
    {
        get => _showEmptyInheritedGrids;
        set
        {
            if (_showEmptyInheritedGrids == value)
            {
                return;
            }

            _showEmptyInheritedGrids = value;
            Changed?.Invoke();
        }
    }
}
