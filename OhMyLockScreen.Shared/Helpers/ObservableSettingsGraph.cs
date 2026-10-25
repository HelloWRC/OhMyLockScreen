using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Reactive.Disposables;

namespace OhMyLockScreen.Shared.Helpers;
/// <summary>跟踪设置树的属性、集合及可替换子模型；释放时解除整棵树的订阅。</summary>
public sealed class ObservableSettingsGraph : IDisposable
{
    private readonly object _root;
    private readonly Action<object, string?, bool> _changed;
    private readonly CompositeDisposable _subscriptions = new();
    private bool _disposed;
    public ObservableSettingsGraph(object root, Action<object, string?, bool> changed)
    {
        _root = root;
        _changed = changed;
        Subscribe();
    }

    private void Subscribe()
    {
        _subscriptions.Clear();
        Visit(_root, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private void Visit(object? value, HashSet<object> visited)
    {
        if (value == null || value is string || !visited.Add(value))
        {
            return;
        }

        if (value is INotifyCollectionChanged collection)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => OnChanged(value, null, true);
            collection.CollectionChanged += handler;
            _subscriptions.Add(Disposable.Create(() => collection.CollectionChanged -= handler));
        }

        if (value is IEnumerable items)
        {
            foreach (var item in items)
            {
                Visit(item, visited);
            }

            return;
        }

        if (value is not INotifyPropertyChanged model)
        {
            return;
        }

        PropertyChangedEventHandler propertyHandler = (_, args) => OnChanged(value, args.PropertyName, false);
        model.PropertyChanged += propertyHandler;
        _subscriptions.Add(Disposable.Create(() => model.PropertyChanged -= propertyHandler));
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0 && CanContainSettings(property.PropertyType))
            {
                Visit(property.GetValue(value), visited);
            }
        }
    }

    private void OnChanged(object sender, string? property, bool isCollection)
    {
        if (_disposed)
        {
            return;
        }

        if (isCollection || string.IsNullOrEmpty(property) || sender.GetType().GetProperty(property)?.PropertyType is { } type && CanContainSettings(type))
        {
            Subscribe();
        }

        _changed(sender, property, isCollection);
    }

    private static bool CanContainSettings(Type type) => typeof(INotifyPropertyChanged).IsAssignableFrom(type) || typeof(INotifyCollectionChanged).IsAssignableFrom(type) || type.IsInterface;
    public void Dispose()
    {
        _disposed = true;
        _subscriptions.Dispose();
    }
}