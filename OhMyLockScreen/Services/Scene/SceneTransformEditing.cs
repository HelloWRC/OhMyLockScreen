using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.ViewModels;
using Windows.Foundation;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Services.Scene;

internal enum ResizeCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>计算组件变换选中轮廓、画布把柄坐标，以及原点/旋转/缩放拖拽参数。</summary>
internal static class SceneTransformEditing
{
    internal const string TransformFeatureId = "omls.features.layout.transform";
    internal const double MinTranslate = -100000;
    internal const double MaxTranslate = 100000;
    internal const double MinScale = -100;
    internal const double MaxScale = 100;
    internal const double MinSkew = -89;
    internal const double MaxSkew = 89;
    internal const double MinOrigin = -10;
    internal const double MaxOrigin = 10;
    internal const double OriginSnapRadiusPx = 8.0;
    internal const double RotateMinPivotDistancePx = 2.0;
    internal const double RotationSnapStepDeg = 15.0;

    private static readonly double[] OriginSnapAnchors = [0, 0.5, 1];

    internal static bool CanEditTransform(IFeatureContainer? container) =>
        container?.Features.FirstOrDefault(item => item.Id == TransformFeatureId) is { IsEnabled: true };

    internal static TransformFeatureSettings? GetTransform(IFeatureContainer? container)
    {
        var feature = container?.Features.FirstOrDefault(item => item.Id == TransformFeatureId);
        return feature is { IsEnabled: true }
            ? feature.GetSettingsOrCreate<TransformFeatureSettings>()
            : null;
    }

    internal static BrushEditorMatrix FromGeneralTransform(GeneralTransform? transform)
    {
        if (transform == null)
        {
            return BrushEditorMatrix.Identity;
        }

        try
        {
            var origin = transform.TransformPoint(new Point(0, 0));
            var pointX = transform.TransformPoint(new Point(100, 0));
            var pointY = transform.TransformPoint(new Point(0, 100));
            var matrix = new BrushEditorMatrix(
                (pointX.X - origin.X) / 100,
                (pointX.Y - origin.Y) / 100,
                (pointY.X - origin.X) / 100,
                (pointY.Y - origin.Y) / 100,
                origin.X,
                origin.Y);
            return IsFiniteMatrix(matrix) ? matrix : BrushEditorMatrix.Identity;
        }
        catch
        {
            return BrushEditorMatrix.Identity;
        }
    }

    internal static (double X, double Y) MapVector(BrushEditorMatrix matrix, double x, double y)
        => (x * matrix.M11 + y * matrix.M21, x * matrix.M12 + y * matrix.M22);

    internal static BrushEditorMatrix LinearOnly(BrushEditorMatrix matrix)
        => new(matrix.M11, matrix.M12, matrix.M21, matrix.M22, 0, 0);

    internal static BrushEditorMatrix BuildPostScaleLinear(TransformFeatureSettings settings)
    {
        var skewX = Math.Clamp(Sanitize(settings.SkewX, 0), MinSkew, MaxSkew) * Math.PI / 180;
        var skewY = Math.Clamp(Sanitize(settings.SkewY, 0), MinSkew, MaxSkew) * Math.PI / 180;
        var rotation = Sanitize(settings.Rotation, 0) * Math.PI / 180;
        var skew = new BrushEditorMatrix(1, Math.Tan(skewY), Math.Tan(skewX), 1, 0, 0);
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        var rotate = new BrushEditorMatrix(cos, sin, -sin, cos, 0, 0);
        return skew.Then(rotate);
    }

    internal static BrushEditorMatrix BuildLinear(TransformFeatureSettings settings)
    {
        var scaleX = ApplyFlip(Sanitize(settings.ScaleX, 1), settings.FlipHorizontal);
        var scaleY = ApplyFlip(Sanitize(settings.ScaleY, 1), settings.FlipVertical);
        return new BrushEditorMatrix(scaleX, 0, 0, scaleY, 0, 0).Then(BuildPostScaleLinear(settings));
    }

