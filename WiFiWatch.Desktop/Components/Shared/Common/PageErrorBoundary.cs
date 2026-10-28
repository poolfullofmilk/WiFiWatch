using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using WiFiWatch.Data.Localization;
using WiFiWatch.Services.Monitoring;

namespace WiFiWatch.Desktop.Components.Shared.Common;

// A Failing Page Is Logged Like Any Other Failure And Recovers On OK Or A Page Switch
public sealed class PageErrorBoundary : ErrorBoundaryBase
{
    [Inject]
    public required NetworkMonitor Monitor { get; set; }

    protected override Task OnErrorAsync(Exception exception) =>
        Monitor.RecordFailureAsync(AppText.Get("App Failed {0}", exception.GetType().Name));

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        builder.AddContent(
            0,
            CurrentException is { } exception ? ErrorContent?.Invoke(exception) : ChildContent
        );
}
