using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PsycheSniperShot
{
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

        private static readonly double[] Masses = {
            1.98841e30, 1.89819e27, 5.6834e26, 8.6813e25, 1.02413e26, 2.29e19
        };

        private static readonly string OutputDir = @"D:\Zimnicky\Моё\Психа";
        private static readonly string SniperCsv = Path.Combine(OutputDir, "sniper_649187_results.csv");

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Directory.CreateDirectory(OutputDir);

            string mapFile = Path.Combine(OutputDir, "solar_map.bin");
            if (!File.Exists(mapFile)) mapFile = @"H:\Psyche\solar_map.bin";
            if (!File.Exists(mapFile))
            {
                Console.WriteLine("ОШИБКА: Файл solar_map.bin не найден!");
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

            Console.WriteLine("Загрузка опорной карты...");
            LoadMapFlat(mapFile, hx, hy, hz, hvx, hvy, hvz, historyLen);

            // Фиксируем ЕДИНУЮ точку взрыва: 649 187.68 лет назад (и соседний срез -1 / +1 для точности)
            double targetYear = 649187.68;
            int centerIdx = (int)((targetYear * 365.25) / recordEverySteps);

            Console.WriteLine($"\n=== СНАЙПЕРСКАЯ ПРИСТРЕЛКА ИЗ ЕДИНОЙ ТОЧКИ ({targetYear:N2} ЛЕТ НАЗАД) ===");
            Stopwatch sw = Stopwatch.StartNew();

            var sb = new StringBuilder();
            sb.AppendLine("SliceOffset;ExactYearsAgo;Target;MinDistKm;SpeedKmS;AzimuthDeg;ElevationDeg;FlightYears;RelVelKmS");

            // Проверяем сам срез 649187.68 и два соседних (+-50 дней), чтобы найти идеальную общую секунду
            for (int offset = -1; offset <= 1; offset++)
            {
                int idx = centerIdx + offset;
                double exactYear = (idx * recordEverySteps) / 365.25;
                double ox = hx[idx, PSYCHE], oy = hy[idx, PSYCHE], oz = hz[idx, PSYCHE];
                double ovx = hvx[idx, PSYCHE], ovy = hvy[idx, PSYCHE], ovz = hvz[idx, PSYCHE];

                // 1. Ультратонкий огонь по УРАНУ вокруг Az=138.11, El=0.40, V=19 км/с
                var hitU = SniperScan(idx, recordEverySteps, hx, hy, hz, hvx, hvy, hvz,
                    ox, oy, oz, ovx, ovy, ovz,
                    138.11 - 0.8, 138.11 + 0.8, 0.01,
                    0.40 - 0.6, 0.40 + 0.6, 0.01,
                    18500, 19500, 100, URANUS, "Уран", exactYear);

                // 2. Ультратонкий огонь по НЕПТУНУ вокруг Az=145.16, El=0.00, V=24 км/с
                var hitN = SniperScan(idx, recordEverySteps, hx, hy, hz, hvx, hvy, hvz,
                    ox, oy, oz, ovx, ovy, ovz,
                    145.16 - 0.8, 145.16 + 0.8, 0.01,
                    0.00 - 0.6, 0.00 + 0.6, 0.01,
                    23500, 24500, 100, NEPTUNE, "Нептун", exactYear);

                Console.WriteLine($"\n--- Единый старт: {exactYear:N2} лет назад (Сдвиг {offset * 50} дней) ---");
                Console.WriteLine($"  УРАН  : Мин. дист. = {hitU.distKm,8:N0} км | V_выбр = {hitU.vKmS,5:F2} км/с | Az = {hitU.az,7:F3}°, El = {hitU.el,6:F3}° | Полет = {hitU.flightY:F3} л | V_подлета = {hitU.relVelKmS:F2} км/с");
                Console.WriteLine($"  НЕПТУН: Мин. дист. = {hitN.distKm,8:N0} км | V_выбр = {hitN.vKmS,5:F2} км/с | Az = {hitN.az,7:F3}°, El = {hitN.el,6:F3}° | Полет = {hitN.flightY:F3} л | V_подлета = {hitN.relVelKmS:F2} км/с");

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0};{1:F2};Уран;{2:F0};{3:F2};{4:F3};{5:F3};{6:F4};{7:F2}",
                    offset, exactYear, hitU.distKm, hitU.vKmS, hitU.az, hitU.el, hitU.flightY, hitU.relVelKmS));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0};{1:F2};Нептун;{2:F0};{3:F2};{4:F3};{5:F3};{6:F4};{7:F2}",
                    offset, exactYear, hitN.distKm, hitN.vKmS, hitN.az, hitN.el, hitN.flightY, hitN.relVelKmS));
            }

            sw.Stop();
            File.WriteAllText(SniperCsv, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"\nСнайперская пристрелка завершена за {sw.Elapsed.TotalSeconds:F2} сек! Сохранено в {SniperCsv}");
        }

        private static (double distKm, double vKmS, double az, double el, double flightY, double relVelKmS) SniperScan(
            int idx, int recEvery, double[,] hx, double[,] hy, double[,] hz, double[,] hvx, double[,] hvy, double[,] hvz,
            double ox, double oy, double oz, double ovx, double ovy, double ovz,
            double azStart, double azEnd, double azStep,
            double elStart, double elEnd, double elStep,
            double vStart, double vEnd, double vStep,
            int targetIdx, string targetName, double exactYear)
        {
            double bestDist = double.MaxValue;
            double bV = 0, bAz = 0, bEl = 0, bFly = 0, bRelV = 0;
            object lockObj = new object();

            int elCount = (int)Math.Round((elEnd - elStart) / elStep) + 1;
            double dt = 7200.0; // Шаг полета: 2 часа для максимальной точности касания!
            double halfDt = dt * 0.5;
            double dt6 = dt / 6.0;
            int maxSteps = (int)(5.5 * 365.25 * 12);

            Parallel.For(0, elCount, new ParallelOptions { MaxDegreeOfParallelism = 10 }, eIdx =>
            {
            double elDeg = elStart + eIdx * elStep;
            double elRad = elDeg * Math.PI / 180.0;
            double cosEl = Math.Cos(elRad);
            double sinEl = Math.Sin(elRad);

            double localBest = double.MaxValue;
            double lV = 0, lAz = 0, lFly = 0, lRelV = 0;

            for (double speed = vStart; speed <= vEnd; speed += vStep)
            {
                double vz0 = ovz + speed * sinEl;
                double vHoriz = speed * cosEl;

                for (double azDeg = azStart; azDeg <= azEnd; azDeg += azStep)
                {
                    double azRad = azDeg * Math.PI / 180.0;
                    double px = ox, py = oy, pz = oz;
                    double vx = ovx + vHoriz * Math.Cos(azRad);
                    double vy = ovy + vHoriz * Math.Sin(azRad);
                    double vz = vz0;

                    for (int s = 1; s <= maxSteps; s++)
                    {
                        double exactSlice = idx - (s * (2.0 / 24.0)) / recEvery;
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

                        if (distKm < localBest)
                        {
                            localBest = distKm;
                            lV = speed * 0.001;
                            lAz = azDeg;
                            lFly = (s * (2.0 / 24.0)) / 365.25;

                            double tvx = hvx[h0, targetIdx] + (hvx[h0 + 1, targetIdx] - hvx[h0, targetIdx]) * frac;
                            double tvy = hvy[h0, targetIdx] + (hvy[h0 + 1, targetIdx] - hvy[h0, targetIdx]) * frac;
                                double tvz = hvz[h0, targetIdx] + (hvz[h0 + 1, targetIdx] - hvz[h0, targetIdx]) * frac;
                                double rvx = vx - tvx, rvy = vy - tvy, rvz = vz - tvz;
                                lRelV = Math.Sqrt(rvx * rvx + rvy * rvy + rvz * rvz) * 0.001;
                            }
                        }
                    }
                }
                lock (lockObj)
                {
                    if (localBest < bestDist)
                    {
                        bestDist = localBest; bV = lV; bAz = lAz; bEl = elDeg; bFly = lFly; bRelV = lRelV;
                    }
                }
            });
            return (bestDist, bV, bAz, bEl, bFly, bRelV);
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
    }
}