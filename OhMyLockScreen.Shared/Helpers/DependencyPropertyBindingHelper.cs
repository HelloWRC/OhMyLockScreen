using System.ComponentModel;
using System.Linq.Expressions;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData.Binding;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Helpers;

/// <summary>将可观测属性单向同步到依赖属性，并提供可释放的订阅。</summary>
/// <remarks>
/// 调用方应在目标所属的 UI 线程上建立、释放绑定和更新源属性；此类不负责线程调度。
/// 绑定会立即同步当前值。释放返回的订阅会停止同步，将目标恢复到绑定前的值，且可重复释放。
/// 调用方负责管理订阅的生命周期，例如将其加入 CompositeDisposable。
/// </remarks>
public static class DependencyPropertyBindingHelper
{
    /// <summary>将源属性的当前值及后续变化直接写入目标依赖属性。</summary>
    /// <param name="source">实现属性变化通知的源对象。</param>
    /// <param name="sourceProperty">指定要监听的源属性的表达式。</param>
    /// <param name="target">目标依赖对象。</param>
    /// <param name="targetProperty">与源属性值类型兼容的目标依赖属性。</param>
    /// <returns>用于停止同步的订阅；释放时恢复绑定前的值。</returns>
    /// <exception cref="ArgumentNullException">任意参数为 null。</exception>
    public static IDisposable Bind<TSource, TValue>(
        TSource source,
        Expression<Func<TSource, TValue>> sourceProperty,
        DependencyObject target,
        DependencyProperty targetProperty)
        where TSource : class, INotifyPropertyChanged
    {
        return Bind(source, sourceProperty, target, targetProperty, static value => value);
    }

    /// <summary>将源属性的当前值及后续变化转换后写入目标依赖属性。</summary>
    /// <param name="source">实现属性变化通知的源对象。</param>
    /// <param name="sourceProperty">指定要监听的源属性的表达式。</param>
    /// <param name="target">目标依赖对象。</param>
    /// <param name="targetProperty">与转换结果类型兼容的目标依赖属性。</param>
    /// <param name="converter">对初始值及后续值执行的转换。</param>
    /// <returns>用于停止同步的订阅；释放时恢复绑定前的值。</returns>
    /// <exception cref="ArgumentNullException">任意参数为 null。</exception>
    /// <remarks>转换和依赖属性赋值产生的异常会向调用方传播。</remarks>
    public static IDisposable Bind<TSource, TValue, TResult>(
        TSource source,
        Expression<Func<TSource, TValue>> sourceProperty,
        DependencyObject target,
        DependencyProperty targetProperty,
        Func<TValue, TResult> converter)
        where TSource : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceProperty);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetProperty);
        ArgumentNullException.ThrowIfNull(converter);

        return Bind(source.WhenValueChanged(sourceProperty, notifyOnInitialValue: true)
            .Select(value => converter(value!)), target, targetProperty);
    }

    /// <summary>将可观测序列中的值写入目标依赖属性。</summary>
    /// <param name="source">提供目标值的可观测序列。</param>
    /// <param name="target">目标依赖对象。</param>
    /// <param name="targetProperty">与序列值类型兼容的目标依赖属性。</param>
    /// <returns>用于停止同步的订阅；释放时恢复绑定前的值。</returns>
    /// <exception cref="ArgumentNullException">任意参数为 null。</exception>
    public static IDisposable Bind<TValue>(
        IObservable<TValue> source,
        DependencyObject target,
        DependencyProperty targetProperty)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetProperty);

        var originalValue = target.GetValue(targetProperty);
        IDisposable subscription;
        try
        {
            subscription = source.Subscribe(value => target.SetValue(targetProperty, value));
        }
        catch
        {
            target.SetValue(targetProperty, originalValue);
            throw;
        }

        return Disposable.Create(() =>
        {
            subscription.Dispose();
            target.SetValue(targetProperty, originalValue);
        });
    }
}
