public sealed class AudioAnalysisSettings
{
    public int FftSize { get; init; } = 4096; // увеличили — больше решает по частоте
    public int AnalysisHopSize { get; init; } = 256; // частота обновления НЕ зависит от FftSize
    public double MinimumFrequencyHz { get; init; } = 20;
    public double MaximumFrequencyHz { get; init; } = 20_000;

    // Вместо трёх именованных полос — одно число: сколько полос всего
    public int SpectrumBandCount { get; init; } = 32;

    public double FloorDbfs { get; init; } = -72;
    public double CeilingDbfs { get; init; } = -3;
    public double InputGainDb { get; init; } = 0;
    public double ResponseCurve { get; init; } = 1.8;


    public double TiltReferenceHz { get; init; } = 1000; // 1 кГц — общепринятая опорная точка в аудио
    public double SpectralTiltDbPerOctave { get; init; } = 3.0; // сколько доп. дБ добавлять на каждую октаву выше опорной

    public double AttackMilliseconds { get; init; } = 2;
    public double ReleaseMilliseconds { get; init; } = 40;
    public double SilenceThreshold { get; init; } = 0.01;
    public bool SnapToZeroInSilence { get; init; } = true;

    public bool LogMeasuredLevels { get; init; } = true;

    // Строит N полос, растущих в геометрической прогрессии от minHz до maxHz.
    // static — значит вызывается как AudioAnalysisSettings.CreateLogSpacedBands(...),
    // без создания объекта, потому что метод не использует данные конкретного
    // экземпляра настроек, только то, что ему явно передали
    public static FrequencyRange[] CreateLogSpacedBands(double minHz, double maxHz, int count)
    {
        var bands = new FrequencyRange[count];
        double ratio = Math.Pow(maxHz / minHz, 1.0 / count);
        double edge = minHz;

        for (int i = 0; i < count; i++)
        {
            double nextEdge = edge * ratio;
            bands[i] = new FrequencyRange(edge, nextEdge);
            edge = nextEdge;
        }

        return bands;
    }
}

public readonly record struct FrequencyRange(double StartHz, double EndHz)
{
    public bool Contains(double frequencyHz) => frequencyHz >= StartHz && frequencyHz < EndHz;
}