using System.Collections.Generic;
using UnityEngine;

namespace HWC.Audio
{
    /// <summary>
    /// Pooled one-shot SFX with variants (name_1, name_2...), pitch jitter and rate limiting,
    /// plus crossfading music with ducking. Clips live in Resources/Audio/Sfx and /Music.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector I;
        public float Master = 0.9f, Music = 0.7f, Sfx = 0.9f;
        public static event System.Action<string, float> Played;   // trailer capture logs sound cues

        readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource musicA, musicB;
        AudioSource loopSrc;
        string currentMusic;
        float duck = 1f, duckTarget = 1f, musicFade = 1f;
        float muffle;
        AudioLowPassFilter lowpass;
        bool aIsCurrent = true;

        public static AudioDirector Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioDirector>();
            I = a;
            for (int i = 0; i < 24; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                a.pool.Add(s);
            }
            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(go.transform, false);
            a.musicA = musicGo.AddComponent<AudioSource>();
            a.musicB = musicGo.AddComponent<AudioSource>();
            foreach (var m in new[] { a.musicA, a.musicB }) { m.loop = true; m.playOnAwake = false; m.volume = 0; }
            a.lowpass = musicGo.AddComponent<AudioLowPassFilter>();
            a.lowpass.cutoffFrequency = 22000f;
            a.loopSrc = go.AddComponent<AudioSource>();
            a.loopSrc.loop = true;
            a.loopSrc.playOnAwake = false;
            return a;
        }

        AudioClip[] Get(string name)
        {
            if (clips.TryGetValue(name, out var c)) return c;
            var list = new List<AudioClip>();
            var one = Resources.Load<AudioClip>("Audio/Sfx/" + name);
            if (one != null) list.Add(one);
            for (int i = 1; i <= 8; i++)
            {
                var v = Resources.Load<AudioClip>($"Audio/Sfx/{name}_{i}");
                if (v == null) break;
                list.Add(v);
            }
            c = list.ToArray();
            clips[name] = c;
            return c;
        }

        public void Play(string name, float intensity = 1f, float pitch = 1f, float minGap = 0.03f)
        {
            var set = Get(name);
            if (set.Length == 0) return;
            float now = Clock.UnscaledTime;
            if (lastPlayed.TryGetValue(name, out float t) && now - t < minGap) return;
            lastPlayed[name] = now;
            var src = Free();
            if (src == null) return;
            src.clip = set[Random.Range(0, set.Length)];
            src.volume = Mathf.Clamp01(intensity) * Sfx * Master;
            src.pitch = pitch * Random.Range(0.94f, 1.06f) * (Time.timeScale < 0.9f ? Mathf.Lerp(0.7f, 1f, Time.timeScale) : 1f);
            src.Play();
            Played?.Invoke(name, src.volume);
        }

        AudioSource Free()
        {
            foreach (var s in pool) if (!s.isPlaying) return s;
            return null;
        }

        public void PlayMusic(string name, float fade = 1.2f)
        {
            if (name == currentMusic) return;
            currentMusic = name;
            var clip = name == null ? null : Resources.Load<AudioClip>("Audio/Music/" + name);
            var next = aIsCurrent ? musicB : musicA;
            aIsCurrent = !aIsCurrent;
            next.clip = clip;
            next.volume = 0;
            if (clip != null) next.Play();
            musicFade = 0f;
            fadeTime = Mathf.Max(0.05f, fade);
        }

        float fadeTime = 1f;

        public void Loop(string name, float volume)
        {
            if (name == null) { loopSrc.Stop(); loopSrc.clip = null; return; }
            var set = Get(name);
            if (set.Length == 0) return;
            if (loopSrc.clip != set[0]) { loopSrc.clip = set[0]; loopSrc.Play(); }
            loopSrc.volume = volume * Sfx * Master;
        }

        public void Duck(float amount, float hold = 0.4f)
        {
            duckTarget = Mathf.Min(duckTarget, 1f - amount);
            duckHold = Mathf.Max(duckHold, hold);
        }

        float duckHold;

        public void Muffle(bool on) { muffle = on ? 1f : 0f; }

        void Update()
        {
            float dt = Clock.UnscaledDelta;
            if (duckHold > 0) duckHold -= dt; else duckTarget = 1f;
            duck = Mathf.MoveTowards(duck, duckTarget, dt * (duckTarget < duck ? 6f : 1.2f));
            musicFade = Mathf.Min(1f, musicFade + dt / fadeTime);
            var cur = aIsCurrent ? musicA : musicB;
            var old = aIsCurrent ? musicB : musicA;
            float v = Music * Master * duck;
            cur.volume = v * musicFade;
            old.volume = v * (1f - musicFade);
            if (musicFade >= 1f && old.isPlaying) old.Stop();
            lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, muffle > 0 ? 900f : 22000f, 1f - Mathf.Exp(-dt * 6f));
        }
    }
}
