// Файл: AudioState.cs

// Это класс-"почтовый ящик": звуковой поток кладёт туда свежие числа,
// поток графики их оттуда забирает. Ничего больше он не делает.
public class AudioState
{
    // Приватные поля — реальные значения. Снаружи их напрямую не трогают,
    // только через методы ниже (та же инкапсуляция, что была в BandSmoother)
    private double _bass;
    private double _mid;
    private double _high;
    private double _bassDbfs;
    private double _midDbfs;
    private double _highDbfs;

    // Объект-"замок" специально для lock. Он не хранит никаких данных сам
    // по себе — это просто объект, вокруг которого договариваемся "дверь
    // закрыта, пока кто-то внутри". Общепринятая практика — заводить
    // отдельный private object именно для этой цели, ничего больше с ним
    // не делают.
    private readonly object _lock = new object();

    // Вызывается ИЗ ЗВУКОВОГО потока — кладёт свежие значения
    public void Update(double bass, double mid, double high, double bassDbfs, double midDbfs, double highDbfs)
    {
        lock (_lock)
        {
            _bass = bass;
            _mid = mid;
            _high = high;
            _bassDbfs = bassDbfs;
            _midDbfs = midDbfs;
            _highDbfs = highDbfs;
        } // дверь автоматически "открывается" здесь — lock сам следит за этим
    }

    // Вызывается ИЗ ПОТОКА ГРАФИКИ — забирает текущие значения
    // Возвращаем сразу три числа через tuple (кортеж — способ вернуть
    // несколько значений из одного метода без отдельного класса под это)
    public (double Bass, double Mid, double High, double BassDbfs, double MidDbfs, double HighDbfs) Read()
    {
        lock (_lock)
        {
            return (_bass, _mid, _high, _bassDbfs, _midDbfs, _highDbfs);
        }
    }
}
