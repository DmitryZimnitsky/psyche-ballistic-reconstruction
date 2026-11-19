using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace PsycheR631Backtracker
{
    class Program
    {
        private const double G = 6.67430e-11;
        private const double AU_M = 149597870700.0;
        private const double GM_SUN = 1.32712440018e20; // м^3/с^2
        private const int N_MAP = 6;

        private const int SUN = 0;
        private const int JUPITER = 1;
        private const int SATURN = 2;
        private const int URANUS = 3;
        private const int NEPTUNE = 4;
        private const int PSYCHE = 5;

        private static readonly double[] MapMasses = {
            1.98841e30, 1.89819e27, 5.6834e26, 8.6813e25, 1.02413e26, 2.29e19
        };

        private static readonly string OutputDir = @"D:\Zimnicky\Моё\Психа";
        private static readonly string ReportCsv = Path.Combine(OutputDir, "r631_history.csv");

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Directory.CreateDirectory(OutputDir);

            string mapFile = Path.Combine(OutputDir, "solar_map.bin");
            if (!File.Exists(mapFile)) mapFile = @"H:\Psyche\solar_map.bin";
            if (!File.Exists(mapFile))
            {
                Console.WriteLine("ОШИБКА: Файл solar_map.bin не найден ни на D:, ни на H:!");
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
            Console.WriteLine("Карта загружена!\n");

            // 1. Орбитальные элементы R631 (Andrew Lowe, 1052 obs, Epoch JDT 2461200.5 = 2026-Jun-09.0)
            double a_au = 66.9527249;
            double e = 0.6406668;
            double incDeg = 142.37384;
            double nodeDeg = 138.26616;
            double periDeg = 53.29755;
            double M0_deg = 345.93639679;
            double n_deg_day = 0.00179908;

            // Пересчет средней аномалии M на базовую дату карты: 2026-Nov-10.0 (JDT 2461354.5, +154.0 дня)
            double deltaDays = 2461354.5 - 2461200.5;
            double M_deg = (M0_deg + n_deg_day * deltaDays) % 360.0;
            if (M_deg < 0) M_deg += 360.0;

            KeplerToCartesian(a_au, e, incDeg, nodeDeg, periDeg, M_deg,
                out double hrx, out double hry, out double hrz,
                out double hvx, out double hvy, out double hvz);

            // Переводим из гелиоцентрических координат в барицентрические (прибавляем вектор Солнца на 2026-Nov-10)
            // Скорость Солнца на 2026-Nov-10 берем из разности первых срезов карты
            double sunVx = (hx[0, SUN] - hx[1, SUN]) / (recordEverySteps * 86400.0);
            double sunVy = (hy[0, SUN] - hy[1, SUN]) / (recordEverySteps * 86400.0);
            double sunVz = (hz[0, SUN] - hz[1, SUN]) / (recordEverySteps * 86400.0);

            double px = hx[0, SUN] + hrx;
            double py = hy[0, SUN] + hry;
            double pz = hz[0, SUN] + hrz;
            double vx = sunVx + hvx;
            double vy = sunVy + hvy;
            double vz = sunVz + hvz;

            Console.WriteLine("=== НАЧАЛЬНЫЕ КООРДИНАТЫ R631 НА 2026-NOV-10 (БАРИЦЕНТР) ===");
            Console.WriteLine($"  X = {px / 1000.0:E6} км | Y = {py / 1000.0:E6} км | Z = {pz / 1000.0:E6} км");
            Console.WriteLine($"  VX= {vx / 1000.0:F4} км/с | VY= {vy / 1000.0:F4} км/с | VZ= {vz / 1000.0:F4} км/с\n");

            // 2. Обратное интегрирование на 1 000 000 лет (шаг -1 сутки)
            double[] minDistAu = { double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue };
            double[] minDistYear = new double[N_MAP];
            string[] bodyNames = { "Солнце", "Юпитер", "Сатурн", "Уран", "Нептун", "16 Психея" };

            // Отдельно следим за сближениями в окрестностях наших главных окон (+- 500 лет)
            double[] keyWindows = { 649187.7, 800437.0, 978156.3 };
            double[,] winMinUranus = new double[keyWindows.Length, 2]; // [distAu, year]
            double[,] winMinNeptune = new double[keyWindows.Length, 2];
            for (int w = 0; w < keyWindows.Length; w++) { winMinUranus[w, 0] = 999; winMinNeptune[w, 0] = 999; }

            var sb = new StringBuilder();
            sb.AppendLine("YearsAgo;HelioDist_AU;UranusDist_AU;NeptuneDist_AU;PsycheDist_AU");

            Stopwatch sw = Stopwatch.StartNew();
            double dt = -86400.0;
            double halfDt = dt * 0.5;
            double dt6 = dt / 6.0;

            for (long step = 1; step <= totalBackSteps; step++)
            {
                double exactSlice = (double)step / recordEverySteps;
                int h0 = (int)exactSlice;
                if (h0 >= historyLen - 1) break;
                double frac = exactSlice - h0;

                AccelBody(px, py, pz, hx, hy, hz, h0, frac, out double ax1, out double ay1, out double az1);
                double vx2 = vx + ax1 * halfDt, vy2 = vy + ay1 * halfDt, vz2 = vz + az1 * halfDt;
                AccelBody(px + vx * halfDt, py + vy * halfDt, pz + vz * halfDt, hx, hy, hz, h0, frac, out double ax2, out double ay2, out double az2);
                double vx3 = vx + ax2 * halfDt, vy3 = vy + ay2 * halfDt, vz3 = vz + az2 * halfDt;
                AccelBody(px + vx2 * halfDt, py + vy2 * halfDt, pz + vz2 * halfDt, hx, hy, hz, h0, frac, out double ax3, out double ay3, out double az3);
                double vx4 = vx + ax3 * dt, vy4 = vy + ay3 * dt, vz4 = vz + az3 * dt;
                AccelBody(px + vx3 * dt, py + vy3 * dt, pz + vz3 * dt, hx, hy, hz, h0, frac, out double ax4, out double ay4, out double az4);

                px += (vx + 2.0 * vx2 + 2.0 * vx3 + vx4) * dt6;
                py += (vy + 2.0 * vy2 + 2.0 * vy3 + vy4) * dt6;
                pz += (vz + 2.0 * vz2 + 2.0 * vz3 + vz4) * dt6;

                vx += (ax1 + 2.0 * ax2 + 2.0 * ax3 + ax4) * dt6;
                vy += (ay1 + 2.0 * ay2 + 2.0 * ay3 + ay4) * dt6;
                vz += (az1 + 2.0 * az2 + 2.0 * az3 + az4) * dt6;

                double yearsAgo = step / 365.25;
                double dSun = 0, dUra = 0, dNep = 0, dPsy = 0;

                for (int j = 0; j < N_MAP; j++)
                {
                    double bx = hx[h0, j] + (hx[h0 + 1, j] - hx[h0, j]) * frac;
                    double by = hy[h0, j] + (hy[h0 + 1, j] - hy[h0, j]) * frac;
                    double bz = hz[h0, j] + (hz[h0 + 1, j] - hz[h0, j]) * frac;
                    double dx = px - bx, dy = py - by, dz = pz - bz;
                    double distAu = Math.Sqrt(dx * dx + dy * dy + dz * dz) / AU_M;

                    if (distAu < minDistAu[j])
                    {
                        minDistAu[j] = distAu;
                        minDistYear[j] = yearsAgo;
                    }

                    if (j == SUN) dSun = distAu;
                    if (j == URANUS) dUra = distAu;
                    if (j == NEPTUNE) dNep = distAu;
                    if (j == PSYCHE) dPsy = distAu;
                }

                // Проверка в окрестности +- 1000 лет вокруг 3 главных окон катастрофы
                for (int w = 0; w < keyWindows.Length; w++)
                {
                    if (Math.Abs(yearsAgo - keyWindows[w]) <= 1000.0)
                    {
                        if (dUra < winMinUranus[w, 0]) { winMinUranus[w, 0] = dUra; winMinUranus[w, 1] = yearsAgo; }
                        if (dNep < winMinNeptune[w, 0]) { winMinNeptune[w, 0] = dNep; winMinNeptune[w, 1] = yearsAgo; }
                    }
                }

                // Записываем в CSV каждые 500 лет (182625 шагов) для построения графиков
                if (step % 182625 == 0)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F1};{1:F3};{2:F3};{3:F3};{4:F3}",
                        yearsAgo, dSun, dUra, dNep, dPsy));
                }
            }

            sw.Stop();
            File.WriteAllText(ReportCsv, sb.ToString(), Encoding.UTF8);

            Console.WriteLine($"=== 1. МИНИМАЛЬНЫЕ СБЛИЖЕНИЯ R631 ЗА 1 000 000 ЛЕТ (расчет за {sw.Elapsed.TotalSeconds:F2} сек) ===");
            for (int j = 0; j < N_MAP; j++)
            {
                Console.WriteLine($"  {bodyNames[j],-12}: Мин. дист. = {minDistAu[j],6:F3} а.е. ({minDistAu[j] * AU_M / 1e9,7:F2} млн км) | Дата: {minDistYear[j]:N1} л.н.");
            }

            Console.WriteLine("\n=== 2. СБЛИЖЕНИЯ R631 ВНУТРИ 3 ГЛАВНЫХ БАЛЛИСТИЧЕСКИХ ОКОН (+-1000 лет) ===");
            for (int w = 0; w < keyWindows.Length; w++)
            {
                Console.WriteLine($"  Окно {keyWindows[w],9:F1} л.н. -> Уран: {winMinUranus[w, 0]:F3} а.е. ({winMinUranus[w, 0] * AU_M / 1e9:F1} млн км) | Нептун: {winMinNeptune[w, 0]:F3} а.е. ({winMinNeptune[w, 0] * AU_M / 1e9:F1} млн км)");
            }
            Console.WriteLine($"\nИстория эволюции R631 сохранена в: {ReportCsv}");
        }

        private static void KeplerToCartesian(double a_au, double e, double incDeg, double nodeDeg, double periDeg, double M_deg,
            out double x, out double y, out double z, out double vx, out double vy, out double vz)
        {
            double a = a_au * AU_M;
            double inc = incDeg * Math.PI / 180.0;
            double node = nodeDeg * Math.PI / 180.0;
            double peri = periDeg * Math.PI / 180.0;
            double M = M_deg * Math.PI / 180.0;

            // Решение уравнения Кеплера E - e*sin(E) = M
            double E = M;
            for (int iter = 0; iter < 30; iter++)
                E = E - (E - e * Math.Sin(E) - M) / (1.0 - e * Math.Cos(E));
            double cosE = Math.Cos(E), sinE = Math.Sin(E);
            double sqrtOneMinusESq = Math.Sqrt(1.0 - e * e);
            // Координаты и скорости в орбитальной плоскости
            double xOrb = a * (cosE - e);
            double yOrb = a * sqrtOneMinusESq * sinE;
            double r = Math.Sqrt(xOrb * xOrb + yOrb * yOrb);
            double vFactor = Math.Sqrt(GM_SUN * a) / r;
            double vxOrb = -vFactor * sinE;
            double vyOrb = vFactor * sqrtOneMinusESq * cosE;
            // Поворот в эклиптическую систему J2000
            double cosO = Math.Cos(node), sinO = Math.Sin(node);
            double cosW = Math.Cos(peri), sinW = Math.Sin(peri);
            double cosI = Math.Cos(inc), sinI = Math.Sin(inc);
            double Px = cosO * cosW - sinO * sinW * cosI;
            double Py = sinO * cosW + cosO * sinW * cosI;
            double Pz = sinW * sinI;
            double Qx = -cosO * sinW - sinO * cosW * cosI;
            double Qy = -sinO * sinW + cosO * cosW * cosI;
            double Qz = cosW * sinI;
            x = Px * xOrb + Qx * yOrb;
            y = Py * xOrb + Qy * yOrb;
            z = Pz * xOrb + Qz * yOrb;
            vx = Px * vxOrb + Qx * vyOrb;
            vy = Py * vxOrb + Qy * vyOrb;
            vz = Pz * vxOrb + Qz * vyOrb;
        }
        private static void AccelBody(double fx, double fy, double fz, double[,] hx, double[,] hy, double[,] hz, int h0, double frac, out double ax, out double ay, out double az)
        {
            ax = 0; ay = 0; az = 0;
            for (int j = 0; j < N_MAP; j++)
            {
                double bx = hx[h0, j] + (hx[h0 + 1, j] - hx[h0, j]) * frac;
                double by = hy[h0, j] + (hy[h0 + 1, j] - hy[h0, j]) * frac;
                double bz = hz[h0, j] + (hz[h0 + 1, j] - hz[h0, j]) * frac;
                double dx = bx - fx, dy = by - fy, dz = bz - fz;
                double rSq = dx * dx + dy * dy + dz * dz;
                if (rSq < 1e10) rSq = 1e10;
                double factor = G * MapMasses[j] / (rSq * Math.Sqrt(rSq));
                ax += dx * factor; ay += dy * factor; az += dz * factor;
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
                        br.ReadDouble(); br.ReadDouble(); br.ReadDouble();
                    }
                }
            }
        }
    }
}