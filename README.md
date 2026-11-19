# Testing the Hypothesis of a Former Planet in the Main Asteroid Belt

Numerical N-body (4th-order Runge–Kutta) ballistic reconstruction and interactive WinForms visualization testing whether a synchronous fragmentation event from the orbit of **16 Psyche** (~649,187.7 years ago) simultaneously accounts for Uranus's 97.8° axial obliquity, Triton's retrograde capture at Neptune, and Hyperion's chaotic orbit at Saturn.

- **Preprint & Full Dataset (Zenodo DOI):** [https://doi.org  ](https://doi.org/10.5281/zenodo.24364057)

## Repository Contents
- `PsycheBallisticSolver/` — Backward 1,000,000-year integrator initialized with NASA JPL DE441 ephemerides (epoch 2026-Nov-10 TDB) and 3D sectorial grid search.
- `PsycheSniperShot/` — High-resolution angular refinement (ΔAz = ΔEl = 0.01°) for Uranus and Neptune encounters.
- `PsycheSaturnCheck/` — 360° ballistic scan for Saturn / Hyperion inner-system penetration.
- `PsycheCatastropheVideo/` — Interactive C# WinForms 2D/ecliptic visualization of planetary trajectories and fragment ejection.
- `Results_CSV/` — Output trajectory tables (`sniper_649187_results.csv`, `menu_3D_results.csv`, `asteroids_convergence.csv`, `r631_history.csv`).

## How to Run
1. Open any `.sln` file in Visual Studio (2017 or newer, `.NET Framework 4.7.2+`).
2. Select **`Release`** and **`x64`** build configuration.
3. Run the solver to generate the binary ephemeris cache (`solar_map.bin`), then run the refinement or visualization tools.

# Testing the Hypothesis of a Former Planet in the Main Asteroid Belt: A Numerical Ballistic Reconstruction of Outer Solar System Anomalies

**Author:** Dmitry Zimnitsky  
**Preprint on Zenodo:** ([https://zenodo.org](https://zenodo.org/records/24364057))
**DOI:** [https://doi.org  ](https://doi.org/10.5281/zenodo.24364057)

## Overview
Numerical N-body (4th-order Runge–Kutta) ballistic reconstruction testing whether a fragmentation event from **16 Psyche** accounts for outer Solar System anomalies (Uranus's obliquity, Triton's capture, Hyperion's orbit) using NASA JPL DE441 ephemerides. 

* A full list of the repository contents, launch instructions, and a citation block in BibTeX/Markdown format can be found in the source file at [Zenodo]([https://zenodo.org](https://zenodo.org/records/24364057))

  ## About This Project (Author's Note)

Hello everyone! I am an electrical engineer with a deep passion for nuclear physics and astronomy. For years, I have been intrigued by the question of whether another planet once existed in our Solar System in the distant past. All the clues seemed to point toward it: the Titius–Bode law predicts a planet between Mars and Jupiter, yet there is no planet there—only a belt of rocks and an exposed metallic core, **16 Psyche**.

Skeptics argue that the total mass of the Main Asteroid Belt is far smaller than that of the Moon, and that a metallic core could not simply remain behind. But what if that missing mass was simply lost? In this study, I test the scenario of a massive catastrophic disruption that scattered the planet's outer layers across the Solar System: most of the mass was captured by Jupiter and the Sun, a significant fragment reached Uranus and tilted its rotational axis, and other fragments were captured by Neptune and Saturn. (Triton and Hyperion were chosen because of their loose composition, which points to an origin within the mantle of some planet.)

Using high-precision numerical N-body ballistic modeling (RK4 with NASA JPL DE441 ephemerides) for 16 Psyche, Uranus, Triton, and Hyperion, I identified a highly probable synchronous ejection window **~649,187.7 years ago**. The full preprint is published on Zenodo: https://zenodo.org. The nuclear and physical processes (though not their ultimate cause) that could have provided the energy for such an event will be modeled in a subsequent paper.

---

### О проекте (Заметка автора)

Привет всем! Я инженер-электрик, увлекающийся ядерной физикой и астрономией. Меня всегда терзал вопрос существования ещё одной планеты в далёком прошлом в Солнечной системе. Кажется, что все условия сходятся: правило Тициуса — Боде указывает на орбиту между Марсом и Юпитером, но самой планеты нет — вместо неё есть пояс астероидов и обнажённое металлическое ядро **16 Психея**.

Противники существования этой планеты опираются на то, что суммарная масса пояса астероидов намного меньше массы Луны. Но что, если эта масса была просто потеряна? В своём исследовании я рассматриваю сценарий грандиозного взрыва с разбрасыванием оболочки планеты: большая часть массы была захвачена Юпитером и Солнцем, крупный фрагмент долетел до Урана и вызвал поворот его оси на 97,8°, а другие фрагменты были захвачены Нептуном (Тритон) и Сатурном (Гиперион). (Тритон и Гиперион выбраны по причине рыхлого состава, указывающего на происхождение из мантии какой-то планеты).

С помощью численного баллистического моделирования (метод Рунге — Кутты 4-го порядка по эфемеридам NASA JPL DE441) я определил единое синхронное окно этих событий — **~649 187,7 лет назад**. Полный текст препринта опубликован на Zenodo. Физические и ядерные процессы (но не их первопричину), которые могли дать энергию для этого события, я планирую смоделировать в следующей публикации.


