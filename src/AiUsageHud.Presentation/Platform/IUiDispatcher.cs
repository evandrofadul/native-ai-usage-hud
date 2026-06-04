namespace AiUsageHud.Presentation.Platform;

/// <summary>
/// UI-thread services the shared view models need from the hosting framework. The
/// hosting head (Avalonia) supplies the implementation so the view models stay free
/// of any UI-framework reference.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Create a timer whose <see cref="IUiTimer.Tick"/> fires on the UI thread.</summary>
    IUiTimer CreateTimer(TimeSpan interval);
}

/// <summary>A recurring timer that raises <see cref="Tick"/> on the UI thread.</summary>
public interface IUiTimer
{
    event Action Tick;
    void Start();
    void Stop();
}
