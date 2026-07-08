using Microsoft.AspNetCore.Components;

namespace NMU.Platform.Components.Services;

public class LayoutState
{
    public RenderFragment? SearchBar { get; set; }
    public RenderFragment? BottomBar { get; set; }
    public event Action? StateChanged;

    public void Clear()
    {
        SearchBar = null;
        BottomBar = null;
        NotifyStateChanged();
    }

    public void NotifyStateChanged() => StateChanged?.Invoke();
}
