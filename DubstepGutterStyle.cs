using System;
using System.Numerics;
using Raylib_cs;

public class DubstepGutterStyle : IVisualizerStyle
{
    private struct SnowParticle
    {
        public Vector2 Position;
        public float BaseSpeed;
        public float Size;
        public float Alpha;
    }

    private readonly SnowParticle[] _particles = new SnowParticle[200];
    private readonly Random _random = new();

    private readonly ValueFollower _bassFollower = new(halfLifeSeconds: 0.05);
    private readonly ValueFollower _midFollower  = new(halfLifeSeconds: 0.07);
    private readonly ValueFollower _highFollower = new(halfLifeSeconds: 0.04);

    private float _bass;
    private float _mid;
    private float _high;

    private float _waveTime;
    private Vector2 _shakeOffset;

    private readonly ColorGradient _gradient = new(new[]
    {
        new ColorStop(0.00f, new Color(15, 5, 30, 255)),
        new ColorStop(0.25f, new Color(0, 220, 255, 255)),
        new ColorStop(0.65f, new Color(255, 0, 140, 255)),
        new ColorStop(1.00f, new Color(255, 255, 240, 255))
    });

    public DubstepGutterStyle()
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ResetParticle(ref _particles[i], randomY: true);
        }
    }

    private void ResetParticle(ref SnowParticle p, bool randomY = false)
    {
        float screenWidth = Raylib.GetScreenWidth();
        float screenHeight = Raylib.GetScreenHeight();

        p.Position.X = (float)(_random.NextDouble() * screenWidth);
        p.Position.Y = randomY ? (float)(_random.NextDouble() * screenHeight) : -15f;
        p.BaseSpeed = (float)(80 + _random.NextDouble() * 180);
        p.Size = (float)(1.5 + _random.NextDouble() * 3.5);
        p.Alpha = (float)(0.3 + _random.NextDouble() * 0.7);
    }

    public void Update(AudioFrame audio, float deltaTime)
    {
        _bass = (float)_bassFollower.Update(audio.Bass, deltaTime);
        _mid  = (float)_midFollower.Update(audio.Mid, deltaTime);
        _high = (float)_highFollower.Update(audio.High, deltaTime);

        _waveTime += deltaTime * 4f;

        // Тряска
        float totalEnergy = _bass * 1.2f + _high * 0.5f;
        if (totalEnergy > 0.08f)
        {
            float shakePower = totalEnergy * 30f;
            _shakeOffset = new Vector2(
                (float)(_random.NextDouble() - 0.5) * shakePower,
                (float)(_random.NextDouble() - 0.5) * shakePower
            );
        }
        else
        {
            _shakeOffset = Vector2.Zero;
        }

        // Снег
        float speedMultiplier = 1.0f + _bass * 4.0f;
        float windSway = MathF.Sin(_waveTime * 1.5f) * (_mid * 80f);

        float screenHeight = Raylib.GetScreenHeight();

        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            
            p.Position.Y += (p.BaseSpeed * speedMultiplier) * deltaTime;
            p.Position.X += windSway * deltaTime;

            if (p.Position.Y > screenHeight + 15)
            {
                ResetParticle(ref p, randomY: false);
            }
        }
    }

    public void Draw()
    {
        Vector2 center = new Vector2(
            Raylib.GetScreenWidth() * 0.75f + _shakeOffset.X, 
            Raylib.GetScreenHeight() * 0.5f + _shakeOffset.Y
        );

        // 1. СНЕГ
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            Vector2 pPos = p.Position + _shakeOffset;

            byte currentAlpha = (byte)Math.Clamp((p.Alpha + _high * 0.5f) * 255, 0, 255);
            Color snowColor = new Color((byte)220, (byte)240, (byte)255, currentAlpha);

            float renderSize = p.Size + (_high * 2.5f);
            Raylib.DrawCircleV(pPos, renderSize, snowColor);
        }

        // 2. БАЗОВОЕ ЯДРО
        float baseRadius = 160f + _bass * 80f;
        Color coreColor = _gradient.GetColor(_bass);

        Raylib.DrawCircleV(center, baseRadius + 25f + (_bass * 50f), new Color(coreColor.R, coreColor.G, coreColor.B, (byte)45));
        Raylib.DrawCircleV(center, baseRadius, new Color(10, 5, 18, 240));
        Raylib.DrawCircleLinesV(center, baseRadius, coreColor);

        // 3. ПЛАВНАЯ ВОЛНА С УВЕЛИЧЕННОЙ ВЫСОТОЙ ПИКОВ (+60%)
        int segments = 90; 
        Vector2[] wavePoints = new Vector2[segments];
        float angleStep = (MathF.PI * 2f) / segments;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * angleStep;

            // Амплитуды подняты с (120, 80, 50) до (195, 130, 85)
            float wave1 = MathF.Sin(angle * 3f + _waveTime) * (_bass * 195f);
            float wave2 = MathF.Sin(angle * 6f - _waveTime * 1.5f) * (_mid * 130f);
            float wave3 = MathF.Sin(angle * 12f + _waveTime * 2f) * (_high * 85f);

            float currentRadius = baseRadius + wave1 + wave2 + wave3;

            float x = center.X + MathF.Cos(angle) * currentRadius;
            float y = center.Y + MathF.Sin(angle) * currentRadius;
            wavePoints[i] = new Vector2(x, y);

            if (i % 3 == 0)
            {
                float innerX = center.X + MathF.Cos(angle) * baseRadius;
                float innerY = center.Y + MathF.Sin(angle) * baseRadius;

                // Нормализация под более высокий диапазон (320px)
                float intensity = (currentRadius - baseRadius) / 320f;
                Color lineCol = _gradient.GetColor(Math.Clamp(intensity, 0f, 1f));
                Color rayColor = new Color(lineCol.R, lineCol.G, lineCol.B, (byte)90);

                Raylib.DrawLineEx(new Vector2(innerX, innerY), wavePoints[i], 1.5f, rayColor);
            }
        }

        // Внешнее кольцо
        for (int i = 0; i < segments; i++)
        {
            Vector2 p1 = wavePoints[i];
            Vector2 p2 = wavePoints[(i + 1) % segments];

            float dist = Vector2.Distance(p1, center) - baseRadius;
            Color lineCol = _gradient.GetColor(Math.Clamp(dist / 320f, 0f, 1f));

            Raylib.DrawLineEx(p1, p2, 3.5f, lineCol);
        }
    }
}