using System;
using System.Numerics;
using Raylib_cs;

public class SpectrumBarsStyle : IVisualizerStyle
{
    private struct Spark
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Size;
        public float Alpha;
        public float Life;
    }

    // Сколько промежуточных точек досчитывать МЕЖДУ каждой парой полос.
    // Больше — плавнее кривая, но больше отрисовки. 8 — разумный баланс
    private const int StepsPerSegment = 8;

    private double[] _heights = Array.Empty<double>();
    private float[] _peaks = Array.Empty<float>();
    private ValueFollower[] _followers = Array.Empty<ValueFollower>();
    private Vector2[] _bandPoints = Array.Empty<Vector2>(); // вершина каждой полосы (то, что раньше было верхом столбика)
    private Vector2[] _curvePoints = Array.Empty<Vector2>(); // досчитанная плавная кривая через все _bandPoints

    private readonly Spark[] _sparks = new Spark[400];
    private readonly Random _random = new();

    private int _regionX;
    private int _regionY;
    private int _regionWidth;
    private int _regionHeight;
    private bool _regionInitialized;

    private readonly ColorGradient _gradient = new(new[]
    {
        new ColorStop(0.00f, new Color(20, 10, 45, 220)),
        new ColorStop(0.25f, new Color(0, 180, 255, 255)),
        new ColorStop(0.65f, new Color(255, 0, 150, 255)),
        new ColorStop(1.00f, new Color(255, 255, 220, 255))
    });

    public SpectrumBarsStyle()
    {
        for (int i = 0; i < _sparks.Length; i++)
            _sparks[i].Life = 0f;
    }

    public void Update(AudioFrame audio, float deltaTime)
    {
        EnsureRegionInitialized();

        int spectrumLength = audio.Spectrum.Length;
        if (spectrumLength == 0) return;

        if (_followers.Length != spectrumLength)
        {
            _followers = new ValueFollower[spectrumLength];
            for (int i = 0; i < _followers.Length; i++)
                _followers[i] = new ValueFollower(halfLifeSeconds: 0.05);
        }

        if (_heights.Length != spectrumLength)
            _heights = new double[spectrumLength];

        for (int i = 0; i < spectrumLength; i++)
            _heights[i] = _followers[i].Update(audio.Spectrum[i], deltaTime);

        if (_peaks.Length != spectrumLength)
            Array.Resize(ref _peaks, spectrumLength);

        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparks[i].Life > 0f)
            {
                _sparks[i].Life -= deltaTime;
                _sparks[i].Position += _sparks[i].Velocity * deltaTime;
                _sparks[i].Velocity.Y += 320f * deltaTime;
                _sparks[i].Alpha = Math.Clamp(_sparks[i].Life / 0.5f, 0f, 1f);
            }
        }
    }

    private void EnsureRegionInitialized()
    {
        if (_regionInitialized) return;

        int monitorCount = Raylib.GetMonitorCount();
        int minX = int.MaxValue, minY = int.MaxValue;
        for (int i = 0; i < monitorCount; i++)
        {
            var pos = Raylib.GetMonitorPosition(i);
            minX = Math.Min(minX, (int)pos.X);
            minY = Math.Min(minY, (int)pos.Y);
        }

        int rightmostMonitor = 0;
        int rightmostX = (int)Raylib.GetMonitorPosition(0).X;
        for (int i = 1; i < monitorCount; i++)
        {
            int x = (int)Raylib.GetMonitorPosition(i).X;
            if (x > rightmostX) { rightmostX = x; rightmostMonitor = i; }
        }

        var position = Raylib.GetMonitorPosition(rightmostMonitor);
        _regionX = (int)position.X - minX;
        _regionY = (int)position.Y - minY;
        _regionWidth = Raylib.GetMonitorWidth(rightmostMonitor);
        _regionHeight = Raylib.GetMonitorHeight(rightmostMonitor);
        _regionInitialized = true;

        Console.WriteLine($"[SpectrumBarsStyle] Правый монитор #{rightmostMonitor}: локально {_regionX},{_regionY}, размер {_regionWidth}x{_regionHeight}");
    }

    private void SpawnSpark(Vector2 pos)
    {
        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparks[i].Life <= 0f)
            {
                _sparks[i].Position = pos;
                _sparks[i].Velocity = new Vector2(
                    (float)(_random.NextDouble() - 0.5) * 180f,
                    (float)(-160 - _random.NextDouble() * 320));
                _sparks[i].Size = (float)(1.5 + _random.NextDouble() * 2.5);
                _sparks[i].Alpha = 1.0f;
                _sparks[i].Life = (float)(0.25 + _random.NextDouble() * 0.4);
                break;
            }
        }
    }

    // Достаёт точку из массива, "прижимая" индекс к границам вместо выхода
    // за пределы массива — так у самой первой и последней полосы всё равно
    // есть "соседи" для расчёта формулы, просто повторяющие крайнюю точку
    private static Vector2 GetClamped(Vector2[] points, int index)
    {
        if (index < 0) index = 0;
        if (index >= points.Length) index = points.Length - 1;
        return points[index];
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            2f * p1 +
            (p2 - p0) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (3f * p1 - p0 - 3f * p2 + p3) * t3
        );
    }

    public void Draw()
    {
        if (_heights.Length == 0) return;

        float deltaTime = Raylib.GetFrameTime();
        int count = _heights.Length;

        float paddingX = _regionWidth * 0.05f;
        float usableWidth = _regionWidth - (paddingX * 2f);
        float slotWidth = usableWidth / count;
        float maxBarHeight = _regionHeight * 0.90f;
        float baseline = _regionY + _regionHeight - 20f;

        // --- 1. Строим вершину каждой полосы — ровно то же, что раньше
        // было "верхом столбика", просто теперь это ТОЧКА, а не прямоугольник
        if (_bandPoints.Length != count)
            _bandPoints = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            float val = (float)Math.Clamp(_heights[i], 0.0, 1.0);
            float centerX = _regionX + paddingX + (i + 0.5f) * slotWidth;
            float y = baseline - val * maxBarHeight;
            _bandPoints[i] = new Vector2(centerX, y);

            if (val > _peaks[i]) _peaks[i] = val;
            else
            {
                _peaks[i] -= 0.65f * deltaTime;
                if (_peaks[i] < 0f) _peaks[i] = 0f;
            }

            float sparkRateMultiplier = 32f / count;
            if (val > 0.60f && _random.NextDouble() < (val * 0.35f * sparkRateMultiplier))
                SpawnSpark(new Vector2(centerX, y));
        }

        // --- 2. Досчитываем плавную кривую через все точки ---
        int sampleCount = (count - 1) * StepsPerSegment + 1;
        if (_curvePoints.Length != sampleCount)
            _curvePoints = new Vector2[sampleCount];

        int sampleIndex = 0;
        for (int i = 0; i < count - 1; i++)
        {
            Vector2 p0 = GetClamped(_bandPoints, i - 1);
            Vector2 p1 = _bandPoints[i];
            Vector2 p2 = _bandPoints[i + 1];
            Vector2 p3 = GetClamped(_bandPoints, i + 2);

            for (int step = 0; step < StepsPerSegment; step++)
            {
                float t = step / (float)StepsPerSegment;
                _curvePoints[sampleIndex++] = CatmullRom(p0, p1, p2, p3, t);
            }
        }
        _curvePoints[sampleIndex] = _bandPoints[count - 1]; // последняя точка ровно на месте, без интерполяции

        // --- 3. Подложечное свечение (без изменений) ---
        Raylib.DrawRectangleGradientV(
            (int)(_regionX + paddingX), (int)baseline, (int)usableWidth, 16,
            new Color(0, 180, 255, 50), new Color(0, 0, 0, 0));

        // --- 4. Заливка под кривой — маленькими трапециями между
        // соседними точками, цвет каждой берём из ЕЁ ЖЕ высоты. Кусочки
        // мелкие (StepsPerSegment=8 на полосу), поэтому глаз воспринимает
        // это как непрерывный градиент, а не отдельные плашки
        for (int i = 0; i < _curvePoints.Length - 1; i++)
        {
            Vector2 a = _curvePoints[i];
            Vector2 b = _curvePoints[i + 1];

            float localValue = Math.Clamp((baseline - (a.Y + b.Y) / 2f) / maxBarHeight, 0f, 1f);
            Color fillColor = _gradient.GetColor(localValue);

            var baselineA = new Vector2(a.X, baseline);
            var baselineB = new Vector2(b.X, baseline);

            Raylib.DrawTriangle(a, baselineA, baselineB, fillColor);
            Raylib.DrawTriangle(a, baselineB, b, fillColor);
        }

        // --- 5. Свечение поверх кривой (несколько толстых полупрозрачных
        // проходов снизу, тонкая яркая линия сверху — дешёвая имитация bloom,
        // без реальных шейдеров) ---
        DrawGlowingCurve(3f, 60);
        DrawGlowingCurve(1.6f, 140);
        DrawGlowingCurve(0.6f, 255);

        // --- 6. Падающие пиковые плашки — по одной на КАЖДУЮ полосу
        // (не по кривой, а по настоящей вершине конкретной полосы) ---
        for (int i = 0; i < count; i++)
        {
            float peakY = baseline - _peaks[i] * maxBarHeight;
            Color peakColor = _gradient.GetColor(Math.Clamp(_peaks[i] + 0.3f, 0f, 1f));
            Raylib.DrawCircleV(new Vector2(_bandPoints[i].X, peakY), 2f, peakColor);
        }

        // --- 7. Искры (без изменений) ---
        for (int i = 0; i < _sparks.Length; i++)
        {
            ref var s = ref _sparks[i];
            if (s.Life <= 0f) continue;

            Color sparkCol = _gradient.GetColor(0.85f);
            sparkCol.A = (byte)(s.Alpha * 255);
            Color glowCol = sparkCol;
            glowCol.A = (byte)(s.Alpha * 80);

            Raylib.DrawCircleV(s.Position, s.Size * 2f, glowCol);
            Raylib.DrawCircleV(s.Position, s.Size, sparkCol);
        }
    }

    private void DrawGlowingCurve(float thickness, byte alpha)
{
    float baseline = _regionY + _regionHeight - 20f;
    float maxBarHeight = _regionHeight * 0.90f;

    for (int i = 0; i < _curvePoints.Length - 1; i++)
    {
        Vector2 a = _curvePoints[i];
        Vector2 b = _curvePoints[i + 1];

        float localValue = Math.Clamp((baseline - (a.Y + b.Y) / 2f) / maxBarHeight, 0f, 1f);
        Color lineColor = _gradient.GetColor(localValue);
        lineColor.A = alpha;

        Raylib.DrawLineEx(a, b, thickness, lineColor);
    }
}

    private float GetLocalValueForGlow(Vector2 a, Vector2 b)
    {
        float baseline = _regionY + _regionHeight - 20f;
        float maxBarHeight = _regionHeight * 0.90f;
        return Math.Clamp((baseline - (a.Y + b.Y) / 2f) / maxBarHeight, 0f, 1f);
    }
}