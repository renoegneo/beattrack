// Файл: ValueFollower.cs

// Единственная задача этого класса — плавно "подъезжать" к целевому
// числу, вместо мгновенного скачка. Ничего не знает ни про звук,
// ни про частоты — просто общий инструмент сглаживания движения.
public class ValueFollower
{
    private double _current; // то самое число, которое реально рисуем на экране

    // За сколько секунд "текущее" значение проходит половину пути
    // до целевого. Меньше число — быстрее реакция (более резкая анимация),
    // больше — плавнее, но более "инертно"
    private readonly double _halfLifeSeconds;

    public ValueFollower(double halfLifeSeconds = 0.08)
    {
        _halfLifeSeconds = halfLifeSeconds;
    }

    // Вызывается КАЖДЫЙ КАДР, с реальным временем, прошедшим с прошлого кадра
    public double Update(double target, double deltaTime)
    {
        double factor = Math.Pow(0.5, deltaTime / _halfLifeSeconds);

        // Знакомая формула — та же EMA (экспоненциальное скользящее среднее),
        // что и в BandSmoother, только тут применяется не к "потолку",
        // а прямо к отображаемому значению
        _current = _current * factor + target * (1 - factor);

        return _current;
    }
}