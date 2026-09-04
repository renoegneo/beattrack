using System.Numerics;
using Raylib_cs;

public class CustomGradientStyle : IVisualizerStyle
{
    private readonly ValueFollower _bassFollower = new(halfLifeSeconds: 0.05);

    // НАСТРАИВАЕМ СВОЙ ГРАДИЕНТ:
    // На 0% — Темно-синий
    // На 20% — Фиолетовый
    // На 25% — Розовый (очень резкий переход за 5% громкости!)
    // На 100% — Огненно-жёлтый
    private readonly ColorGradient _bassGradient = new(new[]
    {
        new ColorStop(0.00f, Color.DarkBlue),
        new ColorStop(0.20f, Color.Purple),
        new ColorStop(0.25f, Color.Pink),
        new ColorStop(1.00f, Color.Gold)
    });

    private Color _currentColor;
    private float _radius;

    public void Update(AudioFrame audio, float deltaTime)
    {
        double smoothBass = _bassFollower.Update(audio.Bass, deltaTime);
        float bassFactor = (float)smoothBass; // Переводим double в float

        // МЕСТО МАГИИ: отдаем сглаженный бас градиенту и получаем точный цвет!
        _currentColor = _bassGradient.GetColor(bassFactor);

        _radius = 50f + bassFactor * 200f;
    }

    public void Draw()
    {
        Vector2 center = new Vector2(400, 300);
        
        // Рисуем круг с вычисленным цветом
        Raylib.DrawCircleV(center, _radius, _currentColor);
    }
}