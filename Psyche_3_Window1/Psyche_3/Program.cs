using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PsycheWorkstationSolver
{
    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double d) => new Vec3(a.X * d, a.Y * d, a.Z * d);
        public double LengthSq() => X * X + Y * Y + Z * Z;
    }

    public class HitResult
    {
        public double ExplosionYearsAgo;
        public Vec3 ExplosionCoordsSun;
        public double EjectionSpeedKmS;
        public string TargetName;
        public double FlightYears;
        public double MissDistanceKm;
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

        // Путь сохранения на диске D
        private static readonly string OutputDir = @"D:\Zimnicky\Моё\Психа";
        private static readonly string MapCacheFile = Path.Combine(OutputDir, "solar_map.bin");
        private static readonly string CheckpointFile = Path.Combine(OutputDir, "checkpoint.txt");
        private static readonly string ResultsCsvFile = Path.Combine(OutputDir, "hits_results.csv");

        private static readonly double[] Masses = {
            1.98841e30, // Sun
            1.89819e27, // Jupiter (599)
            5.6834e26,  // Saturn Barycenter
            8.6813e25,  // Uranus Barycenter
            1.02413e26, // Neptune (без Тритона)
            2.29e19     // 16 Psyche
        };

        // Жесткий радиус захвата/удара (5 млн км) для отсечения случайных дальних пролетов и снижения нагрузки на ОЗУ
        private static readonly double CaptureRadiusMeters = 5.0e9;

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // Переводим процесс в тихий фоновый режим (ниже среднего), чтобы не мешать основной работе
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
            Directory.CreateDirectory(OutputDir);

            Console.WriteLine("=== ФОНОВЫЙ БАЛЛИСТИЧЕСКИЙ РЕШАТЕЛЬ (i7-8700 | .NET 4.7.2) ===");
            Console.WriteLine($"Каталог сохранения данных: {OutputDir}");
            Console.WriteLine("Приоритет процесса: BelowNormal (тихий режим).\n");

            double maxYears = 1_000_000.0;
            double dtBack = -86400.0; // -1 день
            int recordEverySteps = 50; // срез каждые 50 дней (размер в ОЗУ: ~1.05 ГБ)
            long totalBackSteps = (long)(maxYears * 365.25);
            int historyLen = (int)(totalBackSteps / recordEverySteps) + 1;

            Vec3[,] historyPos = new Vec3[historyLen, N];
            Vec3[,] historyVel = new Vec3[historyLen, N];

            // ЭТАП 1: Загрузка или построение опорной карты Солнечной системы
            if (File.Exists(MapCacheFile))
            {
                Console.WriteLine("Найден кэш опорной карты на диске D:. Загрузка в ОЗУ...");
                LoadMapFromDisk(MapCacheFile, historyPos, historyVel, historyLen);
                Console.WriteLine("Опорная карта мгновенно загружена!\n");
            }
            else
            {
                Console.WriteLine($"Построение опорной карты на {maxYears:N0} лет (один раз, с сохранением на диск D:)...");
                Stopwatch mapSw = Stopwatch.StartNew();
                BuildAndSaveMap(MapCacheFile, historyPos, historyVel, totalBackSteps, recordEverySteps, historyLen, dtBack);
                Console.WriteLine($"Карта построена и сохранена за {mapSw.Elapsed.TotalSeconds:F1} сек.\n");
            }

            // Подготовка CSV-файла результатов (если его еще нет)
            if (!File.Exists(ResultsCsvFile))
            {
                File.WriteAllText(ResultsCsvFile, "ExplosionYearsAgo;PlanetsHitCount;OriginX_MlnKm;OriginY_MlnKm;OriginZ_MlnKm;Details\n", Encoding.UTF8);
            }

            // ЭТАП 2: Многопоточный веерный поиск с сохранением чекпоинтов
            int startIdx = (int)((300_000.0 * 365.25) / recordEverySteps);
            int endIdx = historyLen - (int)((40.0 * 365.25) / recordEverySteps);
            int epochStride = 36; // шаг сканирования: ~5 лет (вместо 2 556 604 эпох станет ~142 000)
            int totalIterations = (endIdx - startIdx) / epochStride;

            // Проверяем, есть ли сохраненный прогресс с прошлого запуска
            int startIter = 0;
            if (File.Exists(CheckpointFile) && int.TryParse(File.ReadAllText(CheckpointFile).Trim(), out int savedIter))
            {
                startIter = savedIter;
                Console.WriteLine($"Возобновление расчета с сохраненной отметки: итерация {startIter} из {totalIterations} ({(double)startIter / totalIterations * 100:F1}%).\n");
            }

            // Используем 8 потоков из 12 на i7-8700, чтобы ПК работал тихо и оставался отзывчивым
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 8 };
            object fileLock = new object();

            double[] ejectionSpeeds = { 8000, 12000, 16000, 20000, 24000, 28000 };
            int angleSteps = 72;
            double capRadSq = CaptureRadiusMeters * CaptureRadiusMeters;

            int completedCount = startIter;
            Stopwatch searchSw = Stopwatch.StartNew();
            long lastReportMs = 0;

            Console.WriteLine("Запуск веерного поиска (8 потоков). Прогресс автоматически сохраняется на диск D:...\n");

            Parallel.For(startIter, totalIterations, parallelOptions, iter =>
            {
                int idx = startIdx + iter * epochStride;
                Vec3 originPos = historyPos[idx, PSYCHE];
                Vec3 originVel = historyVel[idx, PSYCHE];
                double yearsAgo = (idx * recordEverySteps) / 365.25;

                List<HitResult> hitsInThisEpoch = null;

                for (int a = 0; a < angleSteps; a++)
                {
                    double theta = (2.0 * Math.PI * a) / angleSteps;
                    double cosT = Math.Cos(theta);
                    double sinT = Math.Sin(theta);

                    foreach (double vEj in ejectionSpeeds)
                    {
                        Vec3 fragPos = originPos;
                        Vec3 fragVel = new Vec3(
                            originVel.X + vEj * cosT,
                            originVel.Y + vEj * sinT,
                            originVel.Z
                        );

                        double dtFlight = 345600.0; // +4 дня
                        int maxFlightSteps = (int)((25.0 * 365.25) / 4.0);

                        for (int fStep = 1; fStep <= maxFlightSteps; fStep++)
                        {
                            int currentHistIdx = idx - (fStep * 4) / recordEverySteps;
                            if (currentHistIdx < 0) break;

                            fragPos = StepFragmentRK4(fragPos, ref fragVel, historyPos, currentHistIdx, dtFlight);

                            for (int p = SATURN; p <= NEPTUNE; p++)
                            {
                                double distSq = (fragPos - historyPos[currentHistIdx, p]).LengthSq();
                                if (distSq < capRadSq)
                                {
                                    if (hitsInThisEpoch == null) hitsInThisEpoch = new List<HitResult>(3);
                                    string pName = p == SATURN ? "Сатурн" : (p == URANUS ? "Уран" : "Нептун");

                                    if (!hitsInThisEpoch.Any(h => h.TargetName == pName))
                                    {
                                        hitsInThisEpoch.Add(new HitResult
                                        {
                                            ExplosionYearsAgo = yearsAgo,
                                            ExplosionCoordsSun = originPos - historyPos[idx, SUN],
                                            EjectionSpeedKmS = vEj / 1000.0,
                                            TargetName = pName,
                                            FlightYears = (fStep * 4.0) / 365.25,
                                            MissDistanceKm = Math.Sqrt(distSq) / 1000.0
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                // Если поражены минимум 2 планеты из одной точки — сразу пишем в CSV на диск D:
                if (hitsInThisEpoch != null && hitsInThisEpoch.Count >= 2)
                {
                    var first = hitsInThisEpoch[0];
                    string details = string.Join(" | ", hitsInThisEpoch.Select(h => $"{h.TargetName}: V={h.EjectionSpeedKmS:F0}км/с, T={h.FlightYears:F1}л, Промах={h.MissDistanceKm / 1e6:F2}млн.км"));
                    string csvLine = string.Format(CultureInfo.InvariantCulture,
                        "{0:F2};{1};{2:F2};{3:F2};{4:F2};{5}\n",
                        first.ExplosionYearsAgo, hitsInThisEpoch.Count,
                        first.ExplosionCoordsSun.X / 1e9, first.ExplosionCoordsSun.Y / 1e9, first.ExplosionCoordsSun.Z / 1e9,
                        details);

                    lock (fileLock)
                    {
                        File.AppendAllText(ResultsCsvFile, csvLine, Encoding.UTF8);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[НАЙДЕНО СОВПАДЕНИЕ!] {first.ExplosionYearsAgo:N1} лет назад -> {details}");
                        Console.ResetColor();
                    }
                }
                int done = Interlocked.Increment(ref completedCount);
                // Каждые 50 итераций сохраняем чекпоинт и обновляем статус в консоли
                if (done % 50 == 0)
                {
                    lock (fileLock)
                    {
                        // Записываем минимальный гарантированно пройденный рубеж (с запасом на параллельные потоки)
                        int safeCheckpoint = Math.Max(startIter, done - 16);
                        File.WriteAllText(CheckpointFile, safeCheckpoint.ToString());
                        if (searchSw.ElapsedMilliseconds - lastReportMs >= 5000)
                        {
                            lastReportMs = searchSw.ElapsedMilliseconds;
                            double pct = (double)done / totalIterations * 100.0;
                            double elapsedMin = searchSw.Elapsed.TotalMinutes;
                            int sessionDone = done - startIter;
                            double etaMin = sessionDone > 0 ? (elapsedMin / sessionDone) * (totalIterations - done) : 0;
                            Console.WriteLine($"Прогресс: {pct,5:F1}% ({done}/{totalIterations}) | Прошло: {elapsedMin:F1} мин | Осталось: ~{etaMin:F1} мин");
                        }
                    }
                }
            });
            File.WriteAllText(CheckpointFile, totalIterations.ToString());
            Console.WriteLine("\n=== ПОИСК ПОЛНОСТЬЮ ЗАВЕРШЕН ===");
            Console.WriteLine($"Все найденные события сохранены в файл: {ResultsCsvFile}");
        }
        private static void BuildAndSaveMap(string path, Vec3[,] hPos, Vec3[,] hVel, long totalSteps, int recEvery, int hLen, double dt)
        {
            Vec3[] pos = new Vec3[N]
            {
new Vec3(-1.504607374351569e8, -7.530129189757679e8, 1.338692538323492e7),
new Vec3(-5.560365363682998e11, 5.686542190896240e11, 1.008528715757149e10),
new Vec3(1.376501030917742e12, 2.961706883621369e11, -5.995691056356573e10),
new Vec3(1.314712586684628e12, 2.592200337094818e12, -7.405246523543715e9),
new Vec3(4.462422379863634e12, 2.245729891671212e11, -1.074657797458610e11),
new Vec3(-3.745914596673930e11, 2.742036684832305e11, -2.725299623958126e9)
            };
            Vec3[] vel = new Vec3[N]
            {
new Vec3(10.24208825506264, 4.931312178683457, -0.2235574920994247),
new Vec3(-9500.542584319556, -8521.224732288195, 247.8973752653122),
new Vec3(-2561.540027099157, 9422.420086656416, -61.88395992239126),
new Vec3(-6123.699747115159, 2762.946908531664, 89.58894236319392),
new Vec3(-308.9118299114407, 5460.637094016144, -105.3339609654929),
new Vec3(-11200.08852447988, -11900.59061065919, 862.3525930782749)
            };
            for (int i = 0; i < N; i++) { hPos[0, i] = pos[i]; hVel[0, i] = vel[i]; }
            int recIdx = 1;
            for (long step = 1; step <= totalSteps; step++)
            {
                StepRK4(pos, vel, dt);
                if (step % recEvery == 0 && recIdx < hLen)
                {
                    for (int i = 0; i < N; i++) { hPos[recIdx, i] = pos[i]; hVel[recIdx, i] = vel[i]; }
                    recIdx++;
                }
            }
            using (var bw = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                for (int r = 0; r < hLen; r++)
                {
                    for (int i = 0; i < N; i++)
                    {
                        bw.Write(hPos[r, i].X); bw.Write(hPos[r, i].Y); bw.Write(hPos[r, i].Z);
                        bw.Write(hVel[r, i].X); bw.Write(hVel[r, i].Y); bw.Write(hVel[r, i].Z);
                    }
                }
            }
        }
        private static void LoadMapFromDisk(string path, Vec3[,] hPos, Vec3[,] hVel, int hLen)
        {
            using (var br = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                for (int r = 0; r < hLen; r++)
                {
                    for (int i = 0; i < N; i++)
                    {
                        hPos[r, i] = new Vec3(br.ReadDouble(), br.ReadDouble(), br.ReadDouble());
                        hVel[r, i] = new Vec3(br.ReadDouble(), br.ReadDouble(), br.ReadDouble());
                    }
                }
            }
        }
        private static void StepRK4(Vec3[] p, Vec3[] v, double dt)
        {
            Vec3[] a1 = AccelSystem(p);
            Vec3[] p2 = new Vec3[N], v2 = new Vec3[N];
            for (int i = 0; i < N; i++) { p2[i] = p[i] + v[i] * (dt * 0.5); v2[i] = v[i] + a1[i] * (dt * 0.5); }
            Vec3[] a2 = AccelSystem(p2);
            Vec3[] p3 = new Vec3[N], v3 = new Vec3[N];
            for (int i = 0; i < N; i++) { p3[i] = p[i] + v2[i] * (dt * 0.5); v3[i] = v[i] + a2[i] * (dt * 0.5); }
            Vec3[] a3 = AccelSystem(p3);
            Vec3[] p4 = new Vec3[N], v4 = new Vec3[N];
            for (int i = 0; i < N; i++) { p4[i] = p[i] + v3[i] * dt; v4[i] = v[i] + a3[i] * dt; }
            Vec3[] a4 = AccelSystem(p4);
            double dt6 = dt / 6.0;
            for (int i = 0; i < N; i++)
            {
                p[i] += (v[i] + v2[i] * 2.0 + v3[i] * 2.0 + v4[i]) * dt6;
                v[i] += (a1[i] + a2[i] * 2.0 + a3[i] * 2.0 + a4[i]) * dt6;
            }
        }
        private static Vec3[] AccelSystem(Vec3[] p)
        {
            Vec3[] a = new Vec3[N];
            for (int i = 0; i < N - 1; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    Vec3 d = p[j] - p[i];
                    double rSq = d.LengthSq();
                    if (rSq < 1e12) rSq = 1e12;
                    double invR3 = G / (rSq * Math.Sqrt(rSq));
                    a[i] += d * (Masses[j] * invR3);
                    a[j] -= d * (Masses[i] * invR3);
                }
            }
            return a;
        }
        private static Vec3 StepFragmentRK4(Vec3 pos, ref Vec3 vel, Vec3[,] histPos, int hIdx, double dt)
        {
            Vec3 a1 = AccelFragment(pos, histPos, hIdx);
            Vec3 v2 = vel + a1 * (dt * 0.5);
            Vec3 a2 = AccelFragment(pos + vel * (dt * 0.5), histPos, hIdx);
            Vec3 v3 = vel + a2 * (dt * 0.5);
            Vec3 a3 = AccelFragment(pos + v2 * (dt * 0.5), histPos, hIdx);
            Vec3 v4 = vel + a3 * dt;
            Vec3 a4 = AccelFragment(pos + v3 * dt, histPos, hIdx);
            double dt6 = dt / 6.0;
            Vec3 nextPos = pos + (vel + v2 * 2.0 + v3 * 2.0 + v4) * dt6;
            vel += (a1 + a2 * 2.0 + a3 * 2.0 + a4) * dt6;
            return nextPos;
        }
        private static Vec3 AccelFragment(Vec3 fPos, Vec3[,] histPos, int hIdx)
        {
            Vec3 acc = new Vec3(0, 0, 0);
            for (int j = 0; j < 5; j++)
            {
                Vec3 d = histPos[hIdx, j] - fPos;
                double rSq = d.LengthSq();
                if (rSq < 1e12) rSq = 1e12;
                acc += d * (G * Masses[j] / (rSq * Math.Sqrt(rSq)));
            }
            return acc;
        }
    }
}