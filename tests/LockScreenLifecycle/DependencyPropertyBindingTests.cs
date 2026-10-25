using System.ComponentModel;
using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

internal static class DependencyPropertyBindingTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Property bindings immediately synchronize and remain one-way", () =>
        {
            var source = new Source { Value = 24 };
            var target = new TextBlock();
            using var binding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, target, TextBlock.FontSizeProperty);

            Check((double)target.GetValue(TextBlock.FontSizeProperty)! == 24,
                "The initial source value was not synchronized.");
            source.Value = 32;
            Check((double)target.GetValue(TextBlock.FontSizeProperty)! == 32,
                "A source update was not synchronized.");
            target.SetValue(TextBlock.FontSizeProperty, 48d);
            Check(source.Value == 32, "A target update changed the source.");
            source.Value = 64;
            Check((double)target.GetValue(TextBlock.FontSizeProperty)! == 64,
                "A target update interrupted source synchronization.");
        });

        run("Property bindings convert initial and subsequent values", () =>
        {
            var source = new Source { Value = 12 };
            var target = new TextBlock();
            var conversions = 0;
            using var binding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, target, TextBlock.TextProperty,
                value =>
                {
                    conversions++;
                    return value == 12 ? "initial" : "updated";
                });

            Check((string)target.GetValue(TextBlock.TextProperty)! == "initial" && conversions == 1,
                "The initial value was not converted exactly once.");
            source.Value = 20;
            Check((string)target.GetValue(TextBlock.TextProperty)! == "updated" && conversions == 2,
                "A source update was not converted exactly once.");
        });

        run("Property bindings preserve null values and pass them to converters", () =>
        {
            var source = new NullableSource();
            var direct = new TextBlock();
            var converted = new TextBlock();
            using var directBinding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Text, direct, TextBlock.TextProperty);
            using var convertedBinding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Text, converted, TextBlock.TextProperty,
                value => value ?? "empty");
            Check(direct.GetValue(TextBlock.TextProperty) == null &&
                (string)converted.GetValue(TextBlock.TextProperty)! == "empty",
                "The initial null value was dropped or was not passed to the converter.");

            source.Text = "updated";
            Check((string)direct.GetValue(TextBlock.TextProperty)! == "updated" &&
                (string)converted.GetValue(TextBlock.TextProperty)! == "updated",
                "An update after the initial null value was not synchronized.");
            source.Text = null;
            Check(direct.GetValue(TextBlock.TextProperty) == null &&
                (string)converted.GetValue(TextBlock.TextProperty)! == "empty",
                "A subsequent null value was dropped or was not passed to the converter.");
        });

        run("Disposing property bindings releases listeners and restores the original value once", () =>
        {
            var source = new Source { Value = 24 };
            var target = new TextBlock { FontSize = 18 };
            using var bindings = new CompositeDisposable();
            var binding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, target, TextBlock.FontSizeProperty);
            bindings.Add(binding);
            Check(source.Subscribers > 0, "The source was not observed.");
            source.Value = 32;

            bindings.Dispose();
            Check(source.Subscribers == 0, "Disposal left source listeners attached.");
            source.Value = 48;
            Check(target.FontSize == 18,
                "Disposal did not restore the original value or allowed later synchronization.");
            target.FontSize = 20;
            binding.Dispose();
            bindings.Dispose();
            Check(target.FontSize == 20, "Repeated disposal restored the original value again.");
        });

        run("Converted property bindings restore original null values", () =>
        {
            var source = new Source { Value = 24 };
            var target = new TextBlock();
            target.SetValue(TextBlock.TextProperty, null);
            using var binding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, target, TextBlock.TextProperty,
                value => value.ToString());
            source.Value = 32;
            binding.Dispose();
            source.Value = 48;
            Check(target.GetValue(TextBlock.TextProperty) == null && source.Subscribers == 0,
                "A converted binding did not restore the original null value or retained its listener.");
        });

        run("Property bindings on the same source can be disposed independently", () =>
        {
            var source = new Source { Value = 24 };
            var first = new TextBlock();
            var second = new TextBlock();
            using var firstBinding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, first, TextBlock.FontSizeProperty);
            using var secondBinding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, second, TextBlock.FontSizeProperty);

            firstBinding.Dispose();
            firstBinding.Dispose();
            source.Value = 32;
            Check((double)first.GetValue(TextBlock.FontSizeProperty)! == 14 &&
                (double)second.GetValue(TextBlock.FontSizeProperty)! == 32,
                "Disposing one binding affected another binding on the same source.");
            secondBinding.Dispose();
            Check(source.Subscribers == 0, "Independent bindings left source listeners attached.");
        });

        run("Property bindings reject null arguments before subscribing", () =>
        {
            var source = new Source();
            var target = new TextBlock();
            var property = TextBlock.FontSizeProperty;
            Throws<ArgumentNullException>(() => DependencyPropertyBindingHelper.Bind<Source, double>(
                null!, settings => settings.Value, target, property), "source");
            Throws<ArgumentNullException>(() => DependencyPropertyBindingHelper.Bind<Source, double>(
                source, null!, target, property), "sourceProperty");
            Throws<ArgumentNullException>(() => DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, null!, property), "target");
            Throws<ArgumentNullException>(() => DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, target, null!), "targetProperty");
            Throws<ArgumentNullException>(() => DependencyPropertyBindingHelper.Bind<Source, double, double>(
                source, settings => settings.Value, target, property, null!), "converter");
            Check(source.Subscribers == 0, "Invalid arguments left source listeners attached.");
        });

        run("Property bindings propagate conversion errors and release failed subscriptions", () =>
        {
            var source = new Source();
            var target = new TextBlock();
            Throws<InvalidOperationException>(() => DependencyPropertyBindingHelper.Bind<Source, double, double>(
                source, settings => settings.Value, target, TextBlock.FontSizeProperty,
                _ => throw new InvalidOperationException("Initial conversion failed.")));
            Check(source.Subscribers == 0, "An initial conversion failure left source listeners attached.");

            using var binding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, target, TextBlock.FontSizeProperty,
                value => value == 0 ? 24d : throw new InvalidOperationException("Update conversion failed."));
            Throws<InvalidOperationException>(() => source.Value = 1);
            Check((double)target.GetValue(TextBlock.FontSizeProperty)! == 24 && source.Subscribers == 0,
                "A conversion failure changed the last value or left source listeners attached.");
        });

        run("Property bindings propagate dependency property assignment errors", () =>
        {
            var source = new Source { Value = 1 };
            var target = new ThrowingTarget();
            Throws<InvalidOperationException>(() => DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, target, ThrowingTarget.ValueProperty));
            Check(source.Subscribers == 0, "An initial assignment failure left source listeners attached.");

            source.Value = 0;
            using var binding = DependencyPropertyBindingHelper.Bind(
                source, settings => settings.Value, target, ThrowingTarget.ValueProperty);
            Throws<InvalidOperationException>(() => source.Value = 1);
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Throws<TException>(Action action, string? parameterName = null)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            if (parameterName != null)
            {
                Check(exception is ArgumentException argumentException && argumentException.ParamName == parameterName,
                    $"Expected parameter name '{parameterName}'.");
            }

            return;
        }

        throw new Exception($"Expected {typeof(TException).Name}.");
    }

    private sealed class NullableSource : ObservableObject
    {
        private string? _text;

        public string? Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }
    }

    private sealed class ThrowingTarget : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(double), typeof(ThrowingTarget), new PropertyMetadata(0d, (_, args) =>
            {
                if ((double)args.NewValue! == 1)
                {
                    throw new InvalidOperationException("Dependency property assignment failed.");
                }
            }));
    }

    private sealed class Source : INotifyPropertyChanged
    {
        private double _value;
        private PropertyChangedEventHandler? _propertyChanged;

        public int Subscribers => _propertyChanged?.GetInvocationList().Length ?? 0;

        public double Value
        {
            get => _value;
            set
            {
                if (_value.Equals(value))
                {
                    return;
                }

                _value = value;
                _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }
    }
}
