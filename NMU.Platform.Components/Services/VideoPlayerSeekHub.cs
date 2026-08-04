using Microsoft.JSInterop;

namespace NMU.Platform.Components.Services;

public static class VideoPlayerSeekHub
{
    public static Action<double, bool>? Seek;
    public static Action<double, double, string, string, string>? Tick;

    [JSInvokable]
    public static void OnSeekFromJs(double time, bool committed)
        => Seek?.Invoke(time, committed);

    [JSInvokable]
    public static void OnTimestateFromJs(double current, double duration, string error, string ready, string network)
        => Tick?.Invoke(current, duration, error, ready, network);
}
