// Просто "коробка" с тремя готовыми числами (уже сглаженными),
// без какой-либо логики внутри. Стили будут получать именно её,
// не зная вообще ничего про FFT/NAudio/сглаживание.
public class AudioFrame
{
    // Fixed-range, presentation-ready values in 0..1.
    public double Bass;
    public double Mid;
    public double High;

    // Calibrated source measurements. Future UI/debug overlays can show the
    // actual dBFS level without reverse-engineering a visual style.
    public double BassDbfs;
    public double MidDbfs;
    public double HighDbfs;
}
