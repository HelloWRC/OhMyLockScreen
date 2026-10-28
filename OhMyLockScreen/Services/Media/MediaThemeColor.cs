namespace OhMyLockScreen.Services.Media;

/// <summary>从缩小后的直通 BGRA 像素提取代表色；不应用展示层的暗化。</summary>
public static class MediaThemeColor
{
    public static uint Extract(ReadOnlySpan<byte> pixels)
    {
        var buckets = new Dictionary<int, (long Weight, long R, long G, long B)>();
        for (var index = 0; index + 3 < pixels.Length; index += 4)
        {
            var alpha = pixels[index + 3];
            if (alpha < 16)
            {
                continue;
            }

            int b = pixels[index], g = pixels[index + 1], r = pixels[index + 2];
            var key = (r >> 3) << 10 | (g >> 3) << 5 | b >> 3;
            var bucket = buckets.GetValueOrDefault(key);
            buckets[key] = (bucket.Weight + alpha, bucket.R + r * alpha, bucket.G + g * alpha, bucket.B + b * alpha);
        }

        if (buckets.Count == 0)
        {
            return 0xFF606060;
        }

        var dominant = buckets.OrderByDescending(entry => entry.Value.Weight).ThenBy(entry => entry.Key).First().Value;
        return 0xFF000000 | (uint)(dominant.R / dominant.Weight) << 16
            | (uint)(dominant.G / dominant.Weight) << 8 | (uint)(dominant.B / dominant.Weight);
    }
}
