using System;
using System.Numerics;
using Raylib_cs;

public class NebulaStyle : IVisualizerStyle
{
    private struct Particle
    {
        public float Angle;        // Угол на орбите (в радианах)
        public float BaseDistance; // Базовый радиус от центра
        public float Speed;        // Индивидуальная скорость вращения
        public float Size;         // Размер частицы
        public float ColorShift;   // Индивидуальное смещение по палитре
    }

    private readonly Particle[] _particles = new Particle[320];
    private readonly Random _random = new();

    private readonly ValueFollower _bassFollower = new(halfLifeSeconds: 0.05);
    private readonly ValueFollower _midFollower  = new(halfLifeSeconds: 0.08);
    private readonly ValueFollower _highFollower = new(halfLifeSeconds: 0.04);

    // Храним сглаженные значения между Update и Draw
    private float _bass;
    private float _mid;
    private float _high;

    // Космическая палитра: Тьма -> Фиолетовый -> Неоновый Циан -> Ярко-белый
    private readonly ColorGradient _gradient = new(new[]
    {
        new ColorStop(0.00f, new Color(20, 10, 35, 255)),
        new ColorStop(0.35f, new Color(130, 30, 200, 255)),
        new ColorStop(0.70f, new Color(0, 220, 255, 255)),
        new ColorStop(1.00f, new Color(255, 255, 240, 255))
    });

    public NebulaStyle()
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            _particles[i] = new Particle
            {
                Angle = (float)(_random.NextDouble() * Math.PI * 2),
                BaseDistance = (float)(15 + Math.Pow(_random.NextDouble(), 1.6) * 400),
                Speed = (float)(0.3 + _random.NextDouble() * 1.2),
                Size = (float)(1.2 + _random.NextDouble() * 3.0),
                ColorShift = (float)_random.NextDouble() * 0.2f
            };
        }
    }

    public void Update(AudioFrame audio, float deltaTime)
    {
        // Записываем результат напрямую в поля класса
        _bass = (float)_bassFollower.Update(audio.Bass, deltaTime);
        _mid  = (float)_midFollower.Update(audio.Mid, deltaTime);
        _high = (float)_highFollower.Update(audio.High, deltaTime);

        float speedMultiplier = 1.0f + _mid * 5.0f;

        for (int i = 0; i < _particles.Length; i++)
        {
            _particles[i].Angle += _particles[i].Speed * speedMultiplier * deltaTime;
            if (_particles[i].Angle > Math.PI * 2)
            {
                _particles[i].Angle -= (float)(Math.PI * 2);
            }
        }
    }

    public void Draw()
    {
        // Динамический центр правого монитора (75% от общей ширины 2x FHD)
        Vector2 center = new Vector2(Raylib.GetScreenWidth() * 0.75f, Raylib.GetScreenHeight() * 0.5f);

        // 1. Центральное размытое ядро
        float coreRadius = 30f + _bass * 140f;
        Color coreColor = _gradient.GetColor(_bass * 0.6f);
        
        // Полупрозрачная подложка ядра
        Raylib.DrawCircleV(center, coreRadius, new Color(coreColor.R, coreColor.G, coreColor.B, (byte)80));

        // 2. Отрисовка вихря частиц
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];

            // НЧ выталкивают частицы дальше от центра
            float pushFactor = p.BaseDistance / 400f;
            float currentDist = p.BaseDistance + (_bass * 180f * pushFactor);

            // ВЧ добавляют хаотичную вибрацию
            float jitterX = (float)(_random.NextDouble() - 0.5) * _high * 12f;
            float jitterY = (float)(_random.NextDouble() - 0.5) * _high * 12f;

            float x = center.X + (float)Math.Cos(p.Angle) * currentDist + jitterX;
            float y = center.Y + (float)Math.Sin(p.Angle) * currentDist + jitterY;

            // Позиция в градиенте зависит от расстояния + сдвиг от ВЧ
            float gradientPos = (currentDist / 500f) + p.ColorShift + (_high * 0.35f);
            Color pColor = _gradient.GetColor(gradientPos);

            float renderSize = p.Size + (_bass * 1.5f);

            Raylib.DrawCircleV(new Vector2(x, y), renderSize, pColor);
        }
    }
}