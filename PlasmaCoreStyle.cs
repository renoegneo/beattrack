using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

public class PlasmaCoreStyle : IVisualizerStyle
{
    private struct Ember
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Size;
        public float Life;
        public float MaxLife;
    }

    private const int FilamentCount = 40;   // сколько "языков пламени", НЕ равно количеству полос спектра
    private const int JaggedDepth = 4;      // уровни рекурсии дрожания — 2^4 = 16 сегментов на языке

    private double[] _grouped = Array.Empty<double>();   // спектр, сжатый до FilamentCount групп
    private ValueFollower[] _filamentFollowers = Array.Empty<ValueFollower>();

    private readonly ValueFollower _bassFollower = new(halfLifeSeconds: 0.05);
    private readonly ValueFollower _midFollower = new(halfLifeSeconds: 0.08);
    private readonly ValueFollower _highFollower = new(halfLifeSeconds: 0.03);

    private double _previousBass;      // для детекта "удара" (onset detection)
    private float _flashIntensity;     // вспышка на баc-ударе, гаснет сама
    private float _hueShiftDegrees;    // накопленный поворот цвета от средних частот

    private readonly Ember[] _embers = new Ember[300];
    private readonly Random _random = new();

    // Огненная палитра: тёмно-красный -> оранжевый -> жёлтый -> почти белый на пиках
    private readonly ColorGradient _gradient = new(new[]
    {
        new ColorStop(0.00f, new Color(40, 0, 0, 255)),
        new ColorStop(0.35f, new Color(200, 40, 0, 255)),
        new ColorStop(0.65f, new Color(255, 140, 0, 255)),
        new ColorStop(1.00f, new Color(255, 240, 180, 255))
    });

    public PlasmaCoreStyle()
    {
        for (int i = 0; i < _embers.Length; i++)
            _embers[i].Life = 0f;
    }

    public void Update(AudioFrame audio, float deltaTime)
    {
        int spectrumLength = audio.Spectrum.Length;
        if (spectrumLength == 0) return;

        // Сжимаем честный полный спектр (70-128 полос) до фиксированных
        // FilamentCount "языков" — усредняем соседние индексы группами,
        // не выдумывая данные, просто укрупняя разрешение под визуал
        if (_grouped.Length != FilamentCount)
        {
            _grouped = new double[FilamentCount];
            _filamentFollowers = new ValueFollower[FilamentCount];
            for (int i = 0; i < FilamentCount; i++)
                _filamentFollowers[i] = new ValueFollower(halfLifeSeconds: 0.04);
        }

        for (int i = 0; i < FilamentCount; i++)
        {
            int start = i * spectrumLength / FilamentCount;
            int end = (i + 1) * spectrumLength / FilamentCount;
            end = Math.Max(end, start + 1); // на случай если группа "схлопнулась" в 0 элементов

            double sum = 0;
            for (int j = start; j < end; j++)
                sum += audio.Spectrum[j];

            double raw = sum / (end - start);
            _grouped[i] = _filamentFollowers[i].Update(raw, deltaTime);
        }

        // Классическая тройка — управляет ГЛОБАЛЬНЫМИ параметрами сцены,
        // а не отдельными языками пламени
        double bass = _bassFollower.Update(audio.AverageInRange(20, 250), deltaTime);
        double mid = _midFollower.Update(audio.AverageInRange(250, 4000), deltaTime);
        double high = _highFollower.Update(audio.AverageInRange(4000, 20000), deltaTime);

        // Onset-детект: если бас резко подскочил относительно прошлого
        // кадра — это "удар", запускаем вспышку. Простейший вид детекции
        // транзиентов — сравнение с предыдущим значением, без всякого ML
        double bassDelta = bass - _previousBass;
        if (bassDelta > 0.25)
            _flashIntensity = 1f;
        _previousBass = bass;

        _flashIntensity = MathF.Max(0f, _flashIntensity - deltaTime * 3f); // быстро гаснет сама

        // Средние частоты медленно крутят оттенок всей сцены
        _hueShiftDegrees += (float)mid * deltaTime * 40f;
        _hueShiftDegrees %= 360f;

        UpdateEmbers(deltaTime, (float)high, (float)bass);
    }

    private void UpdateEmbers(float deltaTime, float high, float bass)
    {
        for (int i = 0; i < _embers.Length; i++)
        {
            if (_embers[i].Life <= 0f) continue;

            _embers[i].Life -= deltaTime;
            _embers[i].Position += _embers[i].Velocity * deltaTime;
            _embers[i].Velocity.Y -= 15f * deltaTime; // лёгкое "всплытие" вверх, как у настоящих искр
            _embers[i].Velocity.X += (float)(_random.NextDouble() - 0.5) * 10f * deltaTime; // дрожание в стороны
        }

        // Чем сильнее ВЧ — тем чаще рождаются искры
        int spawnCount = (int)(high * 4);
        for (int i = 0; i < spawnCount; i++)
            SpawnEmber(bass);
    }

    private void SpawnEmber(float bass)
    {
        for (int i = 0; i < _embers.Length; i++)
        {
            if (_embers[i].Life <= 0f)
            {
                _embers[i].Position = new Vector2(
                    Raylib.GetScreenWidth() * (float)_random.NextDouble(),
                    Raylib.GetScreenHeight() * (0.6f + (float)_random.NextDouble() * 0.3f));
                _embers[i].Velocity = new Vector2(
                    (float)(_random.NextDouble() - 0.5) * 40f,
                    -60f - bass * 120f); // на сильном басу искры летят выше
                _embers[i].Size = 1.5f + (float)_random.NextDouble() * 2f;
                _embers[i].MaxLife = 0.6f + (float)_random.NextDouble() * 0.8f;
                _embers[i].Life = _embers[i].MaxLife;
                return;
            }
        }
    }

    public void Draw()
    {
        if (_grouped.Length == 0) return;

        int width = Raylib.GetScreenWidth();
        int height = Raylib.GetScreenHeight();
        float baseline = height * 0.95f;
        float maxHeight = height * 0.65f;

        DrawCoreGlow(width, height, baseline);
        DrawFilaments(width, baseline, maxHeight);
        DrawEmbers();
        DrawFlashOverlay(width, height);
    }

    // --- ФОН: дышащее ядро в основании сцены ---
    private void DrawCoreGlow(int width, int height, float baseline)
    {
        float bassPulse = (float)_bassFollower.Update(0, 0); // читаем последнее значение без пересчёта (deltaTime=0 не двигает сглаживание)
        float radius = width * 0.25f + bassPulse * width * 0.15f;

        Color inner = ShiftHue(_gradient.GetColor(0.6f), _hueShiftDegrees);
        Color outer = inner;
        outer.A = 0;

        Raylib.DrawCircleGradient(new Vector2(width / 2f, baseline), radius, inner, outer);
    }

    // --- ПЕРЕДНИЙ ПЛАН: языки пламени по всей ширине ---
    private void DrawFilaments(int width, float baseline, float maxHeight)
    {
        float slotWidth = width / (float)FilamentCount;

        for (int i = 0; i < FilamentCount; i++)
        {
            float val = (float)Math.Clamp(_grouped[i], 0.0, 1.0);
            float x = (i + 0.5f) * slotWidth;

            var bottom = new Vector2(x, baseline);
            var top = new Vector2(x, baseline - val * maxHeight);

            float wobble = slotWidth * 0.35f * (0.3f + val); // сильнее реагирует — сильнее дрожит
            var points = Jagged(bottom, top, wobble, JaggedDepth, _random);

            Color color = ShiftHue(_gradient.GetColor(val), _hueShiftDegrees);

            // Тройной проход: широкий и тусклый снизу (имитация свечения),
            // тонкий и яркий сверху — тот же приём bloom, что и в прошлом стиле
            DrawPolyline(points, 6f, WithAlpha(color, 60));
            DrawPolyline(points, 3f, WithAlpha(color, 140));
            DrawPolyline(points, 1.2f, WithAlpha(color, 255));
        }
    }

    private void DrawPolyline(List<Vector2> points, float thickness, Color color)
    {
        for (int i = 0; i < points.Count - 1; i++)
            Raylib.DrawLineEx(points[i], points[i + 1], thickness, color);
    }

    private void DrawEmbers()
    {
        for (int i = 0; i < _embers.Length; i++)
        {
            if (_embers[i].Life <= 0f) continue;

            float lifeRatio = _embers[i].Life / _embers[i].MaxLife;
            Color color = ShiftHue(_gradient.GetColor(0.9f), _hueShiftDegrees);
            color.A = (byte)(lifeRatio * 255);

            Raylib.DrawCircleV(_embers[i].Position, _embers[i].Size, color);
        }
    }

    // --- Вспышка на бас-ударе — полупрозрачный тёплый прямоугольник на весь экран ---
    private void DrawFlashOverlay(int width, int height)
    {
        if (_flashIntensity <= 0.01f) return;

        Color flash = new Color((byte)255, (byte)200, (byte)140, (byte)(_flashIntensity * 70));
        Raylib.DrawRectangle(0, 0, width, height, flash);
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        color.A = alpha;
        return color;
    }

    private static Color ShiftHue(Color color, float degrees)
    {
        Vector3 hsv = Raylib.ColorToHSV(color);
        hsv.X = (hsv.X + degrees) % 360f;
        return Raylib.ColorFromHSV(hsv.X, hsv.Y, hsv.Z);
    }

    private List<Vector2> Jagged(Vector2 a, Vector2 b, float displacement, int depth, Random rng)
    {
        if (depth <= 0)
            return new List<Vector2> { a, b };

        Vector2 mid = (a + b) / 2f;
        Vector2 direction = b - a;
        Vector2 normal = Vector2.Normalize(new Vector2(-direction.Y, direction.X));

        float offset = (float)(rng.NextDouble() * 2 - 1) * displacement;
        mid += normal * offset;

        var left = Jagged(a, mid, displacement * 0.5f, depth - 1, rng);
        var right = Jagged(mid, b, displacement * 0.5f, depth - 1, rng);

        left.RemoveAt(left.Count - 1);
        left.AddRange(right);
        return left;
    }
}



/*Raylib.DrawCircleGradient((int)(width / 2f), (int)baseline, radius, inner, outer);

No overload for method 'DrawCircleGradient' takes 5 arguments



Vector4 hsv = Raylib.ColorToHSV(color);

Cannot implicitly convert type 'System.Numerics.Vector3' to 'System.Numerics.Vector4'



Color flash = new Color(255, 200, 140, (byte)(_flashIntensity * 70));

The call is ambiguous between the following methods or properties: 'Raylib_cs.Color.Color(byte, byte, byte, byte)' and 'Raylib_cs.Color.Color(int, int, int, int)'*/