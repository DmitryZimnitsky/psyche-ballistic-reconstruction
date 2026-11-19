using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PsycheMenuTuner3D
{
    public class WindowInfo
    {
        public int Id;
        public double YearsAgo;
        public string Note;
    }

    public class PreciseShot
    {
        public int WindowId;
        public double WindowCenterYear;
        public double ExactYearsAgo;
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
        private static readonly string ResultsCsv = Path.Combine(OutputDir, "menu_3D_results.csv");

        private static readonly double[] Masses = {
            1.98841e30, 1.89819e27, 5.6834e26, 8.6813e25, 1.02413e26, 2.29e19
        };

        // Полный список всех 15 окон из первичного поиска
        private static readonly List<WindowInfo> Windows = new List<WindowInfo>
        {
            new WindowInfo { Id = 1,  YearsAgo = 649187.68, Note = "Лидер (Уран + Нептун, 3D уже проверено)" },
            new WindowInfo { Id = 2,  YearsAgo = 800436.96, Note = "Кандидат №2 (Уран + Нептун, 3D уже проверено)" },
            new WindowInfo { Id = 3,  YearsAgo = 978155.24, Note = "ТОП из оставшихся: Сатурн (2.65) + Нептун (2.97) (3D уже проверено)" },
            new WindowInfo { Id = 4,  YearsAgo = 974863.24, Note = "ТОП из оставшихся: Нептун (3.69) + Сатурн (3.28)" },
            new WindowInfo { Id = 5,  YearsAgo = 479955.65, Note = "ТОП из оставшихся: Сатурн (4.99) + Нептун (3.73)" },
            new WindowInfo { Id = 6,  YearsAgo = 602193.02, Note = "Сатурн (1.74 млн км!) + Уран (4.10)" },
            new WindowInfo { Id = 7,  YearsAgo = 356264.48, Note = "Уран (2.82) + Сатурн (3.81)" },
            new WindowInfo { Id = 8,  YearsAgo = 474229.16, Note = "Уран (3.12) + Сатурн (4.14)" },
            new WindowInfo { Id = 9,  YearsAgo = 938833.68, Note = "Уран (3.10) + Сатурн (4.42)" },
            new WindowInfo { Id = 10, YearsAgo = 695423.41, Note = "Сатурн (3.43) + Уран (4.25)" },
            new WindowInfo { Id = 11, YearsAgo = 800047.64, Note = "Уран (3.72) + Сатурн (3.80)" },
            new WindowInfo { Id = 12, YearsAgo = 693319.10, Note = "Уран (4.26) + Сатурн (4.67)" },
            new WindowInfo { Id = 13, YearsAgo = 695497.33, Note = "Сатурн (4.23) + Уран (4.71)" },
            new WindowInfo { Id = 14, YearsAgo = 915858.73, Note = "Сатурн (4.42) + Уран (4.54)" },
            new WindowInfo { Id = 15, YearsAgo = 339794.66, Note = "Уран (4.67) + Сатурн (5.00)" }
        };

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
            Directory.CreateDirectory(OutputDir);

            // Поиск опорной карты на диске D: или на флешке H:
            string mapFile = Path.Combine(OutputDir, "solar_map.bin");
            if (!File.Exists(mapFile)) mapFile = @"H:\Psyche\solar_map.bin";
            if (!File.Exists(mapFile))
            {
                Console.WriteLine("ОШИБКА: Файл solar_map.bin не найден ни на D:\\Zimnicky\\Моё\\Психа, ни на H:\\Psyche!");
                Console.ReadKey();
                return;
            }

            double maxYears = 1_000_000.0;
            int recordEverySteps = 50;
            long totalBackSteps = (long)(maxYears * 365.25);
            int historyLen = (int)(totalBackSteps / recordEverySteps) + 1;

            double[,] hx = new double[historyLen, N];
            double[,] hy = new double[historyLen, N];
            double[,] hz = new double[historyLen, N];
            double[,] hvx = new double[historyLen, N];
            double[,] hvy = new double[historyLen, N];
            double[,] hvz = new double[historyLen, N];

            Console.WriteLine($"Загрузка опорной карты из {mapFile}...");
            LoadMapFlat(mapFile, hx, hy, hz, hvx, hvy, hvz, historyLen);
            Console.WriteLine("Карта загружена успешно!\n");

            if (!File.Exists(ResultsCsv))
            {
                File.WriteAllText(ResultsCsv, "WindowId;CenterYear;Target;ExactYearsAgo;MinDistKm;SpeedKmS;AzimuthDeg;ElevationDeg;FlightYears;OriginX_MlnKm;OriginY_MlnKm;OriginZ_MlnKm\n", Encoding.UTF8);
            }

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("==========================================================================================");
                Console.WriteLine(" МЕНЮ 3D-ПРИЦЕЛИВАНИЯ (ПО ВСЕМ ТРЕМ ГИГАНТАМ: САТУРН, УРАН, НЕПТУН)");
                Console.WriteLine("==========================================================================================");
                foreach (var w in Windows)
                {
                    Console.WriteLine($" [{w.Id,2}] {w.YearsAgo,10:F2} л.н. | {w.Note}");
                }
                Console.WriteLine("------------------------------------------------------------------------------------------");
                Console.WriteLine(" Введите номер окна (1-15), несколько номеров через запятую (напр. 3,4,5) или 0 для выхода:");
                Console.Write(" Ваш выбор > ");
                Console.ResetColor();

                string input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input) || input == "0") break;

                var selectedIds = new List<int>();
                foreach (var part in input.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part, out int id) && id >= 1 && id <= Windows.Count)
                        selectedIds.Add(id);
                }

                if (selectedIds.Count == 0)
                {
                    Console.WriteLine("Некорректный ввод. Попробуйте снова.\n");
                    continue;
                }

                foreach (int id in selectedIds)
                {
                    var win = Windows[id - 1];
                    RunWindow3D(win, recordEverySteps, historyLen, hx, hy, hz, hvx, hvy, hvz);
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\nВсе выбранные задачи завершены! Данные сохранены в: {ResultsCsv}\n");
                Console.ResetColor();
            }
        }

        private static void RunWindow3D(WindowInfo win, int recordEverySteps, int historyLen,
    double[,] hx, double[,] hy, double[,] hz, double[,] hvx, double[,] hvy, double[,] hvz)
        {
            Console.WriteLine($"\n>>> ЭКСПРЕСС-ПОИСК 3D (ПО ЗАКОНОМЕРНОСТЯМ) ДЛЯ ОКНА [{win.Id}]: {win.YearsAgo:N2} л.н. <<<");
            Stopwatch sw = Stopwatch.StartNew();

            int centerIdx = (int)((win.YearsAgo * 365.25) / recordEverySteps);
            // Сужаем окно до +-1.2 года (всего 19 срезов вокруг центрального пика)
            int windowSlices = (int)((1.2 * 365.25) / recordEverySteps);
            int startSlice = Math.Max(100, centerIdx - windowSlices);
            int endSlice = Math.Min(historyLen - 100, centerIdx + windowSlices);
            int totalSlices = endSlice - startSlice + 1;

            PreciseShot bestSaturn = new PreciseShot { MinDistKm = double.MaxValue };
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

                // Упреждение по времени полета: Сатурн (~3.2 г), Уран (~3.6 г), Нептун (~5.2 г)
                int idxS = Math.Max(1, idx - (int)((3.2 * 365.25) / recordEverySteps));
                int idxU = Math.Max(1, idx - (int)((3.6 * 365.25) / recordEverySteps));
                int idxN = Math.Max(1, idx - (int)((5.2 * 365.25) / recordEverySteps));

                double baseAzS = GetAzimuthDeg(hy[idxS, SATURN] - oy, hx[idxS, SATURN] - ox);
                double baseAzU = GetAzimuthDeg(hy[idxU, URANUS] - oy, hx[idxU, URANUS] - ox);
                double baseAzN = GetAzimuthDeg(hy[idxN, NEPTUNE] - oy, hx[idxN, NEPTUNE] - ox);

                double localMinS = double.MaxValue; PreciseShot localShotS = null;
                double localMinU = double.MaxValue; PreciseShot localShotU = null;
                double localMinN = double.MaxValue; PreciseShot localShotN = null;

                // 1. Сатурн: узкий сектор +-6 град, наклон El от -1.2 до +1.2 град, скорости 10-16 км/с
                ShootSector(idx, recordEverySteps, hx, hy, hz, ox, oy, oz, ovx, ovy, ovz,
                    baseAzS - 6.0, baseAzS + 6.0, 0.15, -1.2, 1.2, 0.2, 10000, 16000, 1000,
                    SATURN, "Сатурн", win.Id, win.YearsAgo, yearsAgo, ref localMinS, ref localShotS);

                // 2. Уран: узкий сектор +-6 град, наклон El от -1.2 до +1.2 град, скорости 18-22 км/с
                ShootSector(idx, recordEverySteps, hx, hy, hz, ox, oy, oz, ovx, ovy, ovz,
                    baseAzU - 6.0, baseAzU + 6.0, 0.15, -1.2, 1.2, 0.2, 18000, 22000, 1000,
                    URANUS, "Уран", win.Id, win.YearsAgo, yearsAgo, ref localMinU, ref localShotU);

                // 3. Нептун: узкий сектор +-6 град, наклон El от -1.2 до +1.2 град, скорости 20-25 км/с
                ShootSector(idx, recordEverySteps, hx, hy, hz, ox, oy, oz, ovx, ovy, ovz,
                    baseAzN - 6.0, baseAzN + 6.0, 0.15, -1.2, 1.2, 0.2, 20000, 25000, 1000,
                    NEPTUNE, "Нептун", win.Id, win.YearsAgo, yearsAgo, ref localMinN, ref localShotN);

                lock (lockObj)
                {
                    if (localShotS != null && localMinS < bestSaturn.MinDistKm) bestSaturn = localShotS;
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
                            Console.WriteLine($"  [{pct,5:F1}%] Срез {done}/{totalSlices} | Прошло: {elSec:F0}с | Осталось: ~{etaSec:F0}с | Мин: Сатурн={bestSaturn.MinDistKm:N0}, Уран={bestUranus.MinDistKm:N0}, Нептун={bestNeptune.MinDistKm:N0} км");
                        }
                    }
                }
            });

            sw.Stop();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n>>> ИТОГ ОКНА [{win.Id}] {win.YearsAgo:N2} л.н. (за {sw.Elapsed.TotalSeconds:F1} сек):");
            PrintShot("САТУРН", bestSaturn);
            PrintShot("УРАН  ", bestUranus);
            PrintShot("НЕПТУН", bestNeptune);
            Console.ResetColor();

            AppendShotsToCsv(ResultsCsv, new[] { bestSaturn, bestUranus, bestNeptune });
        }

        private static void PrintShot(string label, PreciseShot s)
        {
            if (s == null || s.MinDistKm > 1e12)
                Console.WriteLine($"  {label}: Попаданий в секторе не зафиксировано.");
            else
                Console.WriteLine($"  {label}: Мин. дист. = {s.MinDistKm,9:N0} км | Дата = {s.ExactYearsAgo:N2} л.н. | V = {s.SpeedKmS,4:F1} км/с | Az = {s.AzimuthDeg,6:F2}°, El = {s.ElevationDeg,5:F2}° | Полет = {s.FlightYears:F2} л");
        }
        private static double GetAzimuthDeg(double dy, double dx)
        {
            double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            return deg < 0 ? deg + 360.0 : deg;
        }
        private static void ShootSector(
        int idx, int recEvery, double[,] hx, double[,] hy, double[,] hz,
        double ox, double oy, double oz, double ovx, double ovy, double ovz,
        double azStart, double azEnd, double azStep,
        double elStart, double elEnd, double elStep,
        double vStart, double vEnd, double vStep,
        int targetIdx, string targetName, int winId, double winCenter, double yearsAgo,
        ref double bestDistKm, ref PreciseShot bestShot)
        {
            double dt = 21600.0; // 6 часов
            double halfDt = dt * 0.5;
            double dt6 = dt / 6.0;
            int maxSteps = (int)(7.5 * 365.25 * 4); // до 7.5 лет полета
            for (double speed = vStart; speed <= vEnd; speed += vStep)
            {
                for (double elDeg = elStart; elDeg <= elEnd; elDeg += elStep)
                {
                    double elRad = elDeg * Math.PI / 180.0;
                    double cosEl = Math.Cos(elRad);
                    double vz0 = ovz + speed * Math.Sin(elRad);
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
                                    WindowId = winId,
                                    WindowCenterYear = winCenter,
                                    ExactYearsAgo = yearsAgo,
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
                ax += dx * factor; ay += dy * factor; az += dz * factor;
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
                if (s == null || s.MinDistKm > 1e12) continue;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0};{1:F2};{2};{3:F2};{4:F0};{5:F1};{6:F2};{7:F2};{8:F3};{9:F2};{10:F2};{11:F2}",
                s.WindowId, s.WindowCenterYear, s.Target, s.ExactYearsAgo, s.MinDistKm, s.SpeedKmS,
                s.AzimuthDeg, s.ElevationDeg, s.FlightYears, s.OrigX, s.OrigY, s.OrigZ));
            }
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}