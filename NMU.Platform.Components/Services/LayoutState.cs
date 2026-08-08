using Microsoft.AspNetCore.Components;

namespace NMU.Platform.Components.Services;

public class LayoutState
{
    public RenderFragment? SearchBar { get; set; }
    public RenderFragment? BottomBar { get; set; }
    public event Action? StateChanged;
    public event Action? SettingsRequested;
    public event Action? CacheRequested;
    public bool PendingSettings { get; set; }
    public bool PendingCache { get; set; }
    public bool SidebarOpen { get; set; }

    public void ToggleSidebar()
    {
        SidebarOpen = !SidebarOpen;
        NotifyStateChanged();
    }

    public void OpenSidebar()
    {
        if (SidebarOpen) return;
        SidebarOpen = true;
        NotifyStateChanged();
    }

    public void CloseSidebar()
    {
        if (!SidebarOpen) return;
        SidebarOpen = false;
        NotifyStateChanged();
    }

    public void Clear()
    {
        SearchBar = null;
        BottomBar = null;
        NotifyStateChanged();
    }

    public void RequestSettings() => SettingsRequested?.Invoke();
    public void RequestClearCache() => CacheRequested?.Invoke();

    public void NotifyStateChanged() => StateChanged?.Invoke();
}
