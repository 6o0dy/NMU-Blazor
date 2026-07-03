using Microsoft.JSInterop;

namespace NMU.Platform.Components.Services;

public class FullscreenService
{
    private readonly IJSRuntime _js;
    public FullscreenService(IJSRuntime js) => _js = js;

    public async Task ToggleAsync()
    {
        try { await _js.InvokeVoidAsync("nmuFunctions.toggleFullScreen"); }
        catch { }
    }
}
