/// <summary>
/// Future UI-facing controls for audio analysis. Defaults intentionally use a
/// fixed dBFS range: no automatic normalization changes a steady sound level.
/// </summary>
public sealed class AudioAnalysisSettings
{
    // FFT / input
    public int FftSize { get; init; } = 2048;
    // New analysis results per 256 samples: ~5.3 ms at 48 kHz.
    public int AnalysisHopSize { get; init; } = 256;
    public double MinimumFrequencyHz { get; init; } = 20;
    public double MaximumFrequencyHz { get; init; } = 20_000;

    // Three broad ranges used by the current styles.
    public FrequencyRange Bass { get; init; } = new(20, 250);
    public FrequencyRange Mid { get; init; } = new(250, 4_000);
    public FrequencyRange High { get; init; } = new(4_000, 20_000);

    // Level mapping. A signal at or below FloorDbfs is 0; at or above
    // CeilingDbfs it is 1. These are absolute, stable reference points.
    // Desktop loopback sources are commonly mastered well below 0 dBFS.
    // This covers normal music without any adaptive gain.
    public double FloorDbfs { get; init; } = -72;
    public double CeilingDbfs { get; init; } = -3;
    public double InputGainDb { get; init; } = 0;
    // Power curve after dB mapping. > 1 suppresses small values/noise and
    // makes transients disproportionately stronger.
    public double ResponseCurve { get; init; } = 1.8;

    // Shared visual envelope, applied once for every frequency band.
    public double AttackMilliseconds { get; init; } = 2;
    public double ReleaseMilliseconds { get; init; } = 40;
    public double SilenceThreshold { get; init; } = 0.01;
    public bool SnapToZeroInSilence { get; init; } = true;

    // Development-only meter. It provides a factual calibration reading in
    // the console until the proper UI meter is added.
    public bool LogMeasuredLevels { get; init; } = true;

    // Kept explicit for the future UI. This version deliberately does not
    // implement adaptive normalization; it is false by default on purpose.
    public bool AdaptiveNormalization { get; init; } = false;
}

public readonly record struct FrequencyRange(double StartHz, double EndHz)
{
    public bool Contains(double frequencyHz) => frequencyHz >= StartHz && frequencyHz < EndHz;
}