    internal static BrushEditorMatrix BuildSelfMatrix(TransformFeatureSettings settings, double width, double height)
    {
        var pivotX = Sanitize(settings.OriginX, 0.5) * width;
        var pivotY = Sanitize(settings.OriginY, 0.5) * height;
        var translateX = Sanitize(settings.TranslateX, 0);
        var translateY = Sanitize(settings.TranslateY, 0);
        return new BrushEditorMatrix(1, 0, 0, 1, -pivotX, -pivotY)
            .Then(BuildLinear(settings))
            .Then(new BrushEditorMatrix(1, 0, 0, 1, pivotX + translateX, pivotY + translateY));
    }

    internal static bool TryGetLinearInverse(TransformFeatureSettings? settings, out BrushEditorMatrix inverse)
    {
        if (settings == null)
        {
            inverse = BrushEditorMatrix.Identity;
            return true;
        }

        return BuildLinear(settings).TryInverse(out inverse);
    }

    internal static (double DeltaX, double DeltaY) MapResizeDelta(
        BrushEditorMatrix? linearInverse,
        double panelDeltaX,
        double panelDeltaY)
    {
        if (linearInverse is not { } inverse)
        {
            return (panelDeltaX, panelDeltaY);
        }

        var mapped = MapVector(inverse, panelDeltaX, panelDeltaY);
        return double.IsFinite(mapped.X) && double.IsFinite(mapped.Y)
            ? mapped
            : (panelDeltaX, panelDeltaY);
    }

