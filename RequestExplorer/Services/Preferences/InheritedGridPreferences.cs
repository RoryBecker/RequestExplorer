namespace RequestExplorer.Services.Preferences;

/// <summary>
/// Live bridge so editor view-models can react to inherited-grid preferences without a Window binding.
/// </summary>
public static class InheritedGridPreferences
{
    private static bool _showEmptyInheritedGrids;
    private static bool _enableInheritBreakControl;

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

    /// <summary>
    /// When true, Inherit checkboxes are shown and <see cref="Models.KeyValueItem.IsInheritable"/>
    /// false breaks the inheritance chain. When false, all rows behave as inheritable.
    /// </summary>
    public static bool EnableInheritBreakControl
    {
        get => _enableInheritBreakControl;
        set
        {
            if (_enableInheritBreakControl == value)
            {
                return;
            }

            _enableInheritBreakControl = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Effective inheritable flag for resolution (feature off ⇒ always true).</summary>
    public static bool IsEffectivelyInheritable(bool isInheritable) =>
        !_enableInheritBreakControl || isInheritable;
}
