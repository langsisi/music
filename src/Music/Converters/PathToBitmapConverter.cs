using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Music.Converters;

/// <summary>
/// 把封面文件路径转换为 <see cref="Bitmap"/>。
/// 内部带一个有上限的缓存：滚动列表时不会反复解码同一张图。
/// 缓存溢出时整体丢弃（不主动 Dispose，避免释放仍被 Image 引用的位图），交由 GC 回收。
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    private const int MaxCached = 300;

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        if (Cache.Count >= MaxCached)
        {
            Cache.Clear();
        }

        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(path);
        }
        catch (Exception)
        {
            // 封面损坏就当作没有封面。
        }

        Cache[path] = bitmap;
        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
