using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PsycheAsteroidsConvergence
{
    class Program
    {
        private const double G = 6.67430e-11;
        private const int N_MAP = 6; // Sun, Jupiter, Saturn, Uranus, Neptune, Psyche

        private const int SUN = 0;
        private const int JUPITER = 1;
        private const int SATURN = 2;
        private const int URANUS = 3;
        private const int NEPTUNE = 4;
        private const int PSYCHE = 5;

        // Массы планет Солнечной системы из опорной карты (кг)
        private static readonly double[] MapMasses = {
            1.98841e30, // Sun
            1.89819e27, // Jupiter
            5.6834e26,  // Saturn
            8.6813e25,  // Uranus
            1.02413e26, // Neptune
            2.29e19     // 16 Psyche
        };

        // Все 15 баллистических окон из первичного поиска
        private static readonly double[] CandidateWindows = {
            649187.68, 800436.96, 978155.24, 974863.24, 479955.65,
            915858.73, 938833.68, 339794.66, 602193.02, 693319.10,
            695423.41, 695497.33, 356264.48, 800047.64, 474229.16
        };

        private static readonly string OutputDir = @"D:\Zimnicky\Моё\Психа";
        private static readonly string ReportCsv = Path.Combine(OutputDir, "asteroids_convergence.csv");

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Directory.CreateDirectory(OutputDir);

            // Ищем готовый файл solar_map.bin на диске D: или на флешке H:
            string mapFile = Path.Combine(OutputDir, "solar_map.bin");
            if (!File.Exists(mapFile)) mapFile = @"H:\Psyche\solar_map.bin";
            if (!File.Exists(mapFile))
            {
                Console.WriteLine("Ошибка: файл solar_map.bin не найден ни на D:, ни на H:!");
                return;
            }

            double maxYears = 1_000_000.0;
            int recordEverySteps = 50;
            long totalBackSteps = (long)(maxYears * 365.25);
            int historyLen = (int)(totalBackSteps / recordEverySteps) + 1;

            double[,] hx = new double[historyLen, N_MAP];
            double[,] hy = new double[historyLen, N_MAP];
            double[,] hz = new double[historyLen, N_MAP];

            Console.WriteLine($"Загрузка опорной карты из {mapFile}...");
            LoadMapPositions(mapFile, hx, hy, hz, historyLen);
            Console.WriteLine("Карта загружена успешно!\n");

            // Начальные векторы 4 астероидов на эпоху 2026-Nov-10 00:00:00 TDB (переведены из км и км/с в м и м/с)
            string[] astNames = { "1 Ceres", "4 Vesta", "22 Kalliope", "216 Kleopatra" };
            double[] px = { -2.122586313409634e10, 3.309706049616137e11, 5.651620324292548e9, 1.870313948158217e11 };
            double[] py = { 3.938087960299909e11, 1.637687402605810e11, 3.930397178275536e11, -3.462842114831958e11 };
            double[] pz = { 1.638641979494435e10, -4.519032003263395e10, 3.782396167784739e10, 9.091478872444987e10 };

            double[] vx = { -18267.59662070844, -6903.100204106784, -18640.38898069654, 13554.04195763150 };
            double[] vy = { -2354.066970932613, 17003.02048381761, 624.6076015563169, 12532.81317148014 };
            double[] vz = { 3292.005773193306, 337.0545208102911, 4213.890962714387, -558.5905852519719 };

            int astCount = astNames.Length;
            double[] globalMinDistKm = Enumerable.Repeat(double.MaxValue, astCount).ToArray();
            double[] globalMinYear = new double[astCount];

            // Минимальные дистанции до Психеи в окрестности (+- 5 лет) каждого из 15 окон
            double[,] windowMinDistKm = new double[CandidateWindows.Length, astCount];
            for (int w = 0; w < CandidateWindows.Length; w++)
                for (int a = 0; a < astCount; a++)
                    windowMinDistKm[w, a] = double.MaxValue;

            Console.WriteLine("Запуск обратной интеграции 4 астероидов на 1 000 000 лет (шаг -1 сутки)...");
            Stopwatch sw = Stopwatch.StartNew();

            double dt = -86400.0; // -1 сутки
            double halfDt = dt * 0.5;
            double dt6 = dt / 6.0;

            for (long step = 1; step <= totalBackSteps; step++)
            {
                double exactSlice = (double)step / recordEverySteps;
                int h0 = (int)exactSlice;
                if (h0 >= historyLen - 1) break;
                double frac = exactSlice - h0;

                // Положение Психеи в текущий момент
                double psycheX = hx[h0, PSYCHE] + (hx[h0 + 1, PSYCHE] - hx[h0, PSYCHE]) * frac;
                double psycheY = hy[h0, PSYCHE] + (hy[h0 + 1, PSYCHE] - hy[h0, PSYCHE]) * frac;
                double psycheZ = hz[h0, PSYCHE] + (hz[h0 + 1, PSYCHE] - hz[h0, PSYCHE]) * frac;
                double yearsAgo = step / 365.25;

                for (int a = 0; a < astCount; a++)
                {
                    // Шаг RK4 для астероида в поле Солнца, планет-гигантов и Психеи
                    AccelBody(px[a], py[a], pz[a], hx, hy, hz, h0, frac, out double ax1, out double ay1, out double az1);
                    double vx2 = vx[a] + ax1 * halfDt, vy2 = vy[a] + ay1 * halfDt, vz2 = vz[a] + az1 * halfDt;
                    AccelBody(px[a] + vx[a] * halfDt, py[a] + vy[a] * halfDt, pz[a] + vz[a] * halfDt, hx, hy, hz, h0, frac, out double ax2, out double ay2, out double az2);
                    double vx3 = vx[a] + ax2 * halfDt, vy3 = vy[a] + ay2 * halfDt, vz3 = vz[a] + az2 * halfDt;
                    AccelBody(px[a] + vx2 * halfDt, py[a] + vy2 * halfDt, pz[a] + vz2 * halfDt, hx, hy, hz, h0, frac, out double ax3, out double ay3, out double az3);
                    double vx4 = vx[a] + ax3 * dt, vy4 = vy[a] + ay3 * dt, vz4 = vz[a] + az3 * dt;
                    AccelBody(px[a] + vx3 * dt, py[a] + vy3 * dt, pz[a] + vz3 * dt, hx, hy, hz, h0, frac, out double ax4, out double ay4, out double az4);

                    px[a] += (vx[a] + 2.0 * vx2 + 2.0 * vx3 + vx4) * dt6;
                    py[a] += (vy[a] + 2.0 * vy2 + 2.0 * vy3 + vy4) * dt6;
                    pz[a] += (vz[a] + 2.0 * vz2 + 2.0 * vz3 + vz4) * dt6;

                    vx[a] += (ax1 + 2.0 * ax2 + 2.0 * ax3 + ax4) * dt6;
                    vy[a] += (ay1 + 2.0 * ay2 + 2.0 * ay3 + ay4) * dt6;
                    vz[a] += (az1 + 2.0 * az2 + 2.0 * az3 + az4) * dt6;

                    double dx = px[a] - psycheX, dy = py[a] - psycheY, dz = pz[a] - psycheZ;
                    double distKm = Math.Sqrt(dx * dx + dy * dy + dz * dz) * 0.001;

                    // Общий минимум за 1 млн лет
                    if (distKm < globalMinDistKm[a])
                    {
                        globalMinDistKm[a] = distKm;
                        globalMinYear[a] = yearsAgo;
                    }

                    // Проверка попадания в +-5 лет вокруг каждого из 15 окон катастрофы
                    for (int w = 0; w < CandidateWindows.Length; w++)
                    {
                        if (Math.Abs(yearsAgo - CandidateWindows[w]) <= 5.0)
                        {
                            if (distKm < windowMinDistKm[w, a])
                                windowMinDistKm[w, a] = distKm;
                        }
                    }
                }
            }

            sw.Stop();
            Console.WriteLine($"Расчет завершен за {sw.Elapsed.TotalSeconds:F2} сек!\n");

            Console.WriteLine("=== 1. АБСОЛЮТНЫЕ МИНИМУМЫ СБЛИЖЕНИЯ С ПСИХЕЕЙ ЗА 1 000 000 ЛЕТ ===");
            for (int a = 0; a < astCount; a++)
            {
                Console.WriteLine($"  {astNames[a],-15}: Мин. дистанция = {globalMinDistKm[a] / 1e6,6:F3} млн км ({globalMinDistKm[a]:N0} км) | Дата: {globalMinYear[a]:N2} лет назад");
            }

            Console.WriteLine("\n=== 2. СБЛИЖЕНИЯ С ПСИХЕЕЙ ВНУТРИ 15 БАЛЛИСТИЧЕСКИХ ОКОН (+-5 лет) ===");
            Console.WriteLine("Окно (л.н.)  | 1 Ceres (млн км) | 4 Vesta (млн км) | 22 Kalliope (млн км) | 216 Kleopatra (млн км)");
            Console.WriteLine(new string('-', 95));

            var sb = new StringBuilder();
            sb.AppendLine("WindowYearsAgo;Ceres_MlnKm;Vesta_MlnKm;Kalliope_MlnKm;Kleopatra_MlnKm");

            for (int w = 0; w < CandidateWindows.Length; w++)
            {
                Console.WriteLine($"{CandidateWindows[w],12:F2} | {windowMinDistKm[w, 0] / 1e6,16:F2} | {windowMinDistKm[w, 1] / 1e6,16:F2} | {windowMinDistKm[w, 2] / 1e6,20:F2} | {windowMinDistKm[w, 3] / 1e6,22:F2}");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F2};{1:F3};{2:F3};{3:F3};{4:F3}",
                    CandidateWindows[w], windowMinDistKm[w, 0] / 1e6, windowMinDistKm[w, 1] / 1e6, windowMinDistKm[w, 2] / 1e6, windowMinDistKm[w, 3] / 1e6));
            }

            File.WriteAllText(ReportCsv, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"\nПолная таблица сохранена в: {ReportCsv}");
        }

        private static void AccelBody(double fx, double fy, double fz, double[,] hx, double[,] hy, double[,] hz, int h0, double frac, out double ax, out double ay, out double az)
        {
            ax = 0; ay = 0; az = 0;
            for (int j = 0; j < N_MAP; j++) // Учитываем Солнце, 4 гиганта и саму Психею
            {
                double bx = hx[h0, j] + (hx[h0 + 1, j] - hx[h0, j]) * frac;
                double by = hy[h0, j] + (hy[h0 + 1, j] - hy[h0, j]) * frac;
                double bz = hz[h0, j] + (hz[h0 + 1, j] - hz[h0, j]) * frac;

                double dx = bx - fx, dy = by - fy, dz = bz - fz;
                double rSq = dx * dx + dy * dy + dz * dz;
                if (rSq < 1e10) rSq = 1e10;
                double factor = G * MapMasses[j] / (rSq * Math.Sqrt(rSq));

                ax += dx * factor;
                ay += dy * factor;
                az += dz * factor;
            }
        }
        private static void LoadMapPositions(string path, double[,] hx, double[,] hy, double[,] hz, int hLen)
        {
            using (var br = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                for (int r = 0; r < hLen; r++)
                {
                    for (int i = 0; i < N_MAP; i++)
                    {
                        hx[r, i] = br.ReadDouble();
                        hy[r, i] = br.ReadDouble();
                        hz[r, i] = br.ReadDouble();
                        br.ReadDouble(); br.ReadDouble(); br.ReadDouble(); // Пропускаем скорости
                    }
                }
            }
        }
    }
}