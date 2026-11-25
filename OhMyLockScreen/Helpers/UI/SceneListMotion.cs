using System.Numerics;

namespace OhMyLockScreen.Helpers.UI;

/// <summary>定义场景列表的 Fluent 进入与位移过渡动画参数及批次排序逻辑。</summary>
public static class SceneListMotion
{
    public const float EntranceOffsetY = 40f;
    public static readonly TimeSpan EntranceTranslationDuration = TimeSpan.FromMilliseconds(333);
    public static readonly TimeSpan EntranceOpacityDuration = TimeSpan.FromMilliseconds(167);
    public static readonly TimeSpan EntranceStaggerStep = TimeSpan.FromMilliseconds(33);
    public const int MaxEntranceStaggerIndex = 8;

    // Fluent Fast Out, Slow In (Decelerate) 曲线：cubic-bezier(0, 0, 0, 1)
    public static readonly Vector2 EntranceControlPoint1 = new(0.0f, 0.0f);
    public static readonly Vector2 EntranceControlPoint2 = new(0.0f, 1.0f);

    public static readonly TimeSpan OffsetTransitionDuration = TimeSpan.FromMilliseconds(333);

    // Fluent Point-to-Point 重排曲线：cubic-bezier(0.55, 0.55, 0, 1)
    public static readonly Vector2 OffsetControlPoint1 = new(0.55f, 0.55f);
    public static readonly Vector2 OffsetControlPoint2 = new(0.0f, 1.0f);

    // 等待初次布局提交 Offset 到 DComp 后再启用隐式过渡，避免卡片从 (0, 0) 或回收旧位置飞入。
    public const int OffsetArmRenderTicks = 2;

    public static TimeSpan GetEntranceDelay(int batchRank) =>
        TimeSpan.FromTicks(EntranceStaggerStep.Ticks * Math.Clamp(batchRank, 0, MaxEntranceStaggerIndex));

    public static List<T> OrderEntranceBatch<T>(IEnumerable<T> candidates, Func<T, int> indexSelector)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(indexSelector);

        var indexed = new List<(T Item, int Index)>();
        foreach (var candidate in candidates)
        {
            var index = indexSelector(candidate);
            if (index >= 0)
            {
                indexed.Add((candidate, index));
            }
        }

        indexed.Sort(static (left, right) => left.Index.CompareTo(right.Index));

        var ordered = new List<T>(indexed.Count);
        foreach (var entry in indexed)
        {
            ordered.Add(entry.Item);
        }

        return ordered;
    }
}
