using System;
using System.Numerics;
using Raylib_cs;

public class FullWidthBarcodeStyle : IVisualizerStyle
{
    private struct Spark
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Size;
        public float Alpha;
        public float Life;
        public bool IsSuperCharged;
    }

    private const int BAR_COUNT = 180;
    private readonly Spark[] _sparks = new Spark[350]; // Увеличен пул под новый поток
    private readonly Random _random = new();

    private readonly ValueFollower _bassFollower = new(halfLifeSeconds: 0.02);
    private readonly ValueFollower _midFollower  = new(halfLifeSeconds: 0.04);
    private readonly ValueFollower _highFollower = new(halfLifeSeconds: 0.015);

    private float _bass;
    private float _mid;
    private float _high;
    private float _time;

    private readonly ColorGradient _gradient = new(new[]
    {
        new ColorStop(0.00f, new Color(15, 25, 60, 220)),
        new ColorStop(0.30f, new Color(0, 200, 255, 255)),
        new ColorStop(0.70f, new Color(255, 0, 160, 255)),
        new ColorStop(1.00f, new Color(255, 255, 240, 255))
    });

    public FullWidthBarcodeStyle()
    {
        for (int i = 0; i < _sparks.Length; i++)
        {
            _sparks[i].Life = 0f;
        }
    }

    private void SpawnSpark(Vector2 pos, bool isSuperCharged)
    {
        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparks[i].Life <= 0f)
            {
                _sparks[i].Position = pos;
                
                float speedBoost = isSuperCharged ? 1.35f : 1.0f;
                // Увеличена начальная скорость (вылет резче и выше)
                _sparks[i].Velocity = new Vector2(
                    (float)(_random.NextDouble() - 0.5) * 180f * speedBoost,
                    (float)(-180 - _random.NextDouble() * 420) * speedBoost 
                );
                
                _sparks[i].Size = (float)(2.0 + _random.NextDouble() * 3.5) * (isSuperCharged ? 1.25f : 1.0f);
                _sparks[i].Alpha = 1.0f;
                _sparks[i].Life = (float)(0.35 + _random.NextDouble() * 0.55);
                _sparks[i].IsSuperCharged = isSuperCharged;
                break;
            }
        }
    }

    public void Update(AudioFrame audio, float deltaTime)
    {
        float rawBass = (float)_bassFollower.Update(audio.Bass, deltaTime);
        float rawMid  = (float)_midFollower.Update(audio.Mid, deltaTime);
        float rawHigh = (float)_highFollower.Update(audio.High, deltaTime);

        _bass = MathF.Pow(rawBass, 1.6f);
        _mid  = MathF.Pow(rawMid,  1.4f);
        _high = MathF.Pow(rawHigh, 1.3f);

        _time += deltaTime * 5f;

        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparks[i].Life > 0f)
            {
                _sparks[i].Life -= deltaTime;
                _sparks[i].Position += _sparks[i].Velocity * deltaTime;
                _sparks[i].Velocity.Y += 240f * deltaTime; // Гравитация дожата для динамичного дугового спада
                _sparks[i].Alpha = Math.Clamp(_sparks[i].Life / 0.5f, 0f, 1f);
            }
        }
    }

    public void Draw()
    {
        float screenWidth = Raylib.GetScreenWidth();
        float screenHeight = Raylib.GetScreenHeight();

        float barWidth = screenWidth / BAR_COUNT;
        float padding = 2f;
        float actualWidth = MathF.Max(1f, barWidth - padding);

        for (int i = 0; i < BAR_COUNT; i++)
        {
            float normIndex = (float)i / BAR_COUNT;

            float waveBass = MathF.Pow(MathF.Sin(normIndex * MathF.PI), 2f) * (_bass * (screenHeight * 0.65f));
            float waveMid  = MathF.Sin(i * 0.12f + _time) * (_mid * (screenHeight * 0.25f));
            float waveHigh = MathF.Sin(i * 0.35f - _time * 2f) * (_high * (screenHeight * 0.15f));

            float barHeight = 15f + waveBass + MathF.Abs(waveMid) + MathF.Abs(waveHigh);
            barHeight = Math.Clamp(barHeight, 10f, screenHeight * 0.9f);

            float posX = i * barWidth;
            float posY = screenHeight - barHeight;

            float intensity = barHeight / (screenHeight * 0.8f);
            Color barColor = _gradient.GetColor(Math.Clamp(intensity, 0f, 1f));

            Raylib.DrawRectangleV(new Vector2(posX, posY), new Vector2(actualWidth, barHeight), barColor);

            Color capColor = _gradient.GetColor(Math.Clamp(intensity + 0.2f, 0f, 1f));
            Raylib.DrawRectangleV(new Vector2(posX, posY - 4f), new Vector2(actualWidth, 4f), capColor);

            if (_high > 0.12f)
            {
                float spawnChance = _high * 0.25f;
                bool isHighPeak = intensity >= 0.60f;

                if (isHighPeak)
                {
                    spawnChance *= 1.62f; // Еще +20% (1.35 * 1.2 = 1.62)
                }

                if (_random.NextDouble() < spawnChance)
                {
                    SpawnSpark(new Vector2(posX + actualWidth * 0.5f, posY), isHighPeak);
                }
            }
        }

        for (int i = 0; i < _sparks.Length; i++)
        {
            ref var s = ref _sparks[i];
            if (s.Life <= 0f) continue;

            if (s.IsSuperCharged)
            {
                Color glowCol = _gradient.GetColor(0.95f);
                glowCol.A = (byte)(s.Alpha * 120);
                Raylib.DrawCircleV(s.Position, s.Size * 2.5f, glowCol);

                Color coreCol = new Color((byte)255, (byte)255, (byte)255, (byte)(s.Alpha * 255));
                Raylib.DrawCircleV(s.Position, s.Size, coreCol);
            }
            else
            {
                Color sparkCol = _gradient.GetColor(0.80f);
                sparkCol.A = (byte)(s.Alpha * 255);
                Raylib.DrawCircleV(s.Position, s.Size, sparkCol);
            }
        }
    }
}