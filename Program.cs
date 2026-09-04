using NAudio.Wave;
using Raylib_cs;

var analysisSettings = new AudioAnalysisSettings
{
    // These become UI sliders later. The defaults keep levels absolute:
    // no adaptive maximum, only a fixed dBFS floor/ceiling.
    FloorDbfs = -72,
    CeilingDbfs = -3,
    InputGainDb = 14,
    ResponseCurve = 2.5,
    AttackMilliseconds = 10,
    ReleaseMilliseconds = 30,
    SilenceThreshold = 0.01,
    SnapToZeroInSilence = true,
    LogMeasuredLevels = false
};

var audioState = new AudioState();
var analyzer = new AudioAnalyzer(analysisSettings, audioState);

using var capture = new WasapiLoopbackCapture();
// Resolve WAVE_FORMAT_EXTENSIBLE once. Its tag alone does not say whether the
// 32-bit payload is PCM or IEEE float; AsStandardWaveFormat reads SubFormat.
WaveFormat captureFormat = capture.WaveFormat.AsStandardWaveFormat();
Console.WriteLine($"[Audio] Capture format: {capture.WaveFormat} -> {captureFormat}");
capture.DataAvailable += OnDataAvailable;
capture.StartRecording();

IVisualizerStyle[] styles =
[
    new CircleStyle(),
    new BarsStyle(),
    new TriBandStyle(),
    new HSVReactiveStyle(),
    new CustomGradientStyle(),
    new NebulaStyle(),
    new DubstepGutterStyle(),
    new FullWidthBarcodeStyle()
];
int currentStyleIndex = 7;

Raylib.InitWindow(1000, 800, "Аудио-визуализатор");
Raylib.SetTargetFPS(90);

unsafe
{
    bool embedded = DesktopEmbedder.TryEmbed((IntPtr)Raylib.GetWindowHandle());
    Console.WriteLine(embedded
        ? "Успех: окно встроено за иконки рабочего стола."
        : "Не удалось встроить — работаем в обычном окне.");
}

while (!Raylib.WindowShouldClose())
{
    var level = audioState.Read();
    float deltaTime = Raylib.GetFrameTime();

    if (Raylib.IsKeyPressed(KeyboardKey.Space))
        currentStyleIndex = (currentStyleIndex + 1) % styles.Length;

    var frame = new AudioFrame
    {
        Bass = level.Bass,
        Mid = level.Mid,
        High = level.High,
        BassDbfs = level.BassDbfs,
        MidDbfs = level.MidDbfs,
        HighDbfs = level.HighDbfs
    };

    styles[currentStyleIndex].Update(frame, deltaTime);

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.Black);
    styles[currentStyleIndex].Draw();
    Raylib.DrawFPS(10, 10);
    Raylib.EndDrawing();
}

Raylib.CloseWindow();
capture.StopRecording();

void OnDataAvailable(object? sender, WaveInEventArgs e) =>
    analyzer.PushAudio(e.Buffer, e.BytesRecorded, captureFormat);
