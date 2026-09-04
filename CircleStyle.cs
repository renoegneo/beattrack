using Raylib_cs;

// ": IVisualizerStyle" значит "этот класс ОБЯЗУЕТСЯ выполнить контракт
// интерфейса IVisualizerStyle". Если забудешь реализовать хоть один
// метод из интерфейса — компилятор сразу укажет на ошибку, не даст
// собрать проект. Это защита от "забыл дописать логику".
public class CircleStyle : IVisualizerStyle
{
    // Свои собственные "следопыты" для плавности — они принадлежат
    // именно этому стилю, никто другой их не видит и не трогает
    private readonly ValueFollower _bassFollower = new(halfLifeSeconds: 0.08);

    // Тут храним текущий (уже сглаженный) радиус между Update и Draw
    private float _radius = 50f;

    public void Update(AudioFrame audio, float deltaTime)
    {
        double smoothBass = _bassFollower.Update(audio.Bass, deltaTime);
        _radius = 50f + (float)smoothBass * 200f;
    }

    public void Draw()
    {
        Raylib.DrawCircle(400, 300, _radius, Color.SkyBlue);
    }
}