    internal static SceneSelectionGeometry ComputeSelectionGeometry(
        double width,
        double height,
        BrushEditorMatrix targetToViewport,
        BrushEditorMatrix? panelToViewport = null,
        TransformFeatureSettings? transform = null)
    {
        var topLeft = targetToViewport.Map(new Point(0, 0));
        var topRight = targetToViewport.Map(new Point(width, 0));
        var bottomRight = targetToViewport.Map(new Point(width, height));
        var bottomLeft = targetToViewport.Map(new Point(0, height));

        var minX = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomRight.X, bottomLeft.X));
        var minY = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomRight.Y, bottomLeft.Y));
        var maxX = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomRight.X, bottomLeft.X));
        var maxY = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomRight.Y, bottomLeft.Y));

        var originX = transform != null ? Sanitize(transform.OriginX, 0.5) : 0.5;
        var originY = transform != null ? Sanitize(transform.OriginY, 0.5) : 0.5;
        var origin = targetToViewport.Map(new Point(originX * width, originY * height));

        var topCenter = new Point((topLeft.X + topRight.X) / 2, (topLeft.Y + topRight.Y) / 2);
        var center = new Point(
            (topLeft.X + topRight.X + bottomRight.X + bottomLeft.X) / 4,
            (topLeft.Y + topRight.Y + bottomRight.Y + bottomLeft.Y) / 4);

        var (nx, ny) = ResolveOutwardTopNormal(topLeft, topRight, topCenter, center, panelToViewport, transform);
        var rotationHandle = new Point(
            topCenter.X + nx * SceneSelectionGeometry.RotationHandleOffset,
            topCenter.Y + ny * SceneSelectionGeometry.RotationHandleOffset);

        return new SceneSelectionGeometry(
            minX,
            minY,
            maxX - minX,
            maxY - minY,
            topLeft,
            topRight,
            bottomRight,
            bottomLeft,
            origin,
            topCenter,
            rotationHandle);
    }

    internal static bool TryBeginOriginDrag(
        TransformFeatureSettings settings,
        double width,
        double height,
        BrushEditorMatrix targetToViewport,
        BrushEditorMatrix panelToViewport,
        Point dragStartViewport,
        out OriginDragState state)
    {
        state = default;
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height)
            || !panelToViewport.TryInverse(out var viewportToPanel))
        {
            return false;
        }

        var startOriginX = Sanitize(settings.OriginX, 0.5);
        var startOriginY = Sanitize(settings.OriginY, 0.5);
        var startTranslateX = Sanitize(settings.TranslateX, 0);
        var startTranslateY = Sanitize(settings.TranslateY, 0);
        var hasViewportToLocal = targetToViewport.TryInverse(out var viewportToLocal);
        var startPivotViewport = targetToViewport.Map(new Point(startOriginX * width, startOriginY * height));
        var grabOffset = new Point(startPivotViewport.X - dragStartViewport.X, startPivotViewport.Y - dragStartViewport.Y);

        state = new OriginDragState(
            width,
            height,
            startOriginX,
            startOriginY,
            startTranslateX,
            startTranslateY,
            BuildLinear(settings),
            targetToViewport,
            LinearOnly(panelToViewport),
            hasViewportToLocal,
            viewportToLocal,
            viewportToPanel,
            startPivotViewport,
            grabOffset);
        return true;
    }

    internal static void UpdateOriginDrag(
        in OriginDragState state,
        TransformFeatureSettings settings,
        Point pointerViewport,
        bool bypassSnap)
    {
        if (!double.IsFinite(pointerViewport.X) || !double.IsFinite(pointerViewport.Y))
        {
            return;
        }

        var current = state;
        var desiredPivotViewport = new Point(
            pointerViewport.X + current.GrabOffset.X,
            pointerViewport.Y + current.GrabOffset.Y);

        if (current.HasViewportToLocal)
        {
            var localPoint = current.StartViewportToLocal.Map(desiredPivotViewport);
            var rawOriginX = localPoint.X / current.Width;
            var rawOriginY = localPoint.Y / current.Height;

            if (!bypassSnap && TryFindNearestSnapAnchor(
                desiredPivotViewport,
                (ax, ay) => current.StartTargetToViewport.Map(new Point(ax * current.Width, ay * current.Height)),
                out var snapX,
                out var snapY))
            {
                rawOriginX = snapX;
                rawOriginY = snapY;
            }

            var nextOriginX = RoundFraction(Math.Clamp(rawOriginX, MinOrigin, MaxOrigin));
            var nextOriginY = RoundFraction(Math.Clamp(rawOriginY, MinOrigin, MaxOrigin));

            // 保持组件画面静止：t1 = t0 + (P0 - P1) - L * (P0 - P1)。
            var deltaPivotX = (current.StartOriginX - nextOriginX) * current.Width;
            var deltaPivotY = (current.StartOriginY - nextOriginY) * current.Height;
            var (mappedDeltaX, mappedDeltaY) = MapVector(current.StartLinear, deltaPivotX, deltaPivotY);
            var deltaTranslateX = deltaPivotX - mappedDeltaX;
            var deltaTranslateY = deltaPivotY - mappedDeltaY;

            settings.OriginX = nextOriginX;
            settings.OriginY = nextOriginY;

            if (Math.Abs(deltaTranslateX) > 1e-9 || Math.Abs(deltaTranslateY) > 1e-9)
            {
                settings.TranslateX = RoundDecimal(Math.Clamp(current.StartTranslateX + deltaTranslateX, MinTranslate, MaxTranslate));
                settings.TranslateY = RoundDecimal(Math.Clamp(current.StartTranslateY + deltaTranslateY, MinTranslate, MaxTranslate));
            }
            else
            {
                if (settings.TranslateX != current.StartTranslateX)
                {
                    settings.TranslateX = current.StartTranslateX;
                }

                if (settings.TranslateY != current.StartTranslateY)
                {
                    settings.TranslateY = current.StartTranslateY;
                }
            }
        }
        else
        {
            var (deltaPanelX, deltaPanelY) = MapVector(
                current.StartViewportToPanel,
                desiredPivotViewport.X - current.StartPivotViewport.X,
                desiredPivotViewport.Y - current.StartPivotViewport.Y);
            var rawOriginX = current.StartOriginX + deltaPanelX / current.Width;
            var rawOriginY = current.StartOriginY + deltaPanelY / current.Height;

            if (!bypassSnap && TryFindNearestSnapAnchor(
                desiredPivotViewport,
                (ax, ay) =>
                {
                    var (vx, vy) = MapVector(
                        current.StartPanelToViewportLinear,
                        (ax - current.StartOriginX) * current.Width,
                        (ay - current.StartOriginY) * current.Height);
                    return new Point(current.StartPivotViewport.X + vx, current.StartPivotViewport.Y + vy);
                },
                out var snapX,
                out var snapY))
            {
                rawOriginX = snapX;
                rawOriginY = snapY;
            }

            settings.OriginX = RoundFraction(Math.Clamp(rawOriginX, MinOrigin, MaxOrigin));
            settings.OriginY = RoundFraction(Math.Clamp(rawOriginY, MinOrigin, MaxOrigin));
        }
    }

    internal static bool TryBeginRotateDrag(
        TransformFeatureSettings settings,
        double width,
        double height,
        BrushEditorMatrix targetToViewport,
        BrushEditorMatrix panelToViewport,
        Point dragStartViewport,
        out RotateDragState state)
    {
        state = default;
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height)
            || !panelToViewport.TryInverse(out var viewportToPanel))
        {
            return false;
        }

        var originX = Sanitize(settings.OriginX, 0.5);
        var originY = Sanitize(settings.OriginY, 0.5);
        var pivotViewport = targetToViewport.Map(new Point(originX * width, originY * height));
        var pivotPanel = viewportToPanel.Map(pivotViewport);
        var startPanel = viewportToPanel.Map(dragStartViewport);
        var dx = startPanel.X - pivotPanel.X;
        var dy = startPanel.Y - pivotPanel.Y;
        var hasPreviousAngle = double.Hypot(dx, dy) >= RotateMinPivotDistancePx;
        var previousAngle = hasPreviousAngle ? Math.Atan2(dy, dx) : 0;

        state = new RotateDragState(
            Sanitize(settings.Rotation, 0),
            viewportToPanel,
            pivotPanel,
            hasPreviousAngle,
            previousAngle,
            0);
        return true;
    }

    internal static void UpdateRotateDrag(
        ref RotateDragState state,
        TransformFeatureSettings settings,
        Point pointerViewport,
        bool snapAngle)
    {
        if (!double.IsFinite(pointerViewport.X) || !double.IsFinite(pointerViewport.Y))
        {
            return;
        }

        var currentPanel = state.ViewportToPanel.Map(pointerViewport);
        var dx = currentPanel.X - state.PivotPanel.X;
        var dy = currentPanel.Y - state.PivotPanel.Y;
        if (double.Hypot(dx, dy) < RotateMinPivotDistancePx)
        {
            return;
        }

        var angle = Math.Atan2(dy, dx);
        if (!state.HasPreviousAngle)
        {
            state.PreviousAngleRad = angle;
            state.HasPreviousAngle = true;
            return;
        }

        var step = angle - state.PreviousAngleRad;
        while (step > Math.PI)
        {
            step -= 2 * Math.PI;
        }

        while (step <= -Math.PI)
        {
            step += 2 * Math.PI;
        }

        state.PreviousAngleRad = angle;
        state.AccumulatedDeltaDeg += step * (180.0 / Math.PI);

        var raw = state.StartRotation + state.AccumulatedDeltaDeg;
        var snapped = snapAngle
            ? Math.Round(raw / RotationSnapStepDeg, MidpointRounding.AwayFromZero) * RotationSnapStepDeg
            : Math.Round(raw, 1, MidpointRounding.AwayFromZero);
        settings.Rotation = NormalizeZero(Math.Round(NormalizeRotation(snapped), 1, MidpointRounding.AwayFromZero));
    }

    internal static double NormalizeRotation(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            return 0;
        }

        var normalized = degrees % 360;
        if (normalized > 180)
        {
            normalized -= 360;
        }
        else if (normalized < -180)
        {
            normalized += 360;
        }

        return NormalizeZero(normalized);
    }

    internal static bool TryBeginScaleDrag(
        TransformFeatureSettings settings,
        double width,
        double height,
        ResizeCorner corner,
        BrushEditorMatrix targetToViewport,
        BrushEditorMatrix panelToViewport,
        Point dragStartViewport,
        out ScaleDragState state)
    {
        state = default;
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return false;
        }

        var postScaleToViewport = BuildPostScaleLinear(settings).Then(LinearOnly(panelToViewport));
        if (!postScaleToViewport.TryInverse(out var viewportToPostScale))
        {
            return false;
        }

        var originX = Sanitize(settings.OriginX, 0.5);
        var originY = Sanitize(settings.OriginY, 0.5);
        var cornerLocalX = corner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft ? 0 : width;
        var cornerLocalY = corner is ResizeCorner.TopLeft or ResizeCorner.TopRight ? 0 : height;
        var leverX = cornerLocalX - originX * width;
        var leverY = cornerLocalY - originY * height;

        var startPivotViewport = targetToViewport.Map(new Point(originX * width, originY * height));
        var startCornerViewport = targetToViewport.Map(new Point(cornerLocalX, cornerLocalY));
        var grabOffset = new Point(startCornerViewport.X - dragStartViewport.X, startCornerViewport.Y - dragStartViewport.Y);

        state = new ScaleDragState(
            Sanitize(settings.ScaleX, 1),
            Sanitize(settings.ScaleY, 1),
            settings.FlipHorizontal,
            settings.FlipVertical,
            leverX,
            leverY,
            startPivotViewport,
            grabOffset,
            viewportToPostScale);
        return true;
    }

    internal static void UpdateScaleDrag(
        in ScaleDragState state,
        TransformFeatureSettings settings,
        Point pointerViewport,
        bool toggleLock)
    {
        if (!double.IsFinite(pointerViewport.X) || !double.IsFinite(pointerViewport.Y))
        {
            return;
        }

        var desiredCornerViewport = new Point(
            pointerViewport.X + state.GrabOffset.X,
            pointerViewport.Y + state.GrabOffset.Y);
        var (u, v) = MapVector(
            state.ViewportToPostScale,
            desiredCornerViewport.X - state.StartPivotViewport.X,
            desiredCornerViewport.Y - state.StartPivotViewport.Y);
        if (!double.IsFinite(u) || !double.IsFinite(v))
        {
            return;
        }

        var proportional = settings.IsScaleLocked ^ toggleLock;
        if (!proportional)
        {
            var nextX = Math.Abs(state.LeverX) > 1e-3
                ? ApplyFlip(u / state.LeverX, state.FlipHorizontal)
                : state.StartScaleX;
            var nextY = Math.Abs(state.LeverY) > 1e-3
                ? ApplyFlip(v / state.LeverY, state.FlipVertical)
                : state.StartScaleY;

            settings.ScaleX = RoundFraction(Math.Clamp(nextX, MinScale, MaxScale));
            settings.ScaleY = RoundFraction(Math.Clamp(nextY, MinScale, MaxScale));
            return;
        }

        var effectiveStartX = ApplyFlip(state.StartScaleX, state.FlipHorizontal);
        var effectiveStartY = ApplyFlip(state.StartScaleY, state.FlipVertical);
        var ax = effectiveStartX * state.LeverX;
        var ay = effectiveStartY * state.LeverY;
        var initialLengthSq = ax * ax + ay * ay;

        double proportionalX;
        double proportionalY;
        if (initialLengthSq > 1e-6)
        {
            var factor = (u * ax + v * ay) / initialLengthSq;
            proportionalX = state.StartScaleX * factor;
            proportionalY = state.StartScaleY * factor;
        }
        else
        {
            var ux = ApplyFlip(state.LeverX, state.FlipHorizontal);
            var uy = ApplyFlip(state.LeverY, state.FlipVertical);
            var unitLengthSq = ux * ux + uy * uy;
            if (unitLengthSq <= 1e-6)
            {
                return;
            }

            var factor = (u * ux + v * uy) / unitLengthSq;
            proportionalX = factor;
            proportionalY = factor;
        }

        var maxAbs = Math.Max(Math.Abs(proportionalX), Math.Abs(proportionalY));
        if (maxAbs > MaxScale)
        {
            var clampFactor = MaxScale / maxAbs;
            proportionalX *= clampFactor;
            proportionalY *= clampFactor;
        }

        settings.ScaleX = RoundFraction(Math.Clamp(proportionalX, MinScale, MaxScale));
        settings.ScaleY = RoundFraction(Math.Clamp(proportionalY, MinScale, MaxScale));
    }

    private static (double X, double Y) ResolveOutwardTopNormal(
        Point topLeft,
        Point topRight,
        Point topCenter,
        Point center,
        BrushEditorMatrix? panelToViewport,
        TransformFeatureSettings? transform)
    {
        var fallback = ResolveFallbackUpNormal(panelToViewport, transform);
        var edgeX = topRight.X - topLeft.X;
        var edgeY = topRight.Y - topLeft.Y;
        var edgeLength = double.Hypot(edgeX, edgeY);
        var fromCenterX = topCenter.X - center.X;
        var fromCenterY = topCenter.Y - center.Y;
        var fromCenterLength = double.Hypot(fromCenterX, fromCenterY);

        if (edgeLength > 1e-3)
        {
            var n1x = edgeY / edgeLength;
            var n1y = -edgeX / edgeLength;
            if (fromCenterLength > 1e-3)
            {
                var dotCenter = n1x * fromCenterX + n1y * fromCenterY;
                if (Math.Abs(dotCenter) > 1e-6)
                {
                    return dotCenter > 0 ? (n1x, n1y) : (-n1x, -n1y);
                }
            }

            var dotFallback = n1x * fallback.X + n1y * fallback.Y;
            return dotFallback >= 0 ? (n1x, n1y) : (-n1x, -n1y);
        }

        if (fromCenterLength > 1e-3)
        {
            return (fromCenterX / fromCenterLength, fromCenterY / fromCenterLength);
        }

        return fallback;
    }

    private static (double X, double Y) ResolveFallbackUpNormal(
        BrushEditorMatrix? panelToViewport,
        TransformFeatureSettings? transform)
    {
        var localUpY = transform?.FlipVertical == true ? 1.0 : -1.0;
        var rotation = transform != null ? Sanitize(transform.Rotation, 0) * Math.PI / 180 : 0;
        var rotatedX = -localUpY * Math.Sin(rotation);
        var rotatedY = localUpY * Math.Cos(rotation);
        var (vx, vy) = panelToViewport is { } matrix
            ? MapVector(LinearOnly(matrix), rotatedX, rotatedY)
            : (rotatedX, rotatedY);
        var length = double.Hypot(vx, vy);
        return length > 1e-6 && double.IsFinite(length)
            ? (vx / length, vy / length)
            : (0, -1);
    }

    private static bool TryFindNearestSnapAnchor(
        Point desiredViewport,
        Func<double, double, Point> anchorToViewport,
        out double snapX,
        out double snapY)
    {
        snapX = 0;
        snapY = 0;
        var bestDistance = OriginSnapRadiusPx;
        var found = false;

        foreach (var ax in OriginSnapAnchors)
        {
            foreach (var ay in OriginSnapAnchors)
            {
                var candidate = anchorToViewport(ax, ay);
                var distance = double.Hypot(desiredViewport.X - candidate.X, desiredViewport.Y - candidate.Y);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    snapX = ax;
                    snapY = ay;
                    found = true;
                }
            }
        }

        return found;
    }

    private static bool IsFiniteMatrix(BrushEditorMatrix matrix) =>
        double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12)
        && double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22)
        && double.IsFinite(matrix.X) && double.IsFinite(matrix.Y);

    private static double Sanitize(double value, double fallback)
        => double.IsFinite(value) ? NormalizeZero(value) : fallback;

    private static double ApplyFlip(double value, bool flip)
        => NormalizeZero(flip ? -value : value);

    private static double NormalizeZero(double value)
        => value == 0 ? 0 : value;

    private static double RoundFraction(double value)
        => NormalizeZero(Math.Round(value, 3, MidpointRounding.AwayFromZero));

    private static double RoundDecimal(double value)
        => NormalizeZero(Math.Round(value, 2, MidpointRounding.AwayFromZero));

    internal readonly record struct OriginDragState(
        double Width,
        double Height,
        double StartOriginX,
        double StartOriginY,
        double StartTranslateX,
        double StartTranslateY,
        BrushEditorMatrix StartLinear,
        BrushEditorMatrix StartTargetToViewport,
        BrushEditorMatrix StartPanelToViewportLinear,
        bool HasViewportToLocal,
        BrushEditorMatrix StartViewportToLocal,
        BrushEditorMatrix StartViewportToPanel,
        Point StartPivotViewport,
        Point GrabOffset);

    internal struct RotateDragState(
        double startRotation,
        BrushEditorMatrix viewportToPanel,
        Point pivotPanel,
        bool hasPreviousAngle,
        double previousAngleRad,
        double accumulatedDeltaDeg)
    {
        public double StartRotation { get; } = startRotation;
        public BrushEditorMatrix ViewportToPanel { get; } = viewportToPanel;
        public Point PivotPanel { get; } = pivotPanel;
        public bool HasPreviousAngle { get; set; } = hasPreviousAngle;
        public double PreviousAngleRad { get; set; } = previousAngleRad;
        public double AccumulatedDeltaDeg { get; set; } = accumulatedDeltaDeg;
    }

    internal readonly record struct ScaleDragState(
        double StartScaleX,
        double StartScaleY,
        bool FlipHorizontal,
        bool FlipVertical,
        double LeverX,
        double LeverY,
        Point StartPivotViewport,
        Point GrabOffset,
        BrushEditorMatrix ViewportToPostScale);
}
