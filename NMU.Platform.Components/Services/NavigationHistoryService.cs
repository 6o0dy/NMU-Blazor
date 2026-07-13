namespace NMU.Platform.Components.Services;

public class NavigationHistoryService
{
    private readonly List<string> _history = new();
    private const int MaxHistory = 50;

    public IReadOnlyList<string> History => _history.AsReadOnly();

    public void NavigatedTo(string url)
    {
        if (_history.Count > 0 && _history[^1] == url) return;
        _history.Add(url);
        if (_history.Count > MaxHistory)
            _history.RemoveAt(0);
    }

    public string? Pop()
    {
        if (_history.Count <= 0) return null;
        _history.RemoveAt(_history.Count - 1);
        return _history.Count > 0 ? _history[^1] : null;
    }

    public void Clear()
    {
        _history.Clear();
    }
}
