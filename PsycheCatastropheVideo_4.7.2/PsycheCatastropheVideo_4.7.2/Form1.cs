using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PsycheCatastropheVideo_4._7._2
{
    public partial class Form1 : Form
    {
        private const double G = 6.67430e-11;
        private const double AU_M = 149597870700.0;
        private const int N_MAP = 6;

        private const int SUN = 0, JUPITER = 1, SATURN = 2, URANUS = 3, NEPTUNE = 4, PSYCHE = 5;
        private static readonly double[] Masses = { 1.98841e30, 1.89819e27, 5.6834e26, 8.6813e25, 1.02413e26, 2.29e19 };

        private double[,] hx, hy, hz, hvx, hvy, hvz;
        private int historyLen, blastSliceIdx;
        private int recordEverySteps = 50;

        private const int PRE_DAYS = 900, POST_DAYS = 2000;
        private const int TOTAL_DAYS = PRE_DAYS + POST_DAYS + 1;

        private double[,] planetX = new double[TOTAL_DAYS, N_MAP];
        private double[,] planetY = new double[TOTAL_DAYS, N_MAP];
        private double[] earthX = new double[TOTAL_DAYS];
        private double[] earthY = new double[TOTAL_DAYS];

        private double[,] fragX = new double[TOTAL_DAYS, 3];
        private double[,] fragY = new double[TOTAL_DAYS, 3];
        private bool[,] fragCaptured = new bool[TOTAL_DAYS, 3];

        private int dayBlast = PRE_DAYS;
        private int dayHitSaturn = PRE_DAYS + 1058;  // +2.896 yr
        private int dayHitUranus = PRE_DAYS + 1171;  // +3.206 yr
        private int dayHitNeptune = PRE_DAYS + 1564; // +4.282 yr

        private System.Windows.Forms.Timer animTimer;
        private double currentDay = 0, simSpeedDaysPerTick = 2.0, zoomAu = 33.5;
        private double camCenterX_AU = -4.0, camCenterY_AU = 4.0;
        private bool isPaused = false, isLightTheme = false, isRecording = false, isDragging = false;
        private int followTarget = -1, frameCounter = 0; // -2 = Earth
        private string recordFolder = @"D:\Zimnicky\Моё\Психа\MPML_Video_Frames";
        private Point lastMouse;

        public Form1()
        {
            this.Text = "Main Belt Planetary Disruption Reconstruction (~649,187.7 yr ago) — Earth, Psyche, Saturn, Uranus, Neptune";
            this.Size = new Size(1380, 860);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(8, 10, 18);

            SetupUI();
            if (!LoadAndSimulateData())
            {
                MessageBox.Show("File solar_map.bin not found in D:\\Zimnicky\\Моё\\Психа or H:\\Psyche!", "Error");
                return;
            }

            this.MouseWheel += (s, e) => {
                zoomAu = Math.Max(0.3, Math.Min(60.0, zoomAu * (e.Delta > 0 ? 0.85 : 1.18)));
                this.Invalidate();
            };
            this.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { followTarget = -1; isDragging = true; lastMouse = e.Location; } };
            this.MouseMove += (s, e) => {
                if (!isDragging) return;
                double scale = Math.Min(this.ClientSize.Width, this.ClientSize.Height - 44) / (2.0 * zoomAu);
                camCenterX_AU -= (e.X - lastMouse.X) / scale;
                camCenterY_AU += (e.Y - lastMouse.Y) / scale;
                lastMouse = e.Location;
                this.Invalidate();
            };
            this.MouseUp += (s, e) => isDragging = false;

            animTimer = new System.Windows.Forms.Timer { Interval = 25 };
            animTimer.Tick += AnimTimer_Tick;
            animTimer.Start();
        }

        private void SetupUI()
        {
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(22, 26, 42) };

            AddBtn(topPanel, "Play / Pause", 10, 95, (s, e) => isPaused = !isPaused);
            AddBtn(topPanel, "Restart", 110, 75, (s, e) => { currentDay = 0; isPaused = false; });
            AddBtn(topPanel, "Theme: Light/Dark", 190, 125, (s, e) => {
                isLightTheme = !isLightTheme;
                this.BackColor = isLightTheme ? Color.White : Color.FromArgb(8, 10, 18);
                this.Invalidate();
            });
            AddBtn(topPanel, "Save figure1.jpg", 320, 120, (s, e) => SaveFigure1Jpg());
            AddBtn(topPanel, "Record Frames", 445, 110, (s, e) => ToggleRecording());

            AddBtn(topPanel, "Full View", 575, 85, (s, e) => { followTarget = -1; camCenterX_AU = -4.0; camCenterY_AU = 4.0; zoomAu = 33.5; });
            AddBtn(topPanel, "Earth", 665, 65, (s, e) => { followTarget = -2; zoomAu = 4.5; });
            AddBtn(topPanel, "Psyche", 735, 70, (s, e) => { followTarget = PSYCHE; zoomAu = 4.5; });
            AddBtn(topPanel, "Saturn", 810, 70, (s, e) => { followTarget = SATURN; zoomAu = 2.2; });
            AddBtn(topPanel, "Uranus", 885, 70, (s, e) => { followTarget = URANUS; zoomAu = 2.0; });
            AddBtn(topPanel, "Neptune", 960, 75, (s, e) => { followTarget = NEPTUNE; zoomAu = 2.2; });

            this.Controls.Add(topPanel);
        }

        private void AddBtn(Panel p, string text, int left, int width, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                Left = left,
                Top = 8,
                Width = width,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 52, 80),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            btn.Click += onClick;
            p.Controls.Add(btn);
        }

        private void SaveFigure1Jpg()
        {
            string outDir = Directory.Exists(@"D:\Zimnicky\Моё\Психа") ? @"D:\Zimnicky\Моё\Психа" : @"H:\Psyche";
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "figure1.jpg");

            using (Bitmap bmp = new Bitmap(this.ClientSize.Width, this.ClientSize.Height - 44))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.TranslateTransform(0, -44);
                RenderScene(g, true);
                bmp.Save(path, ImageFormat.Jpeg);
            }
            MessageBox.Show($"Publication figure saved to:\n{path}", "Saved");
        }
        private void ToggleRecording()
        {
            if (!isRecording)
            {
                string baseDir = Directory.Exists(@"D:\Zimnicky\Моё\Психа") ? @"D:\Zimnicky\Моё\Психа" : @"H:\Psyche";
                recordFolder = Path.Combine(baseDir, "MPML_Video_Frames");
                Directory.CreateDirectory(recordFolder);
                frameCounter = 0; currentDay = 0; isPaused = false; isRecording = true;
                MessageBox.Show("Recording started from T = -2.46 yr! Click again to stop or wait until the end.", "Recording");
            }
            else
            {
                isRecording = false;
                MessageBox.Show($"Saved {frameCounter / 2} frames (960x540) to:\n{recordFolder}", "Recording Complete");
            }
        }

        private bool LoadAndSimulateData()
        {
            string mapFile = @"D:\Zimnicky\Моё\Психа\solar_map.bin";
            if (!File.Exists(mapFile)) mapFile = @"H:\Psyche\solar_map.bin";
            if (!File.Exists(mapFile)) return false;

            long totalBackSteps = (long)(1_000_000.0 * 365.25);
            historyLen = (int)(totalBackSteps / recordEverySteps) + 1;

            hx = new double[historyLen, N_MAP]; hy = new double[historyLen, N_MAP]; hz = new double[historyLen, N_MAP];
            hvx = new double[historyLen, N_MAP]; hvy = new double[historyLen, N_MAP]; hvz = new double[historyLen, N_MAP];

            using (var br = new BinaryReader(File.Open(mapFile, FileMode.Open)))
            {
                for (int r = 0; r < historyLen; r++)
                    for (int i = 0; i < N_MAP; i++)
                    {
                        hx[r, i] = br.ReadDouble(); hy[r, i] = br.ReadDouble(); hz[r, i] = br.ReadDouble();
                        hvx[r, i] = br.ReadDouble(); hvy[r, i] = br.ReadDouble(); hvz[r, i] = br.ReadDouble();
                    }
            }

            blastSliceIdx = (int)((649187.75 * 365.25) / recordEverySteps);

            for (int d = 0; d < TOTAL_DAYS; d++)
            {
                double exactSlice = blastSliceIdx - ((d - PRE_DAYS) / (double)recordEverySteps);
                int h0 = (int)exactSlice; double frac = exactSlice - h0;
                double sunX = hx[h0, SUN] + (hx[h0 + 1, SUN] - hx[h0, SUN]) * frac;
                double sunY = hy[h0, SUN] + (hy[h0 + 1, SUN] - hy[h0, SUN]) * frac;

                for (int p = 0; p < N_MAP; p++)
                {
                    planetX[d, p] = ((hx[h0, p] + (hx[h0 + 1, p] - hx[h0, p]) * frac) - sunX) / AU_M;
                    planetY[d, p] = ((hy[h0, p] + (hy[h0 + 1, p] - hy[h0, p]) * frac) - sunY) / AU_M;
                }
            }

            // Вычисление точного положения Земли на эпоху ~649 187.75 лет назад от вектора NASA JPL DE441 (2026-Nov-10)
            SimulateEarthPosition();

            SimulateFragment(0, 8300.0, 148.94, -3.00, SATURN, dayHitSaturn);
            SimulateFragment(1, 19000.0, 138.09, 0.41, URANUS, dayHitUranus);
            SimulateFragment(2, 24000.0, 145.15, 0.01, NEPTUNE, dayHitNeptune);
            return true;
        }

        private void SimulateEarthPosition()
        {
            // Начальный барицентрический вектор Земли на 2026-Nov-10 00:00:00 TDB (NASA JPL DE441)
            double ex = 9.81124512e10, ey = 1.11458912e11, ez = -3.451208e7;
            double evx = -22758.42, evy = 19412.65, evz = -1.24;

            // Быстрый обратный откат Земли до начала окна анимации (649 190.21 лет назад) с шагом -1 сутки
            long startAnimBackSteps = (long)((649187.75 + (PRE_DAYS / 365.25)) * 365.25);
            double dt = -86400.0, halfDt = dt * 0.5, dt6 = dt / 6.0;

            for (long step = 1; step <= startAnimBackSteps; step++)
            {
                double exactSlice = (double)step / recordEverySteps;
                int h0 = (int)exactSlice;
                if (h0 >= historyLen - 1) break;
                double frac = exactSlice - h0;

                AccelScalar(ex, ey, ez, h0, frac, out double ax1, out double ay1, out double az1);
                double vx2 = evx + ax1 * halfDt, vy2 = evy + ay1 * halfDt, vz2 = evz + az1 * halfDt;
                AccelScalar(ex + evx * halfDt, ey + evy * halfDt, ez + evz * halfDt, h0, frac, out double ax2, out double ay2, out double az2);
                double vx3 = evx + ax2 * halfDt, vy3 = evy + ay2 * halfDt, vz3 = evz + az2 * halfDt;
                AccelScalar(ex + vx2 * halfDt, ey + vy2 * halfDt, ez + vz2 * halfDt, h0, frac, out double ax3, out double ay3, out double az3);
                double vx4 = evx + ax3 * dt, vy4 = evy + ay3 * dt, vz4 = evz + az3 * dt;
                AccelScalar(ex + vx3 * dt, ey + vy3 * dt, ez + vz3 * dt, h0, frac, out double ax4, out double ay4, out double az4);

                ex += (evx + 2 * vx2 + 2 * vx3 + vx4) * dt6; ey += (evy + 2 * vy2 + 2 * vy3 + vy4) * dt6; ez += (evz + 2 * vz2 + 2 * vz3 + vz4) * dt6;
                evx += (ax1 + 2 * ax2 + 2 * ax3 + ax4) * dt6; evy += (ay1 + 2 * ay2 + 2 * ay3 + ay4) * dt6; evz += (az1 + 2 * az2 + 2 * az3 + az4) * dt6;
            }

            // Прямое интегрирование Земли на протяжении всех TOTAL_DAYS кадров анимации
            double dtFwd = 86400.0; halfDt = dtFwd * 0.5; dt6 = dtFwd / 6.0;
            for (int d = 0; d < TOTAL_DAYS; d++)
            {
                double exactSlice = blastSliceIdx - ((d - PRE_DAYS) / (double)recordEverySteps);
                int h0 = (int)exactSlice; double frac = exactSlice - h0;

                double sunX = hx[h0, SUN] + (hx[h0 + 1, SUN] - hx[h0, SUN]) * frac;
                double sunY = hy[h0, SUN] + (hy[h0 + 1, SUN] - hy[h0, SUN]) * frac;
                earthX[d] = (ex - sunX) / AU_M;
                earthY[d] = (ey - sunY) / AU_M;

                AccelScalar(ex, ey, ez, h0, frac, out double ax1, out double ay1, out double az1);
                double vx2 = evx + ax1 * halfDt, vy2 = evy + ay1 * halfDt, vz2 = evz + az1 * halfDt;
                AccelScalar(ex + evx * halfDt, ey + evy * halfDt, ez + evz * halfDt, h0, frac, out double ax2, out double ay2, out double az2);
                double vx3 = evx + ax2 * halfDt, vy3 = evy + ay2 * halfDt, vz3 = evz + az2 * halfDt;
                AccelScalar(ex + vx2 * halfDt, ey + vy2 * halfDt, ez + vz2 * halfDt, h0, frac, out double ax3, out double ay3, out double az3);
                double vx4 = evx + ax3 * dtFwd, vy4 = evy + ay3 * dtFwd, vz4 = evz + az3 * dtFwd;
                AccelScalar(ex + vx3 * dtFwd, ey + vy3 * dtFwd, ez + vz3 * dtFwd, h0, frac, out double ax4, out double ay4, out double az4);

                ex += (evx + 2 * vx2 + 2 * vx3 + vx4) * dt6; ey += (evy + 2 * vy2 + 2 * vy3 + vy4) * dt6; ez += (evz + 2 * vz2 + 2 * vz3 + vz4) * dt6;
                evx += (ax1 + 2 * ax2 + 2 * ax3 + ax4) * dt6; evy += (ay1 + 2 * ay2 + 2 * ay3 + ay4) * dt6; evz += (az1 + 2 * az2 + 2 * az3 + az4) * dt6;
            }
        }
        private void SimulateFragment(int fIdx, double speedMs, double azDeg, double elDeg, int targetPlanet, int exactHitDay)
        {
            double ox = hx[blastSliceIdx, PSYCHE], oy = hy[blastSliceIdx, PSYCHE], oz = hz[blastSliceIdx, PSYCHE];
            double ovx = hvx[blastSliceIdx, PSYCHE], ovy = hvy[blastSliceIdx, PSYCHE], ovz = hvz[blastSliceIdx, PSYCHE];
            double azRad = azDeg * Math.PI / 180.0, elRad = elDeg * Math.PI / 180.0;

            double px = ox, py = oy, pz = oz;
            double vx = ovx + speedMs * Math.Cos(elRad) * Math.Cos(azRad);
            double vy = ovy + speedMs * Math.Cos(elRad) * Math.Sin(azRad);
            double vz = ovz + speedMs * Math.Sin(elRad);

            double dt = 21600.0, halfDt = dt * 0.5, dt6 = dt / 6.0;
            double orbitAngle = 0;

            for (int d = PRE_DAYS; d < TOTAL_DAYS; d++)
            {
                int dayFromBlast = d - PRE_DAYS;
                if (d < exactHitDay)
                {
                    for (int sub = 0; sub < 4; sub++)
                    {
                        double exactSlice = blastSliceIdx - (dayFromBlast + sub * 0.25) / recordEverySteps;
                        int h0 = (int)exactSlice; double frac = exactSlice - h0;
                        AccelScalar(px, py, pz, h0, frac, out double ax1, out double ay1, out double az1);
                        double vx2 = vx + ax1 * halfDt, vy2 = vy + ay1 * halfDt, vz2 = vz + az1 * halfDt;
                        AccelScalar(px + vx * halfDt, py + vy * halfDt, pz + vz * halfDt, h0, frac, out double ax2, out double ay2, out double az2);
                        double vx3 = vx + ax2 * halfDt, vy3 = vy + ay2 * halfDt, vz3 = vz + az2 * halfDt;
                        AccelScalar(px + vx2 * halfDt, py + vy2 * halfDt, pz + vz2 * halfDt, h0, frac, out double ax3, out double ay3, out double az3);
                        double vx4 = vx + ax3 * dt, vy4 = vy + ay3 * dt, vz4 = vz + az3 * dt;
                        AccelScalar(px + vx3 * dt, py + vy3 * dt, pz + vz3 * dt, h0, frac, out double ax4, out double ay4, out double az4);

                        px += (vx + 2 * vx2 + 2 * vx3 + vx4) * dt6; py += (vy + 2 * vy2 + 2 * vy3 + vy4) * dt6; pz += (vz + 2 * vz2 + 2 * vz3 + vz4) * dt6;
                        vx += (ax1 + 2 * ax2 + 2 * ax3 + ax4) * dt6; vy += (ay1 + 2 * ay2 + 2 * ay3 + ay4) * dt6; vz += (az1 + 2 * az2 + 2 * az3 + az4) * dt6;
                    }
                    double sDay = blastSliceIdx - (double)dayFromBlast / recordEverySteps;
                    int hd0 = (int)sDay; double fDay = sDay - hd0;
                    fragX[d, fIdx] = (px - (hx[hd0, SUN] + (hx[hd0 + 1, SUN] - hx[hd0, SUN]) * fDay)) / AU_M;
                    fragY[d, fIdx] = (py - (hy[hd0, SUN] + (hy[hd0 + 1, SUN] - hy[hd0, SUN]) * fDay)) / AU_M;
                    orbitAngle = Math.Atan2(fragY[d, fIdx] - planetY[d, targetPlanet], fragX[d, fIdx] - planetX[d, targetPlanet]);
                }
                else
                {
                    fragCaptured[d, fIdx] = true;
                    if (targetPlanet == URANUS) { fragX[d, fIdx] = planetX[d, URANUS]; fragY[d, fIdx] = planetY[d, URANUS]; }
                    else if (targetPlanet == SATURN) { orbitAngle += 0.29; fragX[d, fIdx] = planetX[d, SATURN] + 0.18 * Math.Cos(orbitAngle); fragY[d, fIdx] = planetY[d, SATURN] + 0.18 * Math.Sin(orbitAngle); }
                    else if (targetPlanet == NEPTUNE) { orbitAngle -= 1.06; fragX[d, fIdx] = planetX[d, NEPTUNE] + 0.16 * Math.Cos(orbitAngle); fragY[d, fIdx] = planetY[d, NEPTUNE] + 0.16 * Math.Sin(orbitAngle); }
                }
            }
        }

        private void AccelScalar(double fx, double fy, double fz, int h0, double frac, out double ax, out double ay, out double az)
        {
            ax = 0; ay = 0; az = 0;
            for (int j = 0; j < 5; j++)
            {
                double dx = (hx[h0, j] + (hx[h0 + 1, j] - hx[h0, j]) * frac) - fx;
                double dy = (hy[h0, j] + (hy[h0 + 1, j] - hy[h0, j]) * frac) - fy;
                double dz = (hz[h0, j] + (hz[h0 + 1, j] - hz[h0, j]) * frac) - fz;
                double rSq = Math.Max(1e10, dx * dx + dy * dy + dz * dz);
                double factor = G * Masses[j] / (rSq * Math.Sqrt(rSq));
                ax += dx * factor; ay += dy * factor; az += dz * factor;
            }
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            if (!isPaused)
            {
                double relDay = currentDay - PRE_DAYS;
                bool slowMo = Math.Abs(relDay) < 25 || Math.Abs(currentDay - dayHitSaturn) < 20 || Math.Abs(currentDay - dayHitUranus) < 25 || Math.Abs(currentDay - dayHitNeptune) < 20;
                currentDay += slowMo ? simSpeedDaysPerTick * 0.35 : simSpeedDaysPerTick;

                if (isRecording && (frameCounter % 2 == 0))
                {
                    using (Bitmap bmp = new Bitmap(this.ClientSize.Width, this.ClientSize.Height - 44))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.TranslateTransform(0, -44);
                        RenderScene(g, false);
                        using (Bitmap small = new Bitmap(bmp, new Size(960, 540)))
                            small.Save(Path.Combine(recordFolder, $"frame_{frameCounter / 2:D4}.jpg"), ImageFormat.Jpeg);
                    }
                }
                frameCounter++;

                if (currentDay >= TOTAL_DAYS - 1) { currentDay = TOTAL_DAYS - 1; isPaused = true; isRecording = false; }
            }
            this.Invalidate();
        }

        private PointF WorldToScreen(double xAu, double yAu)
        {
            double scale = Math.Min(this.ClientSize.Width, this.ClientSize.Height - 44) / (2.0 * zoomAu);
            return new PointF((float)(this.ClientSize.Width * 0.5 + (xAu - camCenterX_AU) * scale),
                              (float)((this.ClientSize.Height + 44) * 0.5 - (yAu - camCenterY_AU) * scale));
        }

        protected override void OnPaint(PaintEventArgs e) => RenderScene(e.Graphics, false);

        private void RenderScene(Graphics g, bool cleanPaperExport)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(isLightTheme ? Color.White : Color.FromArgb(8, 10, 18));

            int d = Math.Min(TOTAL_DAYS - 1, Math.Max(0, (int)currentDay));
            if (followTarget == -2) { camCenterX_AU = earthX[d]; camCenterY_AU = earthY[d]; }
            else if (followTarget >= 0) { camCenterX_AU = planetX[d, followTarget]; camCenterY_AU = planetY[d, followTarget]; }

            Color ringColor = isLightTheme ? Color.FromArgb(55, 80, 80, 80) : Color.FromArgb(40, 180, 180, 200);
            foreach (double r in new[] { 1.00, 2.92, 5.20, 9.58, 19.2, 30.1 })
            {
                PointF pc = WorldToScreen(0, 0);
                float rPx = (float)(r * Math.Min(this.ClientSize.Width, this.ClientSize.Height - 44) / (2.0 * zoomAu));
                if (rPx > 4 && rPx < 9000) using (Pen p = new Pen(ringColor, 1f) { DashStyle = DashStyle.Dot }) g.DrawEllipse(p, pc.X - rPx, pc.Y - rPx, rPx * 2, rPx * 2);
            }

            PointF pSun = WorldToScreen(0, 0);
            g.FillEllipse(isLightTheme ? Brushes.DarkOrange : Brushes.Gold, pSun.X - 6, pSun.Y - 6, 12, 12);
            g.DrawString("Sun", this.Font, isLightTheme ? Brushes.Black : Brushes.Khaki, pSun.X + 8, pSun.Y - 6);

            // Отрисовка Земли (1.0 AU)
            PointF pEarth = WorldToScreen(earthX[d], earthY[d]);
            Color earthCol = isLightTheme ? Color.MediumSeaGreen : Color.LightGreen;
            using (Brush eb = new SolidBrush(earthCol))
            {
                g.FillEllipse(eb, pEarth.X - 4, pEarth.Y - 4, 8, 8);
                if (zoomAu < 25.0) g.DrawString("Earth", this.Font, eb, pEarth.X + 6, pEarth.Y - 6);
            }

            DrawPlanet(g, d, JUPITER, "Jupiter", isLightTheme ? Color.SaddleBrown : Color.SandyBrown, 6, 120, -14);
            DrawPlanet(g, d, SATURN, "Saturn", isLightTheme ? Color.DarkGoldenrod : Color.Khaki, 6, 250, -6);
            DrawPlanet(g, d, URANUS, "Uranus", isLightTheme ? Color.DarkCyan : Color.Cyan, 5, 350, -6);
            DrawPlanet(g, d, NEPTUNE, "Neptune", isLightTheme ? Color.RoyalBlue : Color.CornflowerBlue, 5, 450, -6);

            // Поворот оси Урана на 97.8° после удара
            PointF pUra = WorldToScreen(planetX[d, URANUS], planetY[d, URANUS]);
            double tiltFrac = Math.Max(0.0, Math.Min(1.0, (d - dayHitUranus) / 30.0));
            double angleRad = tiltFrac * (97.8 * Math.PI / 180.0);
            float dxAx = (float)(16 * Math.Sin(angleRad)), dyAx = (float)(16 * Math.Cos(angleRad));
            using (Pen axisPen = new Pen(tiltFrac > 0.1 ? Color.Red : (isLightTheme ? Color.DarkCyan : Color.LightCyan), 2.5f))
                g.DrawLine(axisPen, pUra.X - dxAx, pUra.Y - dyAx, pUra.X + dxAx, pUra.Y + dyAx);

            PointF pPsy = WorldToScreen(planetX[d, PSYCHE], planetY[d, PSYCHE]);
            g.FillEllipse(isLightTheme ? Brushes.DimGray : Brushes.LightGray, pPsy.X - 3, pPsy.Y - 3, 6, 6);
            g.DrawString("16 Psyche (Core)", this.Font, isLightTheme ? Brushes.DimGray : Brushes.LightGray, pPsy.X + 8, pPsy.Y + 4);

            if (d >= dayBlast)
            {
                DrawFrag(g, d, 0, "Hyperion (8.3 km/s)", isLightTheme ? Color.DarkGoldenrod : Color.Yellow, -16);
                if (!fragCaptured[d, 1]) DrawFrag(g, d, 1, "Uranus Impactor (19.0 km/s)", Color.OrangeRed, -4);
                DrawFrag(g, d, 2, "Triton (24.0 km/s)", isLightTheme ? Color.DodgerBlue : Color.DeepSkyBlue, 10);
            }

            DrawEvent(g, d, dayHitSaturn, planetX[d, SATURN], planetY[d, SATURN], "Hyperion Capture (+2.90 yr)", isLightTheme ? Color.DarkGoldenrod : Color.Yellow);
            DrawEvent(g, d, dayHitUranus, planetX[d, URANUS], planetY[d, URANUS], "Grazing Impact: Tilt 97.8° (+3.21 yr)", Color.OrangeRed);
            DrawEvent(g, d, dayHitNeptune, planetX[d, NEPTUNE], planetY[d, NEPTUNE], "Triton Retrograde Capture (+4.28 yr)", isLightTheme ? Color.DodgerBlue : Color.DeepSkyBlue);
            if (!cleanPaperExport)
            {
                double relY = (currentDay - PRE_DAYS) / 365.25;
                Brush hudBrush = isLightTheme ? Brushes.Black : Brushes.White;
                g.DrawString($"T = {relY:+0.00;-0.00} yr | Epoch: ~{(649187.75 - relY):N1} yr ago | Scale: {zoomAu:F1} AU {(isRecording ? "[REC]" : "")}", new Font("Segoe UI", 11f, FontStyle.Bold), hudBrush, 16, 54);
            }
        }
        private void DrawPlanet(Graphics g, int d, int pIdx, string name, Color c, int r, int trail, int yOff)
        {
            int st = Math.Max(0, d - trail);
            if (d - st >= 2) { PointF[] pts = new PointF[d - st + 1]; for (int i = st; i <= d; i++) pts[i - st] = WorldToScreen(planetX[i, pIdx], planetY[i, pIdx]); using (Pen pen = new Pen(Color.FromArgb(110, c), 1.5f)) g.DrawLines(pen, pts); }
            PointF pt = WorldToScreen(planetX[d, pIdx], planetY[d, pIdx]);
            using (Brush b = new SolidBrush(c)) { g.FillEllipse(b, pt.X - r, pt.Y - r, r * 2, r * 2); g.DrawString(name, this.Font, b, pt.X + r + 4, pt.Y + yOff); }
        }
        private void DrawFrag(Graphics g, int d, int fIdx, string name, Color c, int yOff)
        {
            int st = Math.Max(dayBlast, d - 500);
            if (d - st >= 2) { PointF[] pts = new PointF[d - st + 1]; for (int i = st; i <= d; i++) pts[i - st] = WorldToScreen(fragX[i, fIdx], fragY[i, fIdx]); using (Pen pen = new Pen(c, 2f)) g.DrawLines(pen, pts); }
            PointF pt = WorldToScreen(fragX[d, fIdx], fragY[d, fIdx]);
            using (Brush b = new SolidBrush(c)) { g.FillEllipse(b, pt.X - 4, pt.Y - 4, 8, 8); g.DrawString(name, this.Font, b, pt.X + 7, pt.Y + yOff); }
        }
        private void DrawEvent(Graphics g, int d, int evDay, double xAu, double yAu, string text, Color c)
        {
            if (d < evDay || d > evDay + 450) return;
            PointF pt = WorldToScreen(xAu, yAu);
            using (Font f = new Font("Segoe UI", 8.5f, FontStyle.Bold)) using (Brush b = new SolidBrush(c)) g.DrawString(text, f, b, pt.X + 12, pt.Y + 12);
        }
    }
}