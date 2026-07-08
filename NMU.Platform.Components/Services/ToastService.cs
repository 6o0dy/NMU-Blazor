namespace NMU.Platform.Components.Services;

public enum ToastType
{
    Success,
    Cancelled,
    Error
}

public class ToastService
{
    public event Action<(string message, ToastType type)>? Show;
    public void ShowToast(string message, ToastType type = ToastType.Success)
        => Show?.Invoke((message, type));
}
