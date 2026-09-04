using System.Numerics; // Нужен для использования Vector2 (координаты X, Y)
using Raylib_cs;

public class TriBandStyle : IVisualizerStyle
{
    // =========================================================================
    // 1. ПОЛЯ КЛАССА (Состояние стиля)
    // Хранят данные, которые должны жить между кадрами
    // =========================================================================

    // Создаем три независимых сглаживателя для каждой частоты
    private readonly ValueFollower _bassFollower = new(halfLifeSeconds: 0.05);
    private readonly ValueFollower _midFollower  = new(halfLifeSeconds: 0.08);
    private readonly ValueFollower _highFollower = new(halfLifeSeconds: 0.04);

    // Внутренние переменные для хранения посчитанных размеров и углов
    private float _bassRadius = 50f;
    private float _squareRotation = 0f;
    private float _highFlashAlpha = 0f;

    // =========================================================================
    // 2. МЕТОД UPDATE (Математика и логика кадра)
    // Входные данные: audio (структура со звуками) и deltaTime (время кадра)
    // =========================================================================
    public void Update(AudioFrame audio, float deltaTime)
    {
        // Шаг А: Сглаживаем сырой звук от 0.0 до 1.0 через сглаживатели
        double smoothBass = _bassFollower.Update(audio.Bass, deltaTime);
        double smoothMid  = _midFollower.Update(audio.Mid, deltaTime);
        double smoothHigh = _highFollower.Update(audio.High, deltaTime);

        // Шаг Б: Превращаем сглаженный звук в параметры графики

        // 1. Радиус баса: базовая величина 50px + прибавка до 200px от баса
        _bassRadius = 50f + (float)smoothBass * 200f;

        // 2. Вращение квадрата: прибавляем угол каждый кадр. 
        // Чем громче СЧ, тем быстрее вращается
        float rotationSpeed = 90f + (float)smoothMid * 360f; // градусов в секунду
        _squareRotation += rotationSpeed * deltaTime;

        // 3. Прозрачность вспышки от ВЧ (сохраняем значение 0.0...1.0)
        _highFlashAlpha = (float)smoothHigh;
    }

    // =========================================================================
    // 3. МЕТОД DRAW (Чистая отрисовка)
    // Внимание: В этом методе НЕ должно быть сложной математики! Только Raylib.Draw...
    // =========================================================================
    public void Draw()
    {
        // Координаты центра экрана (ширина 800 / 2, высота 600 / 2)
        Vector2 center = new Vector2(400, 300);

        // --- 1. БАС: Пульсирующий красный круг в центре ---
        Raylib.DrawCircleV(center, _bassRadius, Color.Red);

        // --- 2. МИДЫ: Вращающийся золотой квадрат (Poly с 4 гранями) ---
        // DrawPolyLinesEx(центр, кол-во граней, радиус, угол_поворота, толщина_линии, цвет)
        Raylib.DrawPolyLinesEx(center, 4, _bassRadius + 30f, _squareRotation, 5f, Color.Gold);

        // --- 3. ТРЕБЛЫ: Вспыхивающая внешняя рамка ---
        // Превращаем float (0.0 ... 1.0) в byte (0 ... 255) для прозрачности (Alpha)
        byte alphaByte = (byte)(_highFlashAlpha * 255f);
        Color flashColor = new Color((byte)255, (byte)255, (byte)255, alphaByte);

        // Рисуем рамку с толщиной 10 пикселей по краям экрана
        Raylib.DrawRectangleLinesEx(new Rectangle(10, 10, 780, 580), 10f, flashColor);
    }
}