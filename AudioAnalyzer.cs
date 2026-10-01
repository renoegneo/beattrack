using NAudio.Dsp;
using NAudio.Wave;

public sealed class AudioAnalyzer
{
    private const double DbEpsilon = 1e-12;

    private readonly AudioAnalysisSettings _settings;
    private readonly AudioState _state;
    private readonly float[] _sampleRing;
    private readonly float[] _analysisSamples;
    private readonly Complex[] _fft;
    private readonly double[] _window;
    private readonly double _windowCoherentGain;

    private FrequencyRange[] _bands;
    private EnvelopeFollower[] _bandEnvelopes;
    private double[] _bandPower;

    // Кэш: какому бину какая полоса соответствует. Пересчитывается только
    // если поменялась частота дискретизации (на практике — один раз за сессию)
    private int _cachedSampleRate = -1;
    private int[] _binToBand = Array.Empty<int>();
    private int[] _bandBinCounts = Array.Empty<int>();

    private int _writeIndex;
    private int _collectedSamples;
    private int _samplesSinceAnalysis;
    private long _lastLogTimestamp;

    public FrequencyRange[] Bands => _bands; // наружу — для AudioFrame.BandRanges

    public AudioAnalyzer(AudioAnalysisSettings settings, AudioState state)
    {
        if (settings.FftSize < 64 || (settings.FftSize & (settings.FftSize - 1)) != 0)
            throw new ArgumentException("FFT size must be a power of two and at least 64.", nameof(settings));
        if (settings.AnalysisHopSize is < 1 or > 2048 || settings.AnalysisHopSize > settings.FftSize)
            throw new ArgumentException("Analysis hop must be positive and no larger than the FFT size.", nameof(settings));
        if (settings.FloorDbfs >= settings.CeilingDbfs)
            throw new ArgumentException("The dBFS floor must be lower than the ceiling.", nameof(settings));
        if (settings.ResponseCurve <= 0)
            throw new ArgumentException("Response curve must be positive.", nameof(settings));
        if (settings.SpectrumBandCount <= 0)
            throw new ArgumentException("Spectrum band count must be positive.", nameof(settings));

        _settings = settings;
        _state = state;
        _sampleRing = new float[settings.FftSize];
        _analysisSamples = new float[settings.FftSize];
        _fft = new Complex[settings.FftSize];
        _window = new double[settings.FftSize];

        double windowSum = 0;
        for (int i = 0; i < _window.Length; i++)
        {
            _window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (_window.Length - 1)));
            windowSum += _window[i];
        }
        _windowCoherentGain = windowSum / _window.Length;

        _bands = Array.Empty<FrequencyRange>();
        _bandEnvelopes = Array.Empty<EnvelopeFollower>();
        _bandPower = Array.Empty<double>();
    }

    public void PushAudio(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        int bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample is not (2 or 3 or 4) || format.Channels <= 0 || format.BlockAlign <= 0)
            return;

        int completeBytes = bytesRecorded - bytesRecorded % format.BlockAlign;
        for (int frameOffset = 0; frameOffset < completeBytes; frameOffset += format.BlockAlign)
        {
            double mono = 0;
            for (int channel = 0; channel < format.Channels; channel++)
                mono += ReadSample(buffer, frameOffset + channel * bytesPerSample, format);

            PushMonoSample((float)(mono / format.Channels), format.SampleRate);
        }
    }

    private EnvelopeFollower CreateEnvelope() => new(
        _settings.AttackMilliseconds,
        _settings.ReleaseMilliseconds,
        _settings.SilenceThreshold,
        _settings.SnapToZeroInSilence);

    private void PushMonoSample(float sample, int sampleRate)
    {
        _sampleRing[_writeIndex] = sample;
        _writeIndex = (_writeIndex + 1) % _sampleRing.Length;
        _collectedSamples = Math.Min(_collectedSamples + 1, _sampleRing.Length);
        _samplesSinceAnalysis++;

        if (_collectedSamples < _sampleRing.Length || _samplesSinceAnalysis < _settings.AnalysisHopSize)
            return;

        for (int i = 0; i < _analysisSamples.Length; i++)
            _analysisSamples[i] = _sampleRing[(_writeIndex + i) % _sampleRing.Length];

        Analyze(sampleRate);
        _samplesSinceAnalysis = 0;
    }

    // Строит таблицу "бин -> индекс полосы" один раз и переиспользует её,
    // пока частота дискретизации не изменится. Без этого пришлось бы
    // пересчитывать Contains() для каждого бина 189 раз в секунду впустую —
    // граница полос не меняется от кадра к кадру, только реальные амплитуды
    private void EnsureBinMapping(int sampleRate)
    {
        if (sampleRate == _cachedSampleRate)
            return;

        _cachedSampleRate = sampleRate;
        int totalBins = _analysisSamples.Length / 2;
        double hzPerBin = sampleRate / (double)_analysisSamples.Length;

        int minBin = Math.Max(1, (int)Math.Floor(_settings.MinimumFrequencyHz / hzPerBin));
        int maxBin = Math.Min(totalBins - 1, (int)Math.Ceiling(_settings.MaximumFrequencyHz / hzPerBin));
        int usableBins = maxBin - minBin + 1;

        int requestedBandCount = _settings.SpectrumBandCount;
        int actualBandCount = Math.Min(requestedBandCount, usableBins);

        if (actualBandCount != requestedBandCount)
            Console.WriteLine($"[Audio] Запрошено {requestedBandCount} полос, физически различимо только {usableBins} — используем {actualBandCount}.");

        _bands = new FrequencyRange[actualBandCount];
        _binToBand = new int[totalBins];
        Array.Fill(_binToBand, -1);
        _bandBinCounts = new int[actualBandCount];

        double ratio = Math.Pow(_settings.MaximumFrequencyHz / _settings.MinimumFrequencyHz, 1.0 / actualBandCount);
        int previousEndBin = minBin;
        double edgeHz = _settings.MinimumFrequencyHz;

        for (int i = 0; i < actualBandCount; i++)
        {
            edgeHz *= ratio;
            int desiredEndBin = (int)Math.Round(edgeHz / hzPerBin);

            // Вот эта строчка и есть весь фикс: "минимум предыдущий бин + 1" —
            // не даёт полосе остаться пустой, даже если формула "хочет"
            // уместить несколько полос в один и тот же бин
            int endBin = Math.Max(previousEndBin + 1, desiredEndBin);
            if (i == actualBandCount - 1)
                endBin = maxBin + 1; // последняя полоса подчищает всё, что осталось
            endBin = Math.Min(endBin, maxBin + 1);

            for (int bin = previousEndBin; bin < endBin && bin < totalBins; bin++)
                _binToBand[bin] = i;

            _bandBinCounts[i] = endBin - previousEndBin;
            _bands[i] = new FrequencyRange(previousEndBin * hzPerBin, endBin * hzPerBin);
            previousEndBin = endBin;
        }

        _bandEnvelopes = new EnvelopeFollower[actualBandCount];
        for (int i = 0; i < _bandEnvelopes.Length; i++)
            _bandEnvelopes[i] = CreateEnvelope();
        _bandPower = new double[actualBandCount];

        if (_settings.LogMeasuredLevels)
        {
            Console.WriteLine($"[Audio] Разрешение: {hzPerBin:F2} Гц/бин, реальных полос: {actualBandCount}");
            for (int i = 0; i < _bands.Length; i++)
                Console.WriteLine($"[Audio] Полоса {i}: {_bands[i].StartHz:F0}-{_bands[i].EndHz:F0} Гц, бинов: {_bandBinCounts[i]}");
        }
    }

    private int FindBandIndex(double frequency)
    {
        for (int i = 0; i < _bands.Length; i++)
        {
            if (_bands[i].Contains(frequency))
                return i;
        }
        return -1; // частота вне рабочего диапазона (ниже Min или выше Max)
    }

    private void Analyze(int sampleRate)
    {
        EnsureBinMapping(sampleRate);

        for (int i = 0; i < _analysisSamples.Length; i++)
        {
            _fft[i].X = (float)(_analysisSamples[i] * _window[i]);
            _fft[i].Y = 0;
        }

        FastFourierTransform.FFT(true, System.Numerics.BitOperations.TrailingZeroCount(_analysisSamples.Length), _fft);

        Array.Clear(_bandPower);
        double amplitudeScale = 2.0 / (_windowCoherentGain * Math.Sqrt(2));

        for (int bin = 1; bin < _binToBand.Length; bin++)
        {
            int band = _binToBand[bin];
            if (band < 0)
                continue;

            double real = _fft[bin].X;
            double imaginary = _fft[bin].Y;
            double rmsAmplitude = Math.Sqrt(real * real + imaginary * imaginary) * amplitudeScale;
            _bandPower[band] += rmsAmplitude * rmsAmplitude;
        }

        var spectrumUnit = new double[_bands.Length];
        var spectrumDbfs = new double[_bands.Length];
        double deltaSeconds = _settings.AnalysisHopSize / (double)sampleRate;

        for (int band = 0; band < _bands.Length; band++)
        {
            int binCount = _bandBinCounts[band];
            double averagePower = binCount > 0 ? _bandPower[band] / binCount : 0;

            double dbfs = PowerToDbfs(averagePower) + _settings.InputGainDb;

            // НОВОЕ: компенсация естественного спада энергии музыки к высоким
            // частотам. Вставляется здесь — ПОСЛЕ InputGainDb (общий уровень
            // для всех полос), но ДО того, как результат уйдёт в spectrumDbfs
            // и в EnvelopeFollower — то есть влияет на итоговую картинку,
            // но не искажает "сырые" dBFS раньше времени
            double centerHz = (_bands[band].StartHz + _bands[band].EndHz) / 2.0;
            double octavesAboveReference = Math.Log2(centerHz / _settings.TiltReferenceHz);
            dbfs += octavesAboveReference * _settings.SpectralTiltDbPerOctave;

            spectrumDbfs[band] = dbfs;
            spectrumUnit[band] = _bandEnvelopes[band].Update(MapDbfsToUnit(dbfs), deltaSeconds);
        }

        _state.Update(spectrumUnit, spectrumDbfs);

        if (_settings.LogMeasuredLevels && Environment.TickCount64 - _lastLogTimestamp >= 1_000)
        {
            _lastLogTimestamp = Environment.TickCount64;
            Console.WriteLine($"[Audio] Мин dBFS: {spectrumDbfs.Min():F1} | Макс dBFS: {spectrumDbfs.Max():F1}");
        }
    }

    private double PowerToDbfs(double power) => 10 * Math.Log10(Math.Max(power, DbEpsilon));

    private double MapDbfsToUnit(double dbfs)
    {
        double unit = Math.Clamp((dbfs - _settings.FloorDbfs) / (_settings.CeilingDbfs - _settings.FloorDbfs), 0, 1);
        return Math.Pow(unit, _settings.ResponseCurve);
    }

    private static double ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            return BitConverter.ToSingle(buffer, offset);

        return format.BitsPerSample switch
        {
            16 => BitConverter.ToInt16(buffer, offset) / 32768.0,
            24 => ReadInt24(buffer, offset) / 8_388_608.0,
            32 => BitConverter.ToInt32(buffer, offset) / 2_147_483_648.0,
            _ => 0
        };
    }

    private static int ReadInt24(byte[] buffer, int offset)
    {
        int value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        return (value & 0x00800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }
}