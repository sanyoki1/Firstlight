# Stress test results (2026-11-13 01:37)

- Machine: AMD Ryzen 7 5700X 8-Core Processor  (16 threads), AMD Radeon RX 7800 XT, 31 GB RAM
- Resolution 1920x1080, vsync off, Windows player build (Mono + Burst), URP with shadows + 4x MSAA
- Map 256x256 cells, 320 flow fields built in 250 ms (parallel Burst Dijkstra)
- Movement every frame for all citizens; decisions/needs staggered over 8 frames

| Citizens | View | FPS | Frame ms (avg) | Frame ms (p99) | Sim ms (avg) | Sim ms (max) | Cull+upload ms | Drawn |
|---|---|---|---|---|---|---|---|---|
| 5,000 | street | 639 | 1.56 | 5.44 | 0.15 | 3.89 | 0.05 | 358 |
| 5,000 | overview | 587 | 1.70 | 6.87 | 0.14 | 9.12 | 0.19 | 4,267 |
| 10,000 | street | 654 | 1.53 | 5.98 | 0.18 | 3.21 | 0.06 | 713 |
| 10,000 | overview | 488 | 2.05 | 7.59 | 0.24 | 12.17 | 0.35 | 8,553 |
| 20,000 | street | 536 | 1.87 | 7.19 | 0.33 | 7.50 | 0.10 | 1,454 |
| 20,000 | overview | 377 | 2.65 | 8.83 | 0.40 | 7.60 | 0.64 | 17,055 |
