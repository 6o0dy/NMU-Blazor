namespace NMU.Platform.Components.Services;

public class NavigationState
{
    public string? CurrentFileUrl { get; set; }
    public string? CurrentFileName { get; set; }
    public string PageTitle { get; set; } = "NMU-CE & AIE";
    public bool IsFullScreen { get; set; }
    public string? YouTubeVideoId { get; set; }
    public string? YouTubeVideoTitle { get; set; }
}
