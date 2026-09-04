using System.Numerics;
using Raylib_cs;

public class HSVReactiveStyle : IVisualizerStyle
{
    private readonly ValueFollower _bassFollower = new(halfLifeSeconds: 0.05);
    private readonly ValueFollower _midFollower  = new(halfLifeSeconds: 0.08);
    private readonly ValueFollower _highFollower = new(halfLifeSeconds: 0.04);

    // Храним текущий угол цветового круга (от 0 до 360)
    private float _currentHue = 0f;

    // Готовые цвета для отрисовки
    private Color _ringColor;
    private Color _coreColor;
    private float _radius;

    public void Update(AudioFrame audio, float deltaTime)
    {
        double smoothBass = _bassFollower.Update(audio.Bass, deltaTime);
        double smoothMid  = _midFollower.Update(audio.Mid, deltaTime);
        double smoothHigh = _highFollower.Update(audio.High, deltaTime);

        // --- 1. РАСЧЕТ ЦВЕТА (HSV) ---

        // Плавно вращаем цветовой круг с базовой скоростью + ускорение от высоких частот
        float hueSpeed = 30f + (float)smoothHigh * 300f; // градусов в секунду
        _currentHue += hueSpeed * deltaTime;

        // Оператор % (остаток от деления) закольцовывает число в пределы 0...360
        // Если _currentHue станет 365, получится 5
        _currentHue %= 360f;

        // Цвет внешнего кольца: 
        // Hue = наш текущий угол
        // Saturation = 1.0 (максимально сочный)
        // Value = от 0.3 (в тишине) до 1.0 (на громком басу)
        float ringValue = 0.3f + (float)smoothBass * 0.7f;
        _ringColor = Raylib.ColorFromHSV(_currentHue, 1.0f, ringValue);

        // Цвет внутреннего ядра:
        // Сдвигаем Hue на 180 градусов вперед — получаем противоположный (контрастный) цвет!
        float oppositeHue = (_currentHue + 180f) % 360f;
        
        // Насыщенность падает при громких средних частотах (ядро выцветает до белого)
        float coreSaturation = 1.0f - (float)smoothMid;
        _coreColor = Raylib.ColorFromHSV(oppositeHue, coreSaturation, 1.0f);

        // --- 2. РАСЧЕТ ГЕОМЕТРИИ ---
        _radius = 80f + (float)smoothBass * 150f;
    }

    public void Draw()
    {
        Vector2 center = new Vector2(400, 300);

        // Рисуем внутреннее ядро контрастного цвета
        Raylib.DrawCircleV(center, _radius * 0.5f, _coreColor);

        // Рисуем внешнее кольцо основного HSV-цвета
        Raylib.DrawRing(
            center: center, 
            innerRadius: _radius * 0.8f, 
            outerRadius: _radius, 
            startAngle: 0f, 
            endAngle: 360f, 
            segments: 60, 
            color: _ringColor
        );
    }
}