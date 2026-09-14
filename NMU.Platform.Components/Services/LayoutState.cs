using Microsoft.AspNetCore.Components;

namespace NMU.Platform.Components.Services;

public class LayoutState
{
    public RenderFragment? SearchBar { get; set; }
    public RenderFragment? BottomBar { get; set; }
    public event Action? StateChanged;
    public event Action? SettingsRequested;
    public event Action? CacheRequested;
    public event Action? RefreshRequested;
    public bool PendingSettings { get; set; }
    public bool PendingCache { get; set; }
    public bool SidebarOpen { get; set; }
    public bool SidebarHidden { get; set; }
    public bool BottomNavHidden { get; set; }

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

    public void HideSidebar()
    {
        if (SidebarHidden) return;
        SidebarHidden = true;
        NotifyStateChanged();
    }

    public void ShowSidebar()
    {
        if (!SidebarHidden) return;
        SidebarHidden = false;
        NotifyStateChanged();
    }

    public void HideBottomNav()
    {
        if (BottomNavHidden) return;
        BottomNavHidden = true;
        NotifyStateChanged();
    }

    public void ShowBottomNav()
    {
        if (!BottomNavHidden) return;
        BottomNavHidden = false;
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
    public void RequestRefresh() => RefreshRequested?.Invoke();

    public void NotifyStateChanged() => StateChanged?.Invoke();
}
