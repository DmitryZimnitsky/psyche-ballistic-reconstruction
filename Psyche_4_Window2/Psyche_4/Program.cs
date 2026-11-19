using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PsycheTurboTuner3D
{
    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public class PreciseShot
    {
        public string WindowName;
        public double YearsAgo;
        public string Target;
        public double SpeedKmS;
        public double AzimuthDeg;
        public double ElevationDeg;
        public double FlightYears;
        public double MinDistKm;
        public double OrigX, OrigY, OrigZ;
    }

    class Program
    {
        private const double G = 6.67430e-11;
        private const int N = 6;

        private const int SUN = 0;
        private const int JUPITER = 1;
        private const int SATURN = 2;
        private const int URANUS = 3;
        private const int NEPTUNE = 4;
        private const int PSYCHE = 5;

        private static readonly string OutputDir = @"D:\Zimnicky\Моё\Психа";
        private static readonly string MapCacheFile = Path.Combine(OutputDir, "solar_map.bin");
        private static readonly string FineResultsFile = Path.Combine(OutputDir, "fine_tuning_3D.csv");

        private static readonly double[] Masses = {
            1.98841e30, 1.89819e27, 5.6834e26, 8.6813e25, 1.02413e26, 2.29e19
        };

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

            if (!File.Exists(MapCacheFile))
            {
                Console.WriteLine("Ошибка: файл solar_map.bin не найден на диске D:!");
                return;
            }

            double maxYears = 1_000_000.0;
            int recordEverySteps = 50;
            long totalBackSteps = (long)(maxYears * 365.25);
            int historyLen = (int)(totalBackSteps / recordEverySteps) + 1;

            // Плоские массивы для максимальной скорости в .NET 4.7.2
            double[,] hx = new double[historyLen, N];
            double[,] hy = new double[historyLen, N];
            double[,] hz = new double[historyLen, N];
            double[,] hvx = new double[historyLen, N];
            double[,] hvy = new double[historyLen, N];
            double[,] hvz = new double[historyLen, N];

            Console.WriteLine("=== ТУРБО-РЕЖИМ 3D-ПРИЦЕЛИВАНИЯ (Секторный AVX/Scalar) ===");
            Console.WriteLine("Загрузка опорной карты с диска D:...");
            LoadMapFlat(MapCacheFile, hx, hy, hz, hvx, hvy, hvz, historyLen);
            Console.WriteLine("Карта загружена!\n");

            double[] candidateYears = { 800436.96, 649187.68 };
            var allBestShots = new List<PreciseShot>();

            // Заголовок CSV
            File.WriteAllText(FineResultsFile, "CandidateWindow;Target;ExactYearsAgo;MinDistKm;SpeedKmS;AzimuthDeg;ElevationDeg;FlightYears;OriginX_MlnKm;OriginY_MlnKm;OriginZ_MlnKm\n", Encoding.UTF8);

            foreach (double centerYear in candidateYears)
            {
                Console.WriteLine($"--- Расчет окна вокруг {centerYear:N2} лет назад ---");
                Stopwatch sw = Stopwatch.StartNew();

                int centerIdx = (int)((centerYear * 365.25) / recordEverySteps);
                int windowSlices = (int)((3.0 * 365.25) / recordEverySteps); // +-15 лет
                int startSlice = Math.Max(100, centerIdx - windowSlices);
                int endSlice = Math.Min(historyLen - 100, centerIdx + windowSlices);
                int totalSlices = endSlice - startSlice + 1;

                PreciseShot bestUranus = new PreciseShot { MinDistKm = double.MaxValue };
                PreciseShot bestNeptune = new PreciseShot { MinDistKm = double.MaxValue };
                object lockObj = new object();

                int completedSlices = 0;
                long lastReportMs = 0;
                var pOpts = new ParallelOptions { MaxDegreeOfParallelism = 10 };

                Parallel.For(startSlice, endSlice + 1, pOpts, idx =>
                {
                    double ox = hx[idx, PSYCHE], oy = hy[idx, PSYCHE], oz = hz[idx, PSYCHE];
                    double ovx = hvx[idx, PSYCHE], ovy = hvy[idx, PSYCHE], ovz = hvz[idx, PSYCHE];
                    double yearsAgo = (idx * recordEverySteps) / 365.25;

                    // Вычисляем опорные направления на Уран (через ~3.2 года) и Нептун (через ~5.2 года)
                    int idxU = Math.Max(1, idx - (int)((3.2 * 365.25) / recordEverySteps));
                    int idxN = Math.Max(1, idx - (int)((5.2 * 365.25) / recordEverySteps));

                    double baseAzU = Math.Atan2(hy[idxU, URANUS] - oy, hx[idxU, URANUS] - ox) * 180.0 / Math.PI;
                    double baseAzN = Math.Atan2(hy[idxN, NEPTUNE] - oy, hx[idxN, NEPTUNE] - ox) * 180.0 / Math.PI;
                    if (baseAzU < 0) baseAzU += 360.0;
                    if (baseAzN < 0) baseAzN += 360.0;

                    double localMinU = double.MaxValue;
                    PreciseShot localShotU = null;
                    double localMinN = double.MaxValue;
                    PreciseShot localShotN = null;

                    // 1. Прицельный секторный огонь по УРАНУ (+-15 град вокруг упреждения, шаг 0.15 град!)
                    ShootSector(idx, recordEverySteps, hx, hy, hz, ox, oy, oz, ovx, ovy, ovz,
                        baseAzU - 15.0, baseAzU + 15.0, 0.15,
                        -6.0, 6.0, 0.4,
                        18000, 24000, 1000,
                        URANUS, "Уран", centerYear.ToString("F2", CultureInfo.InvariantCulture), yearsAgo,
                        ref localMinU, ref localShotU);

                    // 2. Прицельный секторный огонь по НЕПТУНУ (+-15 град вокруг упреждения, шаг 0.15 град!)
                    ShootSector(idx, recordEverySteps, hx, hy, hz, ox, oy, oz, ovx, ovy, ovz,
                        baseAzN - 15.0, baseAzN + 15.0, 0.15,
                        -6.0, 6.0, 0.4,
                        21000, 27000, 1000,
                        NEPTUNE, "Нептун", centerYear.ToString("F2", CultureInfo.InvariantCulture), yearsAgo,
                        ref localMinN, ref localShotN);

                    lock (lockObj)
                    {
                        if (localShotU != null && localMinU < bestUranus.MinDistKm) bestUranus = localShotU;
                        if (localShotN != null && localMinN < bestNeptune.MinDistKm) bestNeptune = localShotN;
                    }

                    int done = Interlocked.Increment(ref completedSlices);
                    if (sw.ElapsedMilliseconds - Volatile.Read(ref lastReportMs) >= 3000)
                    {
                        lock (lockObj)
                        {
                            if (sw.ElapsedMilliseconds - lastReportMs >= 3000)
                            {
                                lastReportMs = sw.ElapsedMilliseconds;
                                double pct = (double)done / totalSlices * 100.0;
                                double elSec = sw.Elapsed.TotalSeconds;
                                double etaSec = done > 0 ? (elSec / done) * (totalSlices - done) : 0;
                                Console.WriteLine($"  [{pct,5:F1}%] Срез {done}/{totalSlices} | Прошло: {elSec:F0} с | Осталось: ~{etaSec:F0} с | Мин.Уран: {bestUranus.MinDistKm:N0} км | Мин.Нептун: {bestNeptune.MinDistKm:N0} км");
                            }
                        }
                    }
                });

                sw.Stop();
                allBestShots.Add(bestUranus);
                allBestShots.Add(bestNeptune);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n>>> ИТОГ ОКНА {centerYear:N2} (за {sw.Elapsed.TotalSeconds:F1} сек):");
                Console.WriteLine($"  УРАН:   Мин. дист. = {bestUranus.MinDistKm:N0} км | Дата = {bestUranus.YearsAgo:N2} л.н. | V = {bestUranus.SpeedKmS} км/с | Az = {bestUranus.AzimuthDeg:F2}°, El = {bestUranus.ElevationDeg:F2}° | Полет = {bestUranus.FlightYears:F2} л");
                Console.WriteLine($"  НЕПТУН: Мин. дист. = {bestNeptune.MinDistKm:N0} км | Дата = {bestNeptune.YearsAgo:N2} л.н. | V = {bestNeptune.SpeedKmS} км/с | Az = {bestNeptune.AzimuthDeg:F2}°, El = {bestNeptune.ElevationDeg:F2}° | Полет = {bestNeptune.FlightYears:F2} л\n");
                Console.ResetColor();

                // Сразу дописываем в CSV
                AppendShotsToCsv(FineResultsFile, new[] { bestUranus, bestNeptune });
            }

            Console.WriteLine($"=== ВСЕ РАСЧЕТЫ ЗАВЕРШЕНЫ. Отчет сохранен в {FineResultsFile} ===");
        }

        private static void ShootSector(
            int idx, int recEvery,
            double[,] hx, double[,] hy, double[,] hz,
            double ox, double oy, double oz, double ovx, double ovy, double ovz,
            double azStart, double azEnd, double azStep,
            double elStart, double elEnd, double elStep,
            double vStart, double vEnd, double vStep,
            int targetIdx, string targetName, string winName, double yearsAgo,
            ref double bestDistKm, ref PreciseShot bestShot)
        {
            double dt = 21600.0; // 6 часов
            double halfDt = dt * 0.5;
            double dt6 = dt / 6.0;
            int maxSteps = (int)(7.0 * 365.25 * 4); // 7 лет полета

            for (double speed = vStart; speed <= vEnd; speed += vStep)
            {
                for (double elDeg = elStart; elDeg <= elEnd; elDeg += elStep)
                {
                    double elRad = elDeg * Math.PI / 180.0;
                    double cosEl = Math.Cos(elRad);
                    double sinEl = Math.Sin(elRad);
                    double vz0 = ovz + speed * sinEl;
                    double vHoriz = speed * cosEl;

                    for (double azDeg = azStart; azDeg <= azEnd; azDeg += azStep)
                    {
                        double normAz = (azDeg % 360.0 + 360.0) % 360.0;
                        double azRad = normAz * Math.PI / 180.0;
                        double px = ox, py = oy, pz = oz;
                        double vx = ovx + vHoriz * Math.Cos(azRad);
                        double vy = ovy + vHoriz * Math.Sin(azRad);
                        double vz = vz0;
                        for (int s = 1; s <= maxSteps; s++)
                        {
                            double exactSlice = idx - (s * 0.25) / recEvery;
                            int h0 = (int)exactSlice;
                            if (h0 < 1) break;
                            double frac = exactSlice - h0;
                            // Скалярный шаг RK4 (без создания структур Vec3!)
                            AccelScalar(px, py, pz, hx, hy, hz, h0, frac, out double ax1, out double ay1, out double az1);
                            double vx2 = vx + ax1 * halfDt, vy2 = vy + ay1 * halfDt, vz2 = vz + az1 * halfDt;
                            AccelScalar(px + vx * halfDt, py + vy * halfDt, pz + vz * halfDt, hx, hy, hz, h0, frac, out double ax2, out double ay2, out double az2);
                            double vx3 = vx + ax2 * halfDt, vy3 = vy + ay2 * halfDt, vz3 = vz + az2 * halfDt;
                            AccelScalar(px + vx2 * halfDt, py + vy2 * halfDt, pz + vz2 * halfDt, hx, hy, hz, h0, frac, out double ax3, out double ay3, out double az3);
                            double vx4 = vx + ax3 * dt, vy4 = vy + ay3 * dt, vz4 = vz + az3 * dt;
                            AccelScalar(px + vx3 * dt, py + vy3 * dt, pz + vz3 * dt, hx, hy, hz, h0, frac, out double ax4, out double ay4, out double az4);
                            px += (vx + 2.0 * vx2 + 2.0 * vx3 + vx4) * dt6;
                            py += (vy + 2.0 * vy2 + 2.0 * vy3 + vy4) * dt6;
                            pz += (vz + 2.0 * vz2 + 2.0 * vz3 + vz4) * dt6;
                            vx += (ax1 + 2.0 * ax2 + 2.0 * ax3 + ax4) * dt6;
                            vy += (ay1 + 2.0 * ay2 + 2.0 * ay3 + ay4) * dt6;
                            vz += (az1 + 2.0 * az2 + 2.0 * az3 + az4) * dt6;
                            // Позиция целевой планеты
                            double tx = hx[h0, targetIdx] + (hx[h0 + 1, targetIdx] - hx[h0, targetIdx]) * frac;
                            double ty = hy[h0, targetIdx] + (hy[h0 + 1, targetIdx] - hy[h0, targetIdx]) * frac;
                            double tz = hz[h0, targetIdx] + (hz[h0 + 1, targetIdx] - hz[h0, targetIdx]) * frac;
                            double dx = px - tx, dy = py - ty, dz = pz - tz;
                            double distKm = Math.Sqrt(dx * dx + dy * dy + dz * dz) * 0.001;
                            if (distKm < bestDistKm)
                            {
                                bestDistKm = distKm;
                                bestShot = new PreciseShot
                                {
                                    WindowName = winName,
                                    YearsAgo = yearsAgo,
                                    Target = targetName,
                                    SpeedKmS = speed * 0.001,
                                    AzimuthDeg = normAz,
                                    ElevationDeg = elDeg,
                                    FlightYears = (s * 0.25) / 365.25,
                                    MinDistKm = distKm,
                                    OrigX = (ox - hx[idx, SUN]) * 1e-9,
                                    OrigY = (oy - hy[idx, SUN]) * 1e-9,
                                    OrigZ = (oz - hz[idx, SUN]) * 1e-9
                                };
                            }
                        }
                    }
                }
            }
        }
        private static void AccelScalar(double fx, double fy, double fz, double[,] hx, double[,] hy, double[,] hz, int h0, double frac, out double ax, out double ay, out double az)
        {
            ax = 0; ay = 0; az = 0;
            for (int j = 0; j < 5; j++)
            {
                double bx = hx[h0, j] + (hx[h0 + 1, j] - hx[h0, j]) * frac;
                double by = hy[h0, j] + (hy[h0 + 1, j] - hy[h0, j]) * frac;
                double bz = hz[h0, j] + (hz[h0 + 1, j] - hz[h0, j]) * frac;
                double dx = bx - fx, dy = by - fy, dz = bz - fz;
                double rSq = dx * dx + dy * dy + dz * dz;
                if (rSq < 1e10) rSq = 1e10;
                double factor = G * Masses[j] / (rSq * Math.Sqrt(rSq));
                ax += dx * factor;
                ay += dy * factor;
                az += dz * factor;
            }
        }
        private static void LoadMapFlat(string path, double[,] hx, double[,] hy, double[,] hz, double[,] hvx, double[,] hvy, double[,] hvz, int hLen)
        {
            using (var br = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                for (int r = 0; r < hLen; r++)
                {
                    for (int i = 0; i < N; i++)
                    {
                        hx[r, i] = br.ReadDouble(); hy[r, i] = br.ReadDouble(); hz[r, i] = br.ReadDouble();
                        hvx[r, i] = br.ReadDouble(); hvy[r, i] = br.ReadDouble(); hvz[r, i] = br.ReadDouble();
                    }
                }
            }
        }
        private static void AppendShotsToCsv(string path, IEnumerable<PreciseShot> shots)
        {
            var sb = new StringBuilder();
            foreach (var s in shots)
            {
                if (s == null) continue;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0};{1};{2:F2};{3:F0};{4:F1};{5:F2};{6:F2};{7:F3};{8:F2};{9:F2};{10:F2}",
                s.WindowName, s.Target, s.YearsAgo, s.MinDistKm, s.SpeedKmS, s.AzimuthDeg, s.ElevationDeg, s.FlightYears,
                s.OrigX, s.OrigY, s.OrigZ));
            }
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}