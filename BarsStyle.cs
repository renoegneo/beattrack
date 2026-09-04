using Raylib_cs;

public class BarsStyle : IVisualizerStyle
{
    private readonly ValueFollower _bassFollower = new(halfLifeSeconds: 0.08);
    private readonly ValueFollower _midFollower = new(halfLifeSeconds: 0.08);
    private readonly ValueFollower _highFollower = new(halfLifeSeconds: 0.08);

    private float _bassHeight, _midHeight, _highHeight;

    public void Update(AudioFrame audio, float deltaTime)
    {
        _bassHeight = (float)_bassFollower.Update(audio.Bass, deltaTime) * 300f;
        _midHeight = (float)_midFollower.Update(audio.Mid, deltaTime) * 300f;
        _highHeight = (float)_highFollower.Update(audio.High, deltaTime) * 300f;
    }

    public void Draw()
    {
        // DrawRectangle: X, Y (левый верхний угол), ширина, высота, цвет.
        // Рисуем "снизу вверх" — Y считаем как (низ экрана минус высота бара)
        Raylib.DrawRectangle(300, 500 - (int)_bassHeight, 60, (int)_bassHeight, Color.Red);
        Raylib.DrawRectangle(400, 500 - (int)_midHeight, 60, (int)_midHeight, Color.Green);
        Raylib.DrawRectangle(500, 500 - (int)_highHeight, 60, (int)_highHeight, Color.Blue);
    }
}