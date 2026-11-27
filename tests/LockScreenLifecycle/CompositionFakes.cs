using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Controls.Scene;
using Windows.UI.Composition;
using Windows.UI.Xaml;

namespace Windows.UI.Xaml.Hosting
{
    public static class ElementCompositionPreview
    {
        private static readonly ConditionalWeakTable<UIElement, ElementVisualState> States = new();

        public static void SetIsTranslationEnabled(UIElement element, bool value)
        {
            ArgumentNullException.ThrowIfNull(element);
            GetOrCreateState(element).IsTranslationEnabled = value;
        }

        public static bool GetIsTranslationEnabled(UIElement element)
        {
            ArgumentNullException.ThrowIfNull(element);
            return GetOrCreateState(element).IsTranslationEnabled;
        }

        public static Visual GetElementVisual(UIElement element)
        {
            ArgumentNullException.ThrowIfNull(element);
            var state = GetOrCreateState(element);
            if (state.Visual == null)
            {
                var compositor = Compositor.GetForWindow(Window.Current);
                state.Visual = new Visual(compositor, element);
            }

            return state.Visual;
        }

        internal static void Reset() => States.Clear();

        private static ElementVisualState GetOrCreateState(UIElement element) =>
            States.GetValue(element, static _ => new ElementVisualState());

        private sealed class ElementVisualState
        {
            public bool IsTranslationEnabled { get; set; }
            public Visual? Visual { get; set; }
        }
    }
}

namespace Windows.UI.Composition
{
    public enum CompositionGetValueStatus
    {
        Succeeded,
        TypeMismatch,
        NotFound
    }

    public sealed class Compositor
    {
        private static readonly ConditionalWeakTable<Window, Compositor> WindowCompositors = new();
        internal static List<CompositionObject> CreatedObjects { get; } = [];
        internal static List<ActiveAnimationRecord> ActiveKeyFrameAnimations { get; } = [];

        public static Compositor GetForWindow(Window window) =>
            WindowCompositors.GetValue(window, static _ => new Compositor());

        internal static void Reset()
        {
            WindowCompositors.Clear();
            CreatedObjects.Clear();
            ActiveKeyFrameAnimations.Clear();
        }

        internal static TimeProvider Clock => IAppHost.TryGetService<TimeProvider>() ?? TimeProvider.System;

        internal static long NowTicks => Clock.GetTimestamp();

        internal static int ActiveKeyFrameAnimationCount
        {
            get
            {
                var now = NowTicks;
                return ActiveKeyFrameAnimations.Count(entry => entry.IsActiveAt(now));
            }
        }

        public CompositionPropertySet CreatePropertySet() => Track(new CompositionPropertySet(this));

        public CubicBezierEasingFunction CreateCubicBezierEasingFunction(Vector2 controlPoint1, Vector2 controlPoint2) =>
            Track(new CubicBezierEasingFunction(this, controlPoint1, controlPoint2));

        public ScalarKeyFrameAnimation CreateScalarKeyFrameAnimation() =>
            Track(new ScalarKeyFrameAnimation(this));

        public Vector2KeyFrameAnimation CreateVector2KeyFrameAnimation() =>
            Track(new Vector2KeyFrameAnimation(this));

        public ExpressionAnimation CreateExpressionAnimation() =>
            Track(new ExpressionAnimation(this, string.Empty));

        public ExpressionAnimation CreateExpressionAnimation(string expression) =>
            Track(new ExpressionAnimation(this, expression));

        private static T Track<T>(T obj) where T : CompositionObject
        {
            CreatedObjects.Add(obj);
            return obj;
        }

        internal sealed class ActiveAnimationRecord
        {
            public required CompositionObject Target { get; init; }
            public required string PropertyName { get; init; }
            public required TimeSpan Duration { get; init; }
            public required long StartTicks { get; init; }
            public required bool HasExplicitStartKeyFrame { get; init; }
            public bool Stopped { get; set; }

            public bool IsActiveAt(long now) =>
                !Stopped && !Target.IsDisposed && Clock.GetElapsedTime(StartTicks, now) < Duration;
        }
    }

    public class CompositionObject : IDisposable
    {
        private readonly Dictionary<string, IAnimationBinding> _animations = new(StringComparer.Ordinal);
        private int _evaluationDepth;

        internal CompositionObject(Compositor compositor)
        {
            Compositor = compositor;
        }

        public Compositor Compositor { get; }

        public bool IsDisposed { get; private set; }

        public virtual void StartAnimation(string propertyName, CompositionAnimation animation)
        {
            ArgumentException.ThrowIfNullOrEmpty(propertyName);
            ArgumentNullException.ThrowIfNull(animation);

            var now = Compositor.NowTicks;
            var startingValue = EvaluateProperty(propertyName, now);
            RemoveAnimation(propertyName);
            var binding = animation.CreateBinding(this, propertyName, startingValue, now);
            _animations[propertyName] = binding;
            if (binding is KeyFrameAnimationBinding kfBinding)
            {
                Compositor.ActiveKeyFrameAnimations.Add(kfBinding.Record);
            }
        }

        public virtual void StopAnimation(string propertyName)
        {
            ArgumentException.ThrowIfNullOrEmpty(propertyName);
            RemoveAnimation(propertyName);
        }

        internal bool HasActiveKeyFrameAnimation(string propertyName) =>
            _animations.TryGetValue(propertyName, out var binding)
            && binding is KeyFrameAnimationBinding kf
            && kf.Record.IsActiveAt(Compositor.NowTicks);

        internal virtual void SetStaticValue(string propertyName, CompositionValue value) =>
            throw new NotSupportedException($"Property {propertyName} is not writable on {GetType().Name}.");

        internal virtual CompositionValue GetStaticValue(string propertyName) =>
            CompositionValue.ZeroScalar;

        internal CompositionValue EvaluateProperty(string propertyName, long now)
        {
            if (_evaluationDepth > 16)
            {
                throw new InvalidOperationException($"Cycle detected evaluating {GetType().Name}.{propertyName}.");
            }

            _evaluationDepth++;
            try
            {
                if (_animations.TryGetValue(propertyName, out var binding))
                {
                    return binding.Evaluate(now);
                }

                return GetStaticValue(propertyName);
            }
            finally
            {
                _evaluationDepth--;
            }
        }

        public void Dispose()
        {
            IsDisposed = true;
            foreach (var binding in _animations.Values)
            {
                binding.Cancel();
            }

            _animations.Clear();
        }

        private void RemoveAnimation(string propertyName)
        {
            if (_animations.Remove(propertyName, out var existing))
            {
                existing.Cancel();
            }
        }
    }

    public sealed class CompositionPropertySet : CompositionObject
    {
        private readonly Dictionary<string, CompositionValue> _values = new(StringComparer.Ordinal);

        internal CompositionPropertySet(Compositor compositor) : base(compositor)
        {
        }

        public void InsertScalar(string propertyName, float value) =>
            _values[propertyName] = CompositionValue.FromScalar(value);

        public void InsertVector2(string propertyName, Vector2 value) =>
            _values[propertyName] = CompositionValue.FromVector2(value);

        public void InsertVector3(string propertyName, Vector3 value) =>
            _values[propertyName] = CompositionValue.FromVector3(value);

        public CompositionGetValueStatus TryGetScalar(string propertyName, out float value)
        {
            if (!_values.TryGetValue(propertyName, out var stored))
            {
                value = 0;
                return CompositionGetValueStatus.NotFound;
            }

            if (stored.Kind != CompositionValueKind.Scalar)
            {
                value = 0;
                return CompositionGetValueStatus.TypeMismatch;
            }

            value = stored.X;
            return CompositionGetValueStatus.Succeeded;
        }

        public CompositionGetValueStatus TryGetVector2(string propertyName, out Vector2 value)
        {
            if (!_values.TryGetValue(propertyName, out var stored))
            {
                value = default;
                return CompositionGetValueStatus.NotFound;
            }

            if (stored.Kind != CompositionValueKind.Vector2)
            {
                value = default;
                return CompositionGetValueStatus.TypeMismatch;
            }

            value = new Vector2(stored.X, stored.Y);
            return CompositionGetValueStatus.Succeeded;
        }

        public CompositionGetValueStatus TryGetVector3(string propertyName, out Vector3 value)
        {
            if (!_values.TryGetValue(propertyName, out var stored))
            {
                value = default;
                return CompositionGetValueStatus.NotFound;
            }

            if (stored.Kind != CompositionValueKind.Vector3)
            {
                value = default;
                return CompositionGetValueStatus.TypeMismatch;
            }

            value = new Vector3(stored.X, stored.Y, stored.Z);
            return CompositionGetValueStatus.Succeeded;
        }

        internal float EvaluateScalar(string propertyName) => EvaluateProperty(propertyName, Compositor.NowTicks).AsScalar();
        internal Vector2 EvaluateVector2(string propertyName) => EvaluateProperty(propertyName, Compositor.NowTicks).AsVector2();
        internal Vector3 EvaluateVector3(string propertyName) => EvaluateProperty(propertyName, Compositor.NowTicks).AsVector3();

        internal override void SetStaticValue(string propertyName, CompositionValue value) => _values[propertyName] = value;

        internal override CompositionValue GetStaticValue(string propertyName) =>
            _values.TryGetValue(propertyName, out var value) ? value : CompositionValue.ZeroScalar;
    }

    public sealed class Visual : CompositionObject
    {
        private readonly UIElement _element;
        private Vector2 _size;
        private Vector3 _scale = Vector3.One;
        private Vector3 _centerPoint = Vector3.Zero;

        internal Visual(Compositor compositor, UIElement element) : base(compositor)
        {
            _element = element;
            Properties = new CompositionPropertySet(compositor);
        }

        public CompositionPropertySet Properties { get; }

        public Vector2 Size
        {
            get => _element is FrameworkElement fe
                ? new Vector2((float)fe.ActualWidth, (float)fe.ActualHeight)
                : _size;
            set => _size = value;
        }

        public Vector3 Scale
        {
            get => _scale;
            set => _scale = value;
        }

        public Vector3 CenterPoint
        {
            get => _centerPoint;
            set => _centerPoint = value;
        }

        public override void StartAnimation(string propertyName, CompositionAnimation animation)
        {
            if (propertyName == "Translation")
            {
                Properties.StartAnimation(propertyName, animation);
                return;
            }

            base.StartAnimation(propertyName, animation);
        }

        public override void StopAnimation(string propertyName)
        {
            if (propertyName == "Translation")
            {
                Properties.StopAnimation(propertyName);
                return;
            }

            base.StopAnimation(propertyName);
        }

        internal Vector3 EvaluateTranslation() => Properties.EvaluateVector3("Translation");
        internal Vector3 EvaluateScale() => EvaluateProperty(nameof(Scale), Compositor.NowTicks).AsVector3();
        internal Vector3 EvaluateCenterPoint() => EvaluateProperty(nameof(CenterPoint), Compositor.NowTicks).AsVector3();

        internal override void SetStaticValue(string propertyName, CompositionValue value)
        {
            switch (propertyName)
            {
                case nameof(Scale):
                    _scale = value.AsVector3();
                    break;
                case nameof(CenterPoint):
                    _centerPoint = value.AsVector3();
                    break;
                case nameof(Size):
                    _size = value.AsVector2();
                    break;
                default:
                    Properties.SetStaticValue(propertyName, value);
                    break;
            }
        }

        internal override CompositionValue GetStaticValue(string propertyName) => propertyName switch
        {
            nameof(Scale) => CompositionValue.FromVector3(_scale),
            nameof(CenterPoint) => CompositionValue.FromVector3(_centerPoint),
            nameof(Size) => CompositionValue.FromVector2(Size),
            _ => Properties.GetStaticValue(propertyName)
        };
    }

    public class CompositionEasingFunction : CompositionObject
    {
        internal CompositionEasingFunction(Compositor compositor) : base(compositor)
        {
        }

        internal virtual double Evaluate(double progress) => Math.Clamp(progress, 0, 1);
    }

    public sealed class CubicBezierEasingFunction : CompositionEasingFunction
    {
        internal CubicBezierEasingFunction(Compositor compositor, Vector2 controlPoint1, Vector2 controlPoint2)
            : base(compositor)
        {
            ControlPoint1 = controlPoint1;
            ControlPoint2 = controlPoint2;
        }

        public Vector2 ControlPoint1 { get; }
        public Vector2 ControlPoint2 { get; }

        internal override double Evaluate(double progress) =>
            ParallaxMotionMath.EvaluateCubicBezier(progress, ControlPoint1, ControlPoint2);
    }

    public abstract class CompositionAnimation : CompositionObject
    {
        protected readonly Dictionary<string, CompositionObject> ReferenceParameters = new(StringComparer.Ordinal);
        protected readonly Dictionary<string, float> ScalarParameters = new(StringComparer.Ordinal);

        internal CompositionAnimation(Compositor compositor) : base(compositor)
        {
        }

        public void SetReferenceParameter(string key, CompositionObject compositionObject)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentNullException.ThrowIfNull(compositionObject);
            ReferenceParameters[key] = compositionObject;
        }

        public void SetScalarParameter(string key, float value)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ScalarParameters[key] = value;
        }

        internal abstract IAnimationBinding CreateBinding(
            CompositionObject target,
            string propertyName,
            CompositionValue startingValue,
            long startTicks);
    }

    public abstract class KeyFrameAnimation : CompositionAnimation
    {
        protected string? InitialExpression;

        internal KeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        public TimeSpan Duration { get; set; }

        public void InsertExpressionKeyFrame(float normalizedProgressKey, string value)
        {
            if (normalizedProgressKey != 0.0f)
            {
                throw new NotSupportedException("Only normalizedProgressKey == 0.0f expression key frames are modeled.");
            }

            InitialExpression = value;
        }
    }

    public sealed class ScalarKeyFrameAnimation : KeyFrameAnimation
    {
        private readonly List<(float Key, float Value, CompositionEasingFunction? Easing)> _keyFrames = [];

        internal ScalarKeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        public void InsertKeyFrame(float normalizedProgressKey, float value, CompositionEasingFunction? easingFunction = null) =>
            _keyFrames.Add((normalizedProgressKey, value, easingFunction));

        internal override IAnimationBinding CreateBinding(
            CompositionObject target,
            string propertyName,
            CompositionValue startingValue,
            long startTicks)
        {
            var initial = startingValue.AsScalar();
            var hasZero = false;
            float endValue = initial;
            CompositionEasingFunction? easing = null;
            foreach (var (key, value, kfEasing) in _keyFrames)
            {
                if (key == 0.0f)
                {
                    initial = value;
                    hasZero = true;
                }
                else if (key == 1.0f)
                {
                    endValue = value;
                    easing = kfEasing;
                }
            }

            if (!hasZero && InitialExpression != null)
            {
                initial = CompositionExpressionEvaluator.Evaluate(
                    InitialExpression,
                    target,
                    CompositionValue.FromScalar(initial),
                    ReferenceParameters,
                    ScalarParameters,
                    startTicks).AsScalar();
            }

            return new KeyFrameAnimationBinding(
                target,
                propertyName,
                Duration,
                startTicks,
                CompositionValue.FromScalar(initial),
                CompositionValue.FromScalar(endValue),
                easing,
                hasZero);
        }
    }

    public sealed class Vector2KeyFrameAnimation : KeyFrameAnimation
    {
        private readonly List<(float Key, Vector2 Value, CompositionEasingFunction? Easing)> _keyFrames = [];

        internal Vector2KeyFrameAnimation(Compositor compositor) : base(compositor)
        {
        }

        public void InsertKeyFrame(float normalizedProgressKey, Vector2 value, CompositionEasingFunction? easingFunction = null) =>
            _keyFrames.Add((normalizedProgressKey, value, easingFunction));

        internal override IAnimationBinding CreateBinding(
            CompositionObject target,
            string propertyName,
            CompositionValue startingValue,
            long startTicks)
        {
            var initial = startingValue.AsVector2();
            var hasZero = false;
            Vector2 endValue = initial;
            CompositionEasingFunction? easing = null;
            foreach (var (key, value, kfEasing) in _keyFrames)
            {
                if (key == 0.0f)
                {
                    initial = value;
                    hasZero = true;
                }
                else if (key == 1.0f)
                {
                    endValue = value;
                    easing = kfEasing;
                }
            }

            if (!hasZero && InitialExpression != null)
            {
                initial = CompositionExpressionEvaluator.Evaluate(
                    InitialExpression,
                    target,
                    CompositionValue.FromVector2(initial),
                    ReferenceParameters,
                    ScalarParameters,
                    startTicks).AsVector2();
            }

            return new KeyFrameAnimationBinding(
                target,
                propertyName,
                Duration,
                startTicks,
                CompositionValue.FromVector2(initial),
                CompositionValue.FromVector2(endValue),
                easing,
                hasZero);
        }
    }

    public sealed class ExpressionAnimation : CompositionAnimation
    {
        internal ExpressionAnimation(Compositor compositor, string expression) : base(compositor)
        {
            Expression = expression;
            // Parse eagerly to catch syntax typos at creation time, matching WinRT validation.
            CompositionExpressionEvaluator.ValidateSyntax(expression);
        }

        public string Expression { get; }

        internal override IAnimationBinding CreateBinding(
            CompositionObject target,
            string propertyName,
            CompositionValue startingValue,
            long startTicks)
        {
            var refs = new Dictionary<string, CompositionObject>(ReferenceParameters, StringComparer.Ordinal);
            var scalars = new Dictionary<string, float>(ScalarParameters, StringComparer.Ordinal);
            if (string.Equals(Expression.Trim(), "this.StartingValue", StringComparison.Ordinal))
            {
                return new FrozenValueBinding(startingValue);
            }

            return new ExpressionAnimationBinding(Expression, target, startingValue, refs, scalars);
        }
    }

    internal interface IAnimationBinding
    {
        CompositionValue Evaluate(long now);
        void Cancel();
    }

    internal sealed class FrozenValueBinding(CompositionValue frozenValue) : IAnimationBinding
    {
        public CompositionValue Evaluate(long now) => frozenValue;
        public void Cancel() { }
    }

    internal sealed class KeyFrameAnimationBinding : IAnimationBinding
    {
        private readonly CompositionValue _start;
        private readonly CompositionValue _end;
        private readonly CompositionEasingFunction? _easing;

        public KeyFrameAnimationBinding(
            CompositionObject target,
            string propertyName,
            TimeSpan duration,
            long startTicks,
            CompositionValue start,
            CompositionValue end,
            CompositionEasingFunction? easing,
            bool hasExplicitStartKeyFrame)
        {
            _start = start;
            _end = end;
            _easing = easing;
            Record = new Compositor.ActiveAnimationRecord
            {
                Target = target,
                PropertyName = propertyName,
                Duration = duration,
                StartTicks = startTicks,
                HasExplicitStartKeyFrame = hasExplicitStartKeyFrame
            };
        }

        public Compositor.ActiveAnimationRecord Record { get; }

        public CompositionValue Evaluate(long now)
        {
            if (Record.Duration <= TimeSpan.Zero)
            {
                return _end;
            }

            var elapsed = Compositor.Clock.GetElapsedTime(Record.StartTicks, now);
            if (elapsed <= TimeSpan.Zero)
            {
                return _start;
            }

            if (elapsed >= Record.Duration)
            {
                return _end;
            }

            var progress = elapsed.TotalMilliseconds / Record.Duration.TotalMilliseconds;
            var factor = (float)(_easing?.Evaluate(progress) ?? Math.Clamp(progress, 0, 1));
            return CompositionValue.Lerp(_start, _end, factor);
        }

        public void Cancel() => Record.Stopped = true;
    }

    internal sealed class ExpressionAnimationBinding(
        string expression,
        CompositionObject target,
        CompositionValue startingValue,
        Dictionary<string, CompositionObject> refs,
        Dictionary<string, float> scalars) : IAnimationBinding
    {
        public CompositionValue Evaluate(long now) =>
            CompositionExpressionEvaluator.Evaluate(expression, target, startingValue, refs, scalars, now);

        public void Cancel() { }
    }

    internal enum CompositionValueKind
    {
        Scalar,
        Vector2,
        Vector3
    }

    internal readonly record struct CompositionValue(CompositionValueKind Kind, float X, float Y, float Z)
    {
        public static CompositionValue ZeroScalar => new(CompositionValueKind.Scalar, 0, 0, 0);
        public static CompositionValue FromScalar(float x) => new(CompositionValueKind.Scalar, x, 0, 0);
        public static CompositionValue FromVector2(Vector2 v) => new(CompositionValueKind.Vector2, v.X, v.Y, 0);
        public static CompositionValue FromVector3(Vector3 v) => new(CompositionValueKind.Vector3, v.X, v.Y, v.Z);

        public float AsScalar() => X;
        public Vector2 AsVector2() => new(X, Y);
        public Vector3 AsVector3() => new(X, Y, Z);

        public static CompositionValue Lerp(CompositionValue a, CompositionValue b, float t) => a.Kind switch
        {
            CompositionValueKind.Scalar => FromScalar(a.X + (b.X - a.X) * t),
            CompositionValueKind.Vector2 => FromVector2(Vector2.Lerp(a.AsVector2(), b.AsVector2(), t)),
            CompositionValueKind.Vector3 => FromVector3(Vector3.Lerp(a.AsVector3(), b.AsVector3(), t)),
            _ => b
        };
    }

    internal static class CompositionExpressionEvaluator
    {
        internal static void ValidateSyntax(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return;
            }

            _ = new Parser(expression).ParseOnly();
        }

        internal static CompositionValue Evaluate(
            string expression,
            CompositionObject target,
            CompositionValue startingValue,
            IReadOnlyDictionary<string, CompositionObject> refs,
            IReadOnlyDictionary<string, float> scalars,
            long now)
        {
            var parser = new Parser(expression, target, startingValue, refs, scalars, now);
            return parser.Evaluate();
        }

        private ref struct Parser
        {
            private readonly string _text;
            private readonly CompositionObject? _target;
            private readonly CompositionValue _startingValue;
            private readonly IReadOnlyDictionary<string, CompositionObject>? _refs;
            private readonly IReadOnlyDictionary<string, float>? _scalars;
            private readonly long _now;
            private readonly bool _eval;
            private int _pos;

            public Parser(string text)
            {
                _text = text;
                _target = null;
                _startingValue = default;
                _refs = null;
                _scalars = null;
                _now = 0;
                _eval = false;
                _pos = 0;
            }

            public Parser(
                string text,
                CompositionObject target,
                CompositionValue startingValue,
                IReadOnlyDictionary<string, CompositionObject> refs,
                IReadOnlyDictionary<string, float> scalars,
                long now)
            {
                _text = text;
                _target = target;
                _startingValue = startingValue;
                _refs = refs;
                _scalars = scalars;
                _now = now;
                _eval = true;
                _pos = 0;
            }

            public bool ParseOnly()
            {
                _ = ParseTernary();
                SkipWhitespace();
                if (_pos < _text.Length)
                {
                    throw new FormatException($"Unexpected token in Composition expression at {_pos}: {_text[_pos..]}");
                }

                return true;
            }

            public CompositionValue Evaluate()
            {
                var result = ParseTernary();
                SkipWhitespace();
                if (_pos < _text.Length)
                {
                    throw new FormatException($"Unexpected token in Composition expression at {_pos}: {_text[_pos..]}");
                }

                return result;
            }

            private CompositionValue ParseTernary()
            {
                var cond = ParseOr();
                if (Match('?'))
                {
                    var whenTrue = ParseTernary();
                    Expect(':');
                    var whenFalse = ParseTernary();
                    return !_eval ? whenTrue : (cond.X != 0 ? whenTrue : whenFalse);
                }

                return cond;
            }

            private CompositionValue ParseOr()
            {
                var left = ParseAnd();
                while (Match("||"))
                {
                    var right = ParseAnd();
                    left = CompositionValue.FromScalar((left.X != 0 || right.X != 0) ? 1f : 0f);
                }

                return left;
            }

            private CompositionValue ParseAnd()
            {
                var left = ParseComparison();
                while (Match("&&"))
                {
                    var right = ParseComparison();
                    left = CompositionValue.FromScalar((left.X != 0 && right.X != 0) ? 1f : 0f);
                }

                return left;
            }

            private CompositionValue ParseComparison()
            {
                var left = ParseAdditive();
                while (true)
                {
                    if (Match(">="))
                    {
                        var right = ParseAdditive();
                        left = CompositionValue.FromScalar(left.X >= right.X ? 1f : 0f);
                    }
                    else if (Match("<="))
                    {
                        var right = ParseAdditive();
                        left = CompositionValue.FromScalar(left.X <= right.X ? 1f : 0f);
                    }
                    else if (Match("=="))
                    {
                        var right = ParseAdditive();
                        left = CompositionValue.FromScalar(left.X == right.X ? 1f : 0f);
                    }
                    else if (Match("!="))
                    {
                        var right = ParseAdditive();
                        left = CompositionValue.FromScalar(left.X != right.X ? 1f : 0f);
                    }
                    else if (Match('>'))
                    {
                        var right = ParseAdditive();
                        left = CompositionValue.FromScalar(left.X > right.X ? 1f : 0f);
                    }
                    else if (Match('<'))
                    {
                        var right = ParseAdditive();
                        left = CompositionValue.FromScalar(left.X < right.X ? 1f : 0f);
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            private CompositionValue ParseAdditive()
            {
                var left = ParseMultiplicative();
                while (true)
                {
                    if (Match('+'))
                    {
                        var right = ParseMultiplicative();
                        left = Add(left, right);
                    }
                    else if (Match('-'))
                    {
                        var right = ParseMultiplicative();
                        left = Subtract(left, right);
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            private CompositionValue ParseMultiplicative()
            {
                var left = ParseUnary();
                while (true)
                {
                    if (Match('*'))
                    {
                        var right = ParseUnary();
                        left = Multiply(left, right);
                    }
                    else if (Match('/'))
                    {
                        var right = ParseUnary();
                        left = Divide(left, right);
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            private CompositionValue ParseUnary()
            {
                if (Match('-'))
                {
                    var inner = ParseUnary();
                    return new CompositionValue(inner.Kind, -inner.X, -inner.Y, -inner.Z);
                }

                if (Match('+'))
                {
                    return ParseUnary();
                }

                if (Match('!'))
                {
                    var inner = ParseUnary();
                    return CompositionValue.FromScalar(inner.X == 0 ? 1f : 0f);
                }

                return ParsePrimary();
            }

            private CompositionValue ParsePrimary()
            {
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == '(')
                {
                    _pos++;
                    var inner = ParseTernary();
                    Expect(')');
                    return inner;
                }

                if (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                {
                    var start = _pos;
                    while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                    {
                        _pos++;
                    }

                    if (_pos < _text.Length && (_text[_pos] == 'f' || _text[_pos] == 'F'))
                    {
                        throw new FormatException("WinRT Composition expressions do not support 'f' float suffixes.");
                    }

                    var value = float.Parse(_text.AsSpan(start, _pos - start), CultureInfo.InvariantCulture);
                    return CompositionValue.FromScalar(value);
                }

                var ident = ReadIdentifier();
                if (Match('('))
                {
                    return ParseFunction(ident);
                }

                var parts = new List<string> { ident };
                while (Match('.'))
                {
                    parts.Add(ReadIdentifier());
                }

                return !_eval ? CompositionValue.ZeroScalar : ResolvePath(parts);
            }

            private CompositionValue ParseFunction(string name)
            {
                var args = new List<CompositionValue>();
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] != ')')
                {
                    do
                    {
                        args.Add(ParseTernary());
                    } while (Match(','));
                }

                Expect(')');
                if (!_eval)
                {
                    return CompositionValue.ZeroScalar;
                }

                return name switch
                {
                    "Vector2" => CompositionValue.FromVector2(new Vector2(args[0].X, args[1].X)),
                    "Vector3" => CompositionValue.FromVector3(new Vector3(args[0].X, args[1].X, args[2].X)),
                    "Clamp" => CompositionValue.FromScalar(Math.Clamp(args[0].X, args[1].X, args[2].X)),
                    "Max" => CompositionValue.FromScalar(Math.Max(args[0].X, args[1].X)),
                    "Min" => CompositionValue.FromScalar(Math.Min(args[0].X, args[1].X)),
                    "Abs" => CompositionValue.FromScalar(Math.Abs(args[0].X)),
                    _ => throw new NotSupportedException($"Unsupported Composition expression function: {name}")
                };
            }

            private CompositionValue ResolvePath(List<string> parts)
            {
                if (parts.Count == 1 && _scalars != null && _scalars.TryGetValue(parts[0], out var scalar))
                {
                    return CompositionValue.FromScalar(scalar);
                }

                if (parts[0] == "this")
                {
                    if (parts.Count >= 2 && parts[1] == "StartingValue")
                    {
                        return parts.Count == 2 ? _startingValue : SelectComponent(_startingValue, parts[2]);
                    }

                    if (parts.Count >= 3 && parts[1] == "Target" && _target != null)
                    {
                        var val = _target.EvaluateProperty(parts[2], _now);
                        return parts.Count == 3 ? val : SelectComponent(val, parts[3]);
                    }
                }

                if (parts.Count >= 2 && _refs != null && _refs.TryGetValue(parts[0], out var refObj))
                {
                    var val = refObj.EvaluateProperty(parts[1], _now);
                    return parts.Count == 2 ? val : SelectComponent(val, parts[2]);
                }

                throw new KeyNotFoundException($"Unresolved Composition expression reference: {string.Join('.', parts)}");
            }

            private static CompositionValue SelectComponent(CompositionValue value, string component) => component switch
            {
                "X" => CompositionValue.FromScalar(value.X),
                "Y" => CompositionValue.FromScalar(value.Y),
                "Z" => CompositionValue.FromScalar(value.Z),
                _ => throw new NotSupportedException($"Unknown vector component {component}.")
            };

            private static CompositionValue Add(CompositionValue a, CompositionValue b) => a.Kind switch
            {
                CompositionValueKind.Vector2 => CompositionValue.FromVector2(a.AsVector2() + b.AsVector2()),
                CompositionValueKind.Vector3 => CompositionValue.FromVector3(a.AsVector3() + b.AsVector3()),
                _ => CompositionValue.FromScalar(a.X + b.X)
            };

            private static CompositionValue Subtract(CompositionValue a, CompositionValue b) => a.Kind switch
            {
                CompositionValueKind.Vector2 => CompositionValue.FromVector2(a.AsVector2() - b.AsVector2()),
                CompositionValueKind.Vector3 => CompositionValue.FromVector3(a.AsVector3() - b.AsVector3()),
                _ => CompositionValue.FromScalar(a.X - b.X)
            };

            private static CompositionValue Multiply(CompositionValue a, CompositionValue b) => a.Kind switch
            {
                CompositionValueKind.Vector2 when b.Kind == CompositionValueKind.Scalar =>
                    CompositionValue.FromVector2(a.AsVector2() * b.X),
                CompositionValueKind.Scalar when b.Kind == CompositionValueKind.Vector2 =>
                    CompositionValue.FromVector2(a.X * b.AsVector2()),
                CompositionValueKind.Vector3 when b.Kind == CompositionValueKind.Scalar =>
                    CompositionValue.FromVector3(a.AsVector3() * b.X),
                CompositionValueKind.Scalar when b.Kind == CompositionValueKind.Vector3 =>
                    CompositionValue.FromVector3(a.X * b.AsVector3()),
                _ => CompositionValue.FromScalar(a.X * b.X)
            };

            private static CompositionValue Divide(CompositionValue a, CompositionValue b) => a.Kind switch
            {
                CompositionValueKind.Vector2 when b.Kind == CompositionValueKind.Scalar =>
                    CompositionValue.FromVector2(a.AsVector2() / b.X),
                CompositionValueKind.Vector3 when b.Kind == CompositionValueKind.Scalar =>
                    CompositionValue.FromVector3(a.AsVector3() / b.X),
                _ => CompositionValue.FromScalar(a.X / b.X)
            };

            private string ReadIdentifier()
            {
                SkipWhitespace();
                var start = _pos;
                while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
                {
                    _pos++;
                }

                if (_pos == start)
                {
                    throw new FormatException($"Expected identifier in Composition expression at {_pos}: {_text[_pos..]}");
                }

                return _text[start.._pos];
            }

            private void SkipWhitespace()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                {
                    _pos++;
                }
            }

            private bool Match(char c)
            {
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == c)
                {
                    _pos++;
                    return true;
                }

                return false;
            }

            private bool Match(string op)
            {
                SkipWhitespace();
                if (_text.AsSpan(_pos).StartsWith(op, StringComparison.Ordinal))
                {
                    _pos += op.Length;
                    return true;
                }

                return false;
            }

            private void Expect(char c)
            {
                if (!Match(c))
                {
                    throw new FormatException($"Expected '{c}' in Composition expression at {_pos}: {_text[_pos..]}");
                }
            }
        }
    }
}
