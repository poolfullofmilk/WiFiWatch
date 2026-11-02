using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor;

namespace WiFiWatch.Desktop.Components.Shared.Common;

public sealed class NumberField<T> : MudNumericField<T>
{
    public NumberField()
    {
        // Every Number Field In The App Is Outlined
        Variant = Variant.Outlined;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // A Boxless Wrapper Tells The Spin Buttons Their Limit
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", LimitClass);
        builder.OpenRegion(2);
        base.BuildRenderTree(builder);
        builder.CloseRegion();
        builder.CloseElement();
    }

    private string LimitClass
    {
        get
        {
            var value = ReadValue;
            if (value is null)
                return "number-field";

            var atMin = Comparer<T>.Default.Compare(value, Min) <= 0;
            var atMax = Comparer<T>.Default.Compare(value, Max) >= 0;
            return $"number-field{(atMin ? " at-min" : "")}{(atMax ? " at-max" : "")}";
        }
    }
}
