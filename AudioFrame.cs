public class AudioFrame
{
    public double[] Spectrum = Array.Empty<double>();
    public double[] SpectrumDbfs = Array.Empty<double>();

    // Границы полос в Гц — та же ссылка каждый кадр, ничего не копируется
    public FrequencyRange[] BandRanges = Array.Empty<FrequencyRange>();

    // Замена старым Bass/Mid/High: честное среднее по всем полосам,
    // чьё начало попадает в указанный диапазон частот. Старые стили
    // просто просят "дай мне 20-250 Гц" вместо готового поля Bass
    public double AverageInRange(double minHz, double maxHz)
    {
        double sum = 0;
        int count = 0;

        for (int i = 0; i < BandRanges.Length; i++)
        {
            if (BandRanges[i].StartHz >= minHz && BandRanges[i].StartHz < maxHz)
            {
                sum += Spectrum[i];
                count++;
            }
        }

        return count > 0 ? sum / count : 0;
    }
}