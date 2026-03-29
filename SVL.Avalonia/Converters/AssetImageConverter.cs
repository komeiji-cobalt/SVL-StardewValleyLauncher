using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Globalization;
using System.IO;

namespace SVL.Avalonia.Converters;

public sealed class AssetImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Bitmap bitmap)
        {
            return bitmap;
        }

        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = StripQueryAndFragment(path);

        try
        {
            if (File.Exists(normalizedPath))
            {
                return new Bitmap(normalizedPath);
            }

            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile)
                {
                    var localPath = Uri.UnescapeDataString(uri.LocalPath);
                    if (File.Exists(localPath))
                    {
                        return new Bitmap(localPath);
                    }

                    return null;
                }

                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }

            if (Uri.TryCreate(normalizedPath, UriKind.Absolute, out var normalizedUri))
            {
                if (normalizedUri.IsFile)
                {
                    var localPath = Uri.UnescapeDataString(normalizedUri.LocalPath);
                    if (File.Exists(localPath))
                    {
                        return new Bitmap(localPath);
                    }

                    return null;
                }

                using var stream = AssetLoader.Open(normalizedUri);
                return new Bitmap(stream);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string StripQueryAndFragment(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var index = path.IndexOfAny(['?', '#']);
        return index < 0 ? path : path[..index];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
