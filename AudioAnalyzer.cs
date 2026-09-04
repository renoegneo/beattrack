using NAudio.Dsp;
using NAudio.Wave;

/// <summary>
/// Produces calibrated dBFS and the single, attack/release-filtered stream
/// consumed by every visual style.
/// </summary>
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
    private readonly EnvelopeFollower _bassEnvelope;
    private readonly EnvelopeFollower _midEnvelope;
    private readonly EnvelopeFollower _highEnvelope;

    private int _writeIndex;
    private int _collectedSamples;
    private int _samplesSinceAnalysis;
    private long _lastLogTimestamp;

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
        _bassEnvelope = CreateEnvelope();
        _midEnvelope = CreateEnvelope();
        _highEnvelope = CreateEnvelope();
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

        // writeIndex now points at the oldest sample, so this copy is a
        // chronological, overlapping FFT window. At 48 kHz / hop 256 we
        // produce a new envelope point every ~5.3 ms rather than every ~43 ms.
        for (int i = 0; i < _analysisSamples.Length; i++)
            _analysisSamples[i] = _sampleRing[(_writeIndex + i) % _sampleRing.Length];

        Analyze(sampleRate);
        _samplesSinceAnalysis = 0;
    }

    private void Analyze(int sampleRate)
    {
        double inputPower = 0;
        for (int i = 0; i < _analysisSamples.Length; i++)
        {
            inputPower += _analysisSamples[i] * _analysisSamples[i];
            _fft[i].X = (float)(_analysisSamples[i] * _window[i]);
            _fft[i].Y = 0;
        }
        inputPower /= _analysisSamples.Length;

        FastFourierTransform.FFT(true, System.Numerics.BitOperations.TrailingZeroCount(_analysisSamples.Length), _fft);

        double bassPower = 0;
        double midPower = 0;
        double highPower = 0;
        double amplitudeScale = 2.0 / (_windowCoherentGain * Math.Sqrt(2));

        for (int bin = 1; bin < _analysisSamples.Length / 2; bin++)
        {
            double frequency = bin * (double)sampleRate / _analysisSamples.Length;
            if (frequency < _settings.MinimumFrequencyHz || frequency > _settings.MaximumFrequencyHz)
                continue;

            double real = _fft[bin].X;
            double imaginary = _fft[bin].Y;
            double rmsAmplitude = Math.Sqrt(real * real + imaginary * imaginary) * amplitudeScale;
            double power = rmsAmplitude * rmsAmplitude;

            if (_settings.Bass.Contains(frequency)) bassPower += power;
            else if (_settings.Mid.Contains(frequency)) midPower += power;
            else if (_settings.High.Contains(frequency)) highPower += power;
        }

        double bassDbfs = PowerToDbfs(bassPower) + _settings.InputGainDb;
        double midDbfs = PowerToDbfs(midPower) + _settings.InputGainDb;
        double highDbfs = PowerToDbfs(highPower) + _settings.InputGainDb;
        double deltaSeconds = _settings.AnalysisHopSize / (double)sampleRate;

        _state.Update(
            _bassEnvelope.Update(MapDbfsToUnit(bassDbfs), deltaSeconds),
            _midEnvelope.Update(MapDbfsToUnit(midDbfs), deltaSeconds),
            _highEnvelope.Update(MapDbfsToUnit(highDbfs), deltaSeconds),
            bassDbfs,
            midDbfs,
            highDbfs);

        if (_settings.LogMeasuredLevels && Environment.TickCount64 - _lastLogTimestamp >= 1_000)
        {
            _lastLogTimestamp = Environment.TickCount64;
            Console.WriteLine($"[Audio] IN {PowerToDbfs(inputPower):F1} dBFS | B {bassDbfs:F1} | M {midDbfs:F1} | H {highDbfs:F1}");
        }
    }

    private double PowerToDbfs(double power) => 10 * Math.Log10(Math.Max(power, DbEpsilon));

    private double MapDbfsToUnit(double dbfs)
    {
        double unit = Math.Clamp(
            (dbfs - _settings.FloorDbfs) / (_settings.CeilingDbfs - _settings.FloorDbfs),
            0,
            1);
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
