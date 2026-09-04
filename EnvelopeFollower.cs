/// <summary>
/// Asymmetric envelope for visual motion. Attack and release are independent:
/// hits arrive nearly immediately, while falling values have a short decay.
/// </summary>
public sealed class EnvelopeFollower
{
    private readonly double _attackSeconds;
    private readonly double _releaseSeconds;
    private readonly double _snapToZeroThreshold;
    private readonly bool _snapToZero;
    private double _value;

    public EnvelopeFollower(
        double attackMilliseconds,
        double releaseMilliseconds,
        double snapToZeroThreshold,
        bool snapToZero)
    {
        _attackSeconds = Math.Max(attackMilliseconds / 1_000.0, 0.0001);
        _releaseSeconds = Math.Max(releaseMilliseconds / 1_000.0, 0.0001);
        _snapToZeroThreshold = Math.Clamp(snapToZeroThreshold, 0, 1);
        _snapToZero = snapToZero;
    }

    public double Update(double target, double deltaSeconds)
    {
        target = Math.Clamp(target, 0, 1);
        if (_snapToZero && target <= _snapToZeroThreshold)
            return _value = 0;

        double timeConstant = target > _value ? _attackSeconds : _releaseSeconds;
        double factor = Math.Exp(-deltaSeconds / timeConstant);
        return _value = _value * factor + target * (1 - factor);
    }
}
