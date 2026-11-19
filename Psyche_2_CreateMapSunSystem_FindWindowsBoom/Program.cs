using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PsycheBallisticSolver
{
    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double d) => new Vec3(a.X * d, a.Y * d, a.Z * d);
        public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
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
        private const int N = 6; // Sun, Jupiter, Saturn, Uranus, Neptune, Psyche

        private const int SUN = 0;
        private const int JUPITER = 1;
        private const int SATURN = 2;
        private const int URANUS = 3;
        private const int NEPTUNE = 4;
        private const int PSYCHE = 5;

        // Массы тел (кг)
        private static readonly double[] Masses = {
            1.98841e30, // Sun
            1.89819e27, // Jupiter (599)
            5.6834e26,  // Saturn Barycenter (6)
            8.6813e25,  // Uranus Barycenter (7)
            1.02413e26, // Neptune (8, без Тритона)
            2.29e19     // 16 Psyche
        };

        // Радиусы захвата / Сферы Хилла (в метрах) для фиксации попадания
        private static readonly double[] CaptureRadii = {
            0,          // Sun
            5.31e10,    // Jupiter Hill radius (~53.1 млн км)
            6.50e10,    // Saturn Hill radius (~65.0 млн км)
            7.00e10,    // Uranus Hill radius (~70.0 млн км)
            1.16e11     // Neptune Hill radius (~116.0 млн км)
        };

        static void Main(string[] args)
        {
            Console.WriteLine("=== БАЛЛИСТИЧЕСКИЙ РЕШАТЕЛЬ КАТАСТРОФЫ ФАЭТОНА (12 ПОТОКОВ) ===");
            Stopwatch totalSw = Stopwatch.StartNew();

            // 1. Начальные координаты (м) на эпоху 2026-Nov-10 00:00 TDB (NASA Horizons)
            Vec3[] pos = new Vec3[N]
            {
                new Vec3(-1.504607374351569e8, -7.530129189757679e8, 1.338692538323492e7),   // Sun
                new Vec3(-5.560365363682998e11, 5.686542190896240e11, 1.008528715757149e10), // Jupiter (599)
                new Vec3(1.376501030917742e12, 2.961706883621369e11, -5.995691056356573e10), // Saturn Barycenter
                new Vec3(1.314712586684628e12, 2.592200337094818e12, -7.405246523543715e9),  // Uranus Barycenter
                new Vec3(4.462422379863634e12, 2.245729891671212e11, -1.074657797458610e11), // Neptune
                new Vec3(-3.745914596673930e11, 2.742036684832305e11, -2.725299623958126e9)  // 16 Psyche
            };

            // Начальные скорости (м/с) на эпоху 2026-Nov-10 00:00 TDB (NASA Horizons)
            Vec3[] vel = new Vec3[N]
            {
                new Vec3(10.24208825506264, 4.931312178683457, -0.2235574920994247),
                new Vec3(-9500.542584319556, -8521.224732288195, 247.8973752653122),
                new Vec3(-2561.540027099157, 9422.420086656416, -61.88395992239126),
                new Vec3(-6123.699747115159, 2762.946908531664, 89.58894236319392),
                new Vec3(-308.9118299114407, 5460.637094016144, -105.3339609654929),
                new Vec3(-11200.08852447988, -11900.59061065919, 862.3525930782749)
            };

            // ЭТАП 1: Запись опорных траекторий планет и Психеи на 1 000 000 лет назад
            // Шаг интегратора: -1 сутки (-86400 с). Запись в историю: каждые 10 суток.
            double maxYears = 1_000_000.0;
            double dtBack = -86400.0; // -1 день
            int recordEverySteps = 10; // Записываем срез каждые 10 дней
            long totalBackSteps = (long)((maxYears * 365.25) / 1.0);
            int historyLen = (int)(totalBackSteps / recordEverySteps) + 1;

            Console.WriteLine($"Этап 1: Построение опорной карты Солнечной системы на {maxYears:N0} лет ({historyLen:N0} срезов)...");
            Vec3[,] historyPos = new Vec3[historyLen, N];
            Vec3[,] historyVel = new Vec3[historyLen, N];

            for (int i = 0; i < N; i++) { historyPos[0, i] = pos[i]; historyVel[0, i] = vel[i]; }

            int recIdx = 1;
            for (long step = 1; step <= totalBackSteps; step++)
            {
                StepRK4(pos, vel, dtBack);
                if (step % recordEverySteps == 0 && recIdx < historyLen)
                {
                    for (int i = 0; i < N; i++) { historyPos[recIdx, i] = pos[i]; historyVel[recIdx, i] = vel[i]; }
                    recIdx++;
                }
            }
            Console.WriteLine($"Опорная карта построена за {totalSw.Elapsed.TotalSeconds:F1} сек.\n");

            // ЭТАП 2: Многопоточный веерный запуск осколков в окне от 300 000 до 1 000 000 лет назад
            int startIdx = (int)((300_000.0 * 365.25) / recordEverySteps);
            int endIdx = historyLen - (int)((40.0 * 365.25) / recordEverySteps); // запас 40 лет на полет осколка

            // Сканируем эпохи с шагом 100 дней (каждый 10-й срез) для быстрого первичного поиска
            int epochStride = 10;
            var synchronizedEvents = new ConcurrentDictionary<int, List<HitResult>>();

            // Параметры веера выброса при взрыве: скорости от 8 до 28 км/с, 72 угла (каждые 5 градусов)
            double[] ejectionSpeeds = { 8000, 12000, 16000, 20000, 24000, 28000 };
            int angleSteps = 72;

            Console.WriteLine("Этап 2: Запуск веерного баллистического поиска на всех ядрах процессора...");
            Stopwatch fanSw = Stopwatch.StartNew();

            Parallel.For(0, (endIdx - startIdx) / epochStride, iter =>
            {
            int idx = startIdx + iter * epochStride;
            Vec3 originPos = historyPos[idx, PSYCHE];
            Vec3 originVel = historyVel[idx, PSYCHE];
            double yearsAgo = (idx * recordEverySteps) / 365.25;

            List<HitResult> hitsInThisEpoch = null;

            // Веер по направлениям (в плоскости орбиты и с небольшим наклоном по Z)
            for (int a = 0; a < angleSteps; a++)
            {
                double theta = (2.0 * Math.PI * a) / angleSteps;
                double cosT = Math.Cos(theta);
                double sinT = Math.Sin(theta);

                foreach (double vEj in ejectionSpeeds)
                {
                    // Вектор скорости осколка = орбитальная скорость Психеи + импульс взрыва
                    Vec3 fragPos = originPos;
                    Vec3 fragVel = new Vec3(
                        originVel.X + vEj * cosT,
                        originVel.Y + vEj * sinT,
                        originVel.Z // Можно варьировать наклон для точного попадания в Нептун
                    );

                    // Моделируем полет осколка вперед во времени до 30 лет (шаг +2 суток = 172800 с)
                    double dtFlight = 172800.0; // +2 дня
                    int maxFlightSteps = (int)((30.0 * 365.25) / 2.0);

                    for (int fStep = 1; fStep <= maxFlightSteps; fStep++)
                    {
                        // Интерполяция индекса опорной карты (движение вперед по времени = уменьшение idx)
                        int currentHistIdx = idx - (fStep * 2) / recordEverySteps;
                        if (currentHistIdx < 0) break;

                        // Шаг полета осколка в поле Солнца и планет-гигантов
                        fragPos = StepFragmentRK4(fragPos, ref fragVel, historyPos, currentHistIdx, dtFlight);

                        // Проверяем попадание в сферы Хилла Сатурна (2), Урана (3) и Нептуна (4)
                        for (int p = SATURN; p <= NEPTUNE; p++)
                        {
                            double distSq = (fragPos - historyPos[currentHistIdx, p]).LengthSq();
                            if (distSq < CaptureRadii[p] * CaptureRadii[p])
                            {
                                if (hitsInThisEpoch == null) hitsInThisEpoch = new List<HitResult>();
                                string pName = p == SATURN ? "Сатурн" : (p == URANUS ? "Уран" : "Нептун");

                                // Записываем первое попадание по этой планете в данной эпохе
                                if (!hitsInThisEpoch.Any(h => h.TargetName == pName))
                                {
                                    hitsInThisEpoch.Add(new HitResult
                                    {
                                        ExplosionYearsAgo = yearsAgo,
                                        ExplosionCoordsSun = originPos - historyPos[idx, SUN],
                                        EjectionSpeedKmS = vEj / 1000.0,
                                        TargetName = pName,
                                        FlightYears = (fStep * 2.0) / 365.25,
                                        MissDistanceKm = Math.Sqrt(distSq) / 1000.0
                                    });
                                }
                            }
                        }
                    }
                }
            }

                // Нас интересуют только те моменты, когда из ОДНОЙ точки поражены минимум 2 гиганта!
                if (hitsInThisEpoch != null && hitsInThisEpoch.Count >= 2)
                {
                    synchronizedEvents[idx] = hitsInThisEpoch;
                }
            });
            fanSw.Stop();
            Console.WriteLine($"Баллистический поиск завершен за {fanSw.Elapsed.TotalSeconds:F1} сек!\n");
            // Вывод результатов
            var topEvents = synchronizedEvents.Values
            .OrderByDescending(list => list.Count)
            .ThenBy(list => list.Average(h => h.MissDistanceKm))
            .Take(10)
            .ToList();
            Console.WriteLine($"=== НАЙДЕНО СИНХРОННЫХ ОКН РАЗЛЕТА: {synchronizedEvents.Count} ===");
            foreach (var ev in topEvents)
            {
                var first = ev[0];
                Console.WriteLine($"\n[ДАТА ВЗРЫВА: {first.ExplosionYearsAgo:N2} лет назад] | Поражено планет: {ev.Count}");
                Console.WriteLine($"Координаты (отн. Солнца): X={first.ExplosionCoordsSun.X / 1e9:F2}, Y={first.ExplosionCoordsSun.Y / 1e9:F2}, Z={first.ExplosionCoordsSun.Z / 1e9:F2} млн км");
                foreach (var h in ev)
                {
                    Console.WriteLine($"   -> Цель: {h.TargetName,-7} | Скорость выброса: {h.EjectionSpeedKmS,4:F1} км/с | Время полета: {h.FlightYears,5:F2} лет | Промах от центра: {h.MissDistanceKm / 1e6,6:F2} млн км");
                }
            }
        }
        // RK4 для опорных тел (6 тел)
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
        // RK4 для пробного осколка в гравитационном поле Солнца и планет-гигантов
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
            for (int j = 0; j < 5; j++) // Учитываем гравитацию Солнца, Юпитера, Сатурна, Урана и Нептуна
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