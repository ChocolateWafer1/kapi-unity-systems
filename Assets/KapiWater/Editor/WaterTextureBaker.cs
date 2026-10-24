using UnityEngine;

namespace KapiWater.EditorTools
{
    /// <summary>
    /// Bakes the water's seamless textures. Everything is built from waves that repeat exactly across the tile, so
    /// there are never seams. Caustics are traced for real: light is bent through a wavy surface and piled up where
    /// it lands, which gives the bright, branching lines of sunlight on a pool floor.
    /// </summary>
    static class WaterTextureBaker
    {
        const float k_TwoPi = Mathf.PI * 2f;

        struct Wave
        {
            public int kx;
            public int ky;
            public float amplitude;
            public float phase;
        }

        /// <summary>Small wind ripples as a tangent-space normal map.</summary>
        public static Color32[] RippleNormals(int size, int seed)
        {
            // Lots of small waves from every direction, so no streaks or blobs stand out when it repeats.
            var waves = RandomWaves(seed, 160, 3f, 48f, 1.3f, 0f);
            var slopeX = new float[size * size];
            var slopeY = new float[size * size];
            Gradient(waves, size, size, slopeX, slopeY, null);

            // Scale so the average ripple tilts the surface a similar amount whatever the random waves were.
            double sum = 0;
            for (int i = 0; i < slopeX.Length; i++) sum += slopeX[i] * slopeX[i] + slopeY[i] * slopeY[i];
            float scale = 0.55f / Mathf.Sqrt((float)(sum / slopeX.Length) + 1e-12f);

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                var n = new Vector3(-slopeX[i] * scale, -slopeY[i] * scale, 1f).normalized;
                pixels[i] = new Color32(ToByte(n.x * 0.5f + 0.5f), ToByte(n.y * 0.5f + 0.5f), ToByte(n.z * 0.5f + 0.5f), 255);
            }
            return pixels;
        }

        /// <summary>Real caustics: light bent by a wavy surface, gathered where it lands on the floor.</summary>
        public static Color32[] Caustics(int size, int seed)
        {
            var waves = RandomWaves(seed, 30, 2f, 9f, 2f, 0.15f);
            int photons = size * 2;
            var slopeX = new float[photons * photons];
            var slopeY = new float[photons * photons];
            var curvature = new float[photons * photons];
            Gradient(waves, photons, photons, slopeX, slopeY, curvature);

            // Pick the floor depth so light just starts to focus into lines, wherever these waves are curviest.
            double sum = 0;
            for (int i = 0; i < curvature.Length; i++) sum += curvature[i] * curvature[i];
            float depth = 1.25f / Mathf.Sqrt((float)(sum / curvature.Length) + 1e-12f);

            var gathered = new float[size * size];
            for (int y = 0; y < photons; y++)
            {
                for (int x = 0; x < photons; x++)
                {
                    int i = y * photons + x;
                    float u = (x + 0.5f) / photons - slopeX[i] * depth;
                    float v = (y + 0.5f) / photons - slopeY[i] * depth;
                    Splat(gathered, size, u * size - 0.5f, v * size - 0.5f);
                }
            }

            float perPixel = photons * photons / (float)(size * size);
            var light = new float[size * size];
            for (int i = 0; i < light.Length; i++)
            {
                float brightness = gathered[i] / perPixel;
                light[i] = Mathf.Pow(Mathf.Clamp01((brightness - 0.55f) / 2.1f), 0.8f);
            }
            light = Blur(light, size);

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte b = ToByte(light[i]);
                pixels[i] = new Color32(b, b, b, 255);
            }
            return pixels;
        }

        /// <summary>Foam: cloudy patches broken up by bubble holes.</summary>
        public static Color32[] Foam(int size, int seed)
        {
            var pixels = new Color32[size * size];
            int cells = 10;
            var points = new Vector2[cells * cells];
            var random = new System.Random(seed);
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2((float)random.NextDouble(), (float)random.NextDouble());

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;
                    float clouds = Fbm(u, v, seed, 5);
                    float bubbles = Mathf.Clamp01(CellDistance(u, v, points, cells) * 2.2f);
                    float foam = Mathf.Clamp01((clouds - 0.36f) * 2.4f) * Mathf.Lerp(0.35f, 1f, bubbles);
                    byte b = ToByte(foam);
                    pixels[y * size + x] = new Color32(b, b, b, 255);
                }
            }
            return pixels;
        }

        // Random waves with whole-number wave counts across the tile, so they repeat perfectly.
        static Wave[] RandomWaves(int seed, int count, float minCycles, float maxCycles, float falloff, float windBias)
        {
            var random = new System.Random(seed);
            var waves = new Wave[count];
            float wind = (float)random.NextDouble() * k_TwoPi;
            for (int i = 0; i < count; i++)
            {
                int kx, ky;
                float cycles;
                do
                {
                    cycles = Mathf.Exp(Mathf.Lerp(Mathf.Log(minCycles), Mathf.Log(maxCycles), (float)random.NextDouble()));
                    float angle = (float)random.NextDouble() * k_TwoPi;
                    if (random.NextDouble() < windBias) angle = wind + ((float)random.NextDouble() - 0.5f) * 1.2f;
                    kx = Mathf.RoundToInt(Mathf.Cos(angle) * cycles);
                    ky = Mathf.RoundToInt(Mathf.Sin(angle) * cycles);
                } while (kx == 0 && ky == 0);

                float k = Mathf.Sqrt(kx * kx + ky * ky);
                waves[i] = new Wave
                {
                    kx = kx,
                    ky = ky,
                    amplitude = Mathf.Pow(k, -falloff) * (0.6f + 0.4f * (float)random.NextDouble()),
                    phase = (float)random.NextDouble() * k_TwoPi,
                };
            }
            return waves;
        }

        /// <summary>Slope (per tile width) and curvature of the summed waves on a width x height grid.</summary>
        static void Gradient(Wave[] waves, int width, int height, float[] slopeX, float[] slopeY, float[] curvature)
        {
            var sinX = new float[width];
            var cosX = new float[width];
            var sinY = new float[height];
            var cosY = new float[height];
            foreach (var wave in waves)
            {
                // sin(a + b) and cos(a + b) from the row and column parts, so each pixel is a few multiplies.
                for (int x = 0; x < width; x++)
                {
                    float a = k_TwoPi * wave.kx * (x + 0.5f) / width + wave.phase;
                    sinX[x] = Mathf.Sin(a);
                    cosX[x] = Mathf.Cos(a);
                }
                for (int y = 0; y < height; y++)
                {
                    float b = k_TwoPi * wave.ky * (y + 0.5f) / height;
                    sinY[y] = Mathf.Sin(b);
                    cosY[y] = Mathf.Cos(b);
                }

                float gx = wave.amplitude * k_TwoPi * wave.kx;
                float gy = wave.amplitude * k_TwoPi * wave.ky;
                float lap = -wave.amplitude * k_TwoPi * k_TwoPi * (wave.kx * wave.kx + wave.ky * wave.ky);
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        float cos = cosX[x] * cosY[y] - sinX[x] * sinY[y];
                        slopeX[row + x] += gx * cos;
                        slopeY[row + x] += gy * cos;
                        if (curvature != null) curvature[row + x] += lap * (sinX[x] * cosY[y] + cosX[x] * sinY[y]);
                    }
                }
            }
        }

        static void Splat(float[] target, int size, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            Add(target, size, x0, y0, (1f - fx) * (1f - fy));
            Add(target, size, x0 + 1, y0, fx * (1f - fy));
            Add(target, size, x0, y0 + 1, (1f - fx) * fy);
            Add(target, size, x0 + 1, y0 + 1, fx * fy);
        }

        static void Add(float[] target, int size, int x, int y, float amount)
        {
            x = ((x % size) + size) % size;
            y = ((y % size) + size) % size;
            target[y * size + x] += amount;
        }

        static float[] Blur(float[] source, int size)
        {
            var result = new float[source.Length];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sum = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float weight = (dx == 0 ? 2f : 1f) * (dy == 0 ? 2f : 1f);
                        int sx = (x + dx + size) % size;
                        int sy = (y + dy + size) % size;
                        sum += source[sy * size + sx] * weight;
                    }
                    result[y * size + x] = sum / 16f;
                }
            }
            return result;
        }

        static float Fbm(float u, float v, int seed, int octaves)
        {
            float sum = 0f, weight = 0.5f, total = 0f;
            int period = 4;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * period, v * period, period, seed + o * 1013) * weight;
                total += weight;
                weight *= 0.5f;
                period *= 2;
            }
            return sum / total;
        }

        static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Lattice(x0, y0, period, seed), b = Lattice(x0 + 1, y0, period, seed);
            float c = Lattice(x0, y0 + 1, period, seed), d = Lattice(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Lattice(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519u);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
            }
        }

        // Distance to the nearest random point on a grid that wraps around, as a fraction of a cell.
        static float CellDistance(float u, float v, Vector2[] points, int cells)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float best = float.MaxValue;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                var p = points[(((ny % cells) + cells) % cells) * cells + (((nx % cells) + cells) % cells)];
                float px = nx + p.x - x, py = ny + p.y - y;
                best = Mathf.Min(best, px * px + py * py);
            }
            return Mathf.Sqrt(best);
        }

        static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
    }
}
