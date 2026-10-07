namespace WifiWatch.Data.ViewModels;

public sealed record TimelineBar(
    string Label,
    decimal Start,
    decimal End,
    string Message,
    bool IsCritical
);
