public class AudioState
{
    private double[] _spectrumUnit = Array.Empty<double>();
    private double[] _spectrumDbfs = Array.Empty<double>();
    private readonly object _lock = new object();

    // ВАЖНО: вызывающий код обязан передавать НОВЫЙ массив каждый раз,
    // а не мутировать старый на месте — иначе весь смысл паттерна ломается
    public void Update(double[] spectrumUnit, double[] spectrumDbfs)
    {
        lock (_lock)
        {
            _spectrumUnit = spectrumUnit;
            _spectrumDbfs = spectrumDbfs;
        }
    }

    public (double[] Unit, double[] Dbfs) Read()
    {
        lock (_lock)
        {
            return (_spectrumUnit, _spectrumDbfs);
        }
    }
}