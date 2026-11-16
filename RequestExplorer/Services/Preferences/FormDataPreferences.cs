namespace RequestExplorer.Services.Preferences;

/// <summary>Live bridge for form-data file size limits.</summary>
public static class FormDataPreferences
{
    public const int DefaultMaxFileMegabytes = 10;
    public const int MinMaxFileMegabytes = 1;
    public const int MaxMaxFileMegabytes = 500;

    private static int _maxFileMegabytes = DefaultMaxFileMegabytes;

    public static event Action? Changed;

    public static int MaxFileMegabytes
    {
        get => _maxFileMegabytes;
        set
        {
            var clamped = ClampMaxFileMegabytes(value);
            if (_maxFileMegabytes == clamped)
            {
                return;
            }

            _maxFileMegabytes = clamped;
            Changed?.Invoke();
        }
    }

    public static long MaxFileBytes => (long)MaxFileMegabytes * 1024L * 1024L;

    public static int ClampMaxFileMegabytes(int value) =>
        Math.Clamp(value, MinMaxFileMegabytes, MaxMaxFileMegabytes);
}
