using System;
using Raylib_cs;

// Одна точка градиента: позиция (0.0 ... 1.0) и цвет
public struct ColorStop
{
    public float Position; // 0.20f = 20%, 0.25f = 25%
    public Color Color;

    public ColorStop(float position, Color color)
    {
        Position = position;
        Color = color;
    }
}

// Сам класс градиента
public class ColorGradient
{
    private readonly ColorStop[] _stops;

    public ColorGradient(ColorStop[] stops)
    {
        _stops = stops;
        // Сортируем точки по возрастанию позиции, чтобы градиент не сломался
        Array.Sort(_stops, (a, b) => a.Position.CompareTo(b.Position));
    }

    // Главный метод: получает громкость (0.0...1.0) и возвращает готовый цвет
    public Color GetColor(float value)
    {
        // Math.Clamp держит значение строго в диапазоне 0.0 ... 1.0 (чтобы не зайти за границы)
        value = Math.Clamp(value, 0f, 1f);

        // Если громкость меньше самой первой точки — отдаем первый цвет
        if (value <= _stops[0].Position) return _stops[0].Color;

        // Если громкость больше самой последней точки — отдаем последний цвет
        if (value >= _stops[^1].Position) return _stops[^1].Color; // ^1 в C# значит "первый с конца"

        // Ищем, между какими двумя контрольными точками попало наше значение
        for (int i = 0; i < _stops.Length - 1; i++)
        {
            ColorStop start = _stops[i];
            ColorStop end = _stops[i + 1];

            if (value >= start.Position && value <= end.Position)
            {
                // Переводим глобальное значение в локальный процент (от 0.0 до 1.0) между СТАРТОМ и КОНЦОМ
                float localT = (value - start.Position) / (end.Position - start.Position);

                // Смешиваем цвета этих двух точек
                return LerpColor(start.Color, end.Color, localT);
            }
        }

        return _stops[0].Color;
    }

    // Вспомогательный метод: плавно смешивает два цвета по коэффициенту t (0.0 ... 1.0)
    private static Color LerpColor(Color a, Color b, float t)
    {
        byte r = (byte)(a.R + (b.R - a.R) * t);
        byte g = (byte)(a.G + (b.G - a.G) * t);
        byte bChannel = (byte)(a.B + (b.B - a.B) * t);
        byte alpha = (byte)(a.A + (b.A - a.A) * t);

        return new Color(r, g, bChannel, alpha);
    }
}