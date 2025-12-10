using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ecliptix.Core.Shared.Converters;

public class DrawingColorConverter : IMultiValueConverter
{
    // Значення за замовчуванням (Fallback value)
    private const double DefaultStrokeThickness = 1.5;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // 1. Перевірка основних параметрів: Іконка та Колір
        if (values.Count < 2 ||
            values[0] is not Drawing drawing ||
            values[1] is not IBrush brush)
        {
            return null;
        }

        // 2. Отримання товщини (Fallback logic)
        double thickness = DefaultStrokeThickness;

        // Перевіряємо, чи є третій параметр і чи він double
        if (values.Count > 2 && values[2] is double t)
        {
            thickness = t;
        }

        return ApplyOutline(drawing, brush, thickness);
    }

    private Drawing ApplyOutline(Drawing drawing, IBrush brush, double thickness)
    {
        // Рекурсія для груп
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
                // Передаємо товщину далі рекурсивно
                newGroup.Children.Add(ApplyOutline(child, brush, thickness));
            }
            return newGroup;
        }

        // Логіка для геометрії
        if (drawing is GeometryDrawing geo)
        {
            return new GeometryDrawing
            {
                Geometry = geo.Geometry,

                // Заливка пуста (щоб був тільки контур)
                Brush = null,

                // Створюємо Pen з переданою товщиною
                Pen = new Pen(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
            };
        }

        return drawing;
    }
}
