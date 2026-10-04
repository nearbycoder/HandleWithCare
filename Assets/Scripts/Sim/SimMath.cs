using System;

namespace HWC.Sim
{
    /// <summary>Minimal 2D float vector used by the simulation (no UnityEngine dependency).</summary>
    public struct V2 : IEquatable<V2>
    {
        public float x, y;

        public V2(float x, float y) { this.x = x; this.y = y; }

        public static readonly V2 Zero = new V2(0f, 0f);

        public static V2 operator +(V2 a, V2 b) => new V2(a.x + b.x, a.y + b.y);
        public static V2 operator -(V2 a, V2 b) => new V2(a.x - b.x, a.y - b.y);
        public static V2 operator -(V2 a) => new V2(-a.x, -a.y);
        public static V2 operator *(V2 a, float s) => new V2(a.x * s, a.y * s);
        public static V2 operator *(float s, V2 a) => new V2(a.x * s, a.y * s);
        public static V2 operator /(V2 a, float s) => new V2(a.x / s, a.y / s);

        public static float Dot(V2 a, V2 b) => a.x * b.x + a.y * b.y;
        public float Length => MathF.Sqrt(x * x + y * y);
        public float LengthSq => x * x + y * y;
        public V2 Normalized { get { float l = Length; return l > 1e-6f ? new V2(x / l, y / l) : Zero; } }
        public V2 Perp => new V2(-y, x);

        /// <summary>Rotates by angle (radians, counter-clockwise).</summary>
        public V2 Rotated(double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return new V2((float)(x * c - y * s), (float)(x * s + y * c));
        }

        public bool Equals(V2 o) => x == o.x && y == o.y;
        public override bool Equals(object obj) => obj is V2 o && Equals(o);
        public override int GetHashCode() => x.GetHashCode() * 397 ^ y.GetHashCode();
        public override string ToString() => $"({x:0.###}, {y:0.###})";
    }

    /// <summary>Deterministic xorshift RNG. Same seed, same sequence, on every runtime.</summary>
    public struct Rng
    {
        uint state;

        public Rng(uint seed) { state = seed == 0 ? 0x9E3779B9u : seed; }

        public uint NextUInt()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float a, float b) => a + (b - a) * Next01();
    }

    public static class SimMathUtil
    {
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Abs(float a) => a < 0 ? -a : a;
        public static float Sign(float a) => a < 0 ? -1f : 1f;

        /// <summary>Smoothstep easing 0..1.</summary>
        public static double Smooth(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return t * t * (3 - 2 * t);
        }

        /// <summary>Smootherstep (C2) easing 0..1.</summary>
        public static double Smoother(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        /// <summary>FNV-1a hash helper for determinism checks.</summary>
        public static ulong Hash(ulong h, float v)
        {
            uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(v), 0);
            for (int i = 0; i < 4; i++)
            {
                h ^= (bits >> (i * 8)) & 0xFF;
                h *= 1099511628211UL;
            }
            return h;
        }

        public static ulong Hash(ulong h, int v)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= (uint)(v >> (i * 8)) & 0xFF;
                h *= 1099511628211UL;
            }
            return h;
        }

        public const ulong HashSeed = 14695981039346656037UL;
    }
}
