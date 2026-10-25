using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

internal static class EditorConverterTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Editor enum selections round-trip the declared enum type and value", () =>
        {
            var converter = new EnumIndexConverter();
            Type[] types = [typeof(HorizontalAlignment), typeof(VerticalAlignment), typeof(Orientation), typeof(LayoutKind), typeof(GridElementMode), typeof(BackgroundMode), typeof(SceneEditorTab), typeof(SparseSelection)];
            foreach (var type in types)
            {
                var values = Enum.GetValues(type);
                for (var index = 0; index < values.Length; index++)
                {
                    var value = values.GetValue(index)!;
                    Check(Equals(converter.Convert(value, typeof(int), null!, ""), index), "Enum-to-index mapping failed.");
                    var restored = converter.ConvertBack(index, type, null!, "");
                    Check(restored.GetType() == type && Equals(restored, value), "Selection changed the enum type or value.");
                }
            }
        });
        run("Transient empty and out-of-range selections are rejected without resetting a saved alignment", () =>
        {
            var converter = new EnumIndexConverter();
            foreach (var index in new[]
            {
                -1,
                int.MinValue,
                4,
                int.MaxValue
            }

            )
            {
                Check(ReferenceEquals(converter.ConvertBack(index, typeof(HorizontalAlignment), null!, ""), DependencyProperty.UnsetValue), "Invalid selection produced a default alignment instead of suppressing the write.");
            }

            Check(ReferenceEquals(converter.ConvertBack("1", typeof(HorizontalAlignment), null!, ""), DependencyProperty.UnsetValue), "An invalid input type was accepted.");
            Check(ReferenceEquals(converter.ConvertBack(1, typeof(object), null!, ""), DependencyProperty.UnsetValue), "A non-enum destination was accepted.");
        });
        run("Unset and invalid enum sources produce a safe selector index", () =>
        {
            var converter = new EnumIndexConverter();
            foreach (var value in new object?[]
            {
                null,
                DependencyProperty.UnsetValue,
                new object (),
                "Left",
                0
            }

            )
            {
                Check(Equals(converter.Convert(value!, typeof(int), null!, ""), -1), "Invalid enum source could not be displayed safely.");
            }

            Check(Equals(converter.Convert((HorizontalAlignment)99, typeof(int), null!, ""), -1), "An undefined enum value produced a valid index.");
        });
        run("Empty or non-finite numeric input suppresses the write and valid input retains its type", () =>
        {
            var converter = new FiniteNumberConverter();
            foreach (var value in new[]
            {
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity
            }

            )
            {
                Check(ReferenceEquals(converter.ConvertBack(value, typeof(double), null!, ""), DependencyProperty.UnsetValue), "Invalid numeric input was written to a setting.");
            }

            Check(converter.ConvertBack(12.5, typeof(double), null!, "") is double number && number == 12.5, "A valid double lost precision or changed type.");
            Check(converter.ConvertBack(12.0, typeof(int), null!, "") is int integer && integer == 12, "A grid index was not returned as an integer.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private enum SparseSelection
    {
        First = 10,
        Second = 25,
        Third = 100
    }
}