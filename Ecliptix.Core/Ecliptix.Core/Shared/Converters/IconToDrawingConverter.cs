using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ecliptix.Core.Shared.Converters;

public class IconToDrawingConverter : IMultiValueConverter
{
    private static readonly Dictionary<IconCacheKey, Drawing> _drawingCache = new();

    private readonly struct IconCacheKey : IEquatable<IconCacheKey>
    {
        public Enum Kind { get; }
        public IBrush? Brush { get; }
        public double Thickness { get; }

        public IconCacheKey(Enum kind, IBrush? brush, double thickness)
        {
            Kind = kind;
            Brush = brush;
            Thickness = thickness;
        }

        public bool Equals(IconCacheKey other)
        {
            return Equals(Kind, other.Kind) &&
                   Equals(Brush, other.Brush) &&
                   Thickness.Equals(other.Thickness);
        }

        public override bool Equals(object? obj) => obj is IconCacheKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Kind, Brush, Thickness);
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3 ||
            values[0] is not Enum iconKind ||
            values[1] is not IBrush brush ||
            values[2] is not double thickness)
        {
            return null;
        }

        IconCacheKey key = new(iconKind, brush, thickness);
        if (_drawingCache.TryGetValue(key, out Drawing? cachedDrawing))
        {
            return cachedDrawing;
        }

        Drawing? newDrawing = CreateDrawing(iconKind, brush, thickness);

        if (newDrawing != null)
        {
            _drawingCache[key] = newDrawing;
        }

        return newDrawing;
    }

    private Drawing? CreateDrawing(Enum iconKind, IBrush? brush, double thickness)
    {
        string resourceKey = iconKind.ToString();

        object? resource = null;

        bool found = Application.Current!.TryGetResource(resourceKey, null, out resource);

        if (!found || resource == null)
        {
            return new GeometryDrawing
            {
                Geometry = new RectangleGeometry(new Rect(0,0,24,24)),
                Brush = Brushes.Magenta
            };
        }

        if (resource is Geometry geometry)
        {
            return new GeometryDrawing
            {
                Geometry = geometry,
                Pen = new Pen(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
            };
        }
        if (resource is DrawingGroup group)
        {
            return CloneDrawing(group, brush, thickness);
        }
        if (resource is DrawingImage drawingImage && drawingImage.Drawing != null)
        {
            return CloneDrawing(drawingImage.Drawing, brush, thickness);
        }

        return null;
    }

    private Drawing CloneDrawing(Drawing drawing, IBrush? brush, double thickness)
    {
        if (drawing is DrawingGroup group)
        {
            DrawingGroup newGroup = new DrawingGroup
            {
                Opacity = group.Opacity,
                Transform = group.Transform,
                ClipGeometry = group.ClipGeometry,
                OpacityMask = group.OpacityMask
            };

            foreach (Drawing? child in group.Children)
            {
                newGroup.Children.Add(CloneDrawing(child, brush, thickness));
            }
            return newGroup;
        }

        if (drawing is GeometryDrawing geo)
        {
            IDashStyle? originalDash = geo.Pen?.DashStyle;

            return new GeometryDrawing
            {
                Geometry = geo.Geometry,
                Brush = null,
                Pen = new Pen(
                    brush,
                    thickness,
                    lineCap: PenLineCap.Round,
                    lineJoin: PenLineJoin.Round,
                    dashStyle: originalDash)
            };
        }
        return drawing;
    }
}
