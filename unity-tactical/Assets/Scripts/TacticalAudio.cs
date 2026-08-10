using System;
using System.Collections.Generic;
using UnityEngine;

namespace DownRange.Tactical
{
    public enum SoundCue
    {
        Click, MoveReady, Move, Fire, Suppress, Reaction, Sprint, Radio, Medical, Relay, Los, Hit, Objective, Alarm, Turn, Mission, Error
    }

    public sealed class TacticalAudio
    {
        readonly AudioSource source;
        readonly Dictionary<SoundCue, AudioClip> clips = new Dictionary<SoundCue, AudioClip>();
        const int SampleRate = 22050;

        public bool Enabled
        {
            get { return PlayerPrefs.GetInt("downrange.sound", 1) == 1; }
            set { PlayerPrefs.SetInt("downrange.sound", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public TacticalAudio(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = .42f;
            BuildClips();
        }

        public void Play(SoundCue cue)
        {
            AudioClip clip;
            if (Enabled && clips.TryGetValue(cue, out clip)) source.PlayOneShot(clip);
        }

        public bool HasClip(SoundCue cue) { return clips.ContainsKey(cue); }

        void BuildClips()
        {
            clips[SoundCue.Click] = Make("UI click", .055f, (t, random) => Mathf.Sin(t * 2900f) * Mathf.Exp(-t * 45f) * .42f);
            clips[SoundCue.MoveReady] = Make("Move ready", .16f, (t, random) => Chime(t, 0f, 330f) + Chime(t, .075f, 440f));
            clips[SoundCue.Move] = Make("Movement", .13f, (t, random) => (random * .18f + Mathf.Sin(t * 520f) * .12f) * Mathf.Exp(-t * 18f));
            clips[SoundCue.Fire] = Make("Rifle shot", .17f, (t, random) => (random * .75f + Mathf.Sin(t * 720f) * .22f) * Mathf.Exp(-t * 30f));
            clips[SoundCue.Suppress] = Make("Suppressive burst", .48f, (t, random) => Burst(t, random, 0f) + Burst(t, random, .14f) + Burst(t, random, .29f));
            clips[SoundCue.Reaction] = Make("Reaction held", .28f, (t, random) => Chime(t, 0f, 520f) + Chime(t, .11f, 390f));
            clips[SoundCue.Sprint] = Make("Sprint", .30f, (t, random) => Step(t, random, 0f) + Step(t, random, .09f) + Step(t, random, .18f));
            clips[SoundCue.Radio] = Make("Radio observation", .42f, (t, random) => RadioSquelch(t, random) + Chime(t, .16f, 1180f) + Chime(t, .25f, 920f));
            clips[SoundCue.Hit] = Make("Impact", .20f, (t, random) => (Mathf.Sin(t * 260f) * .4f + random * .18f) * Mathf.Exp(-t * 15f));
            clips[SoundCue.Medical] = Make("Medical", .34f, (t, random) => Chime(t, 0f, 760f) + Chime(t, .16f, 980f));
            clips[SoundCue.Relay] = Make("Relay observation", .54f, (t, random) => Sweep(t, 340f, 880f, .38f) + Chime(t, .35f, 1040f));
            clips[SoundCue.Los] = Make("Line of sight", .30f, (t, random) => Chime(t, 0f, 880f) + Chime(t, .13f, 1320f));
            clips[SoundCue.Objective] = Make("Objective", .46f, (t, random) => Chime(t, 0f, 620f) + Chime(t, .13f, 780f) + Chime(t, .27f, 1040f));
            clips[SoundCue.Alarm] = Make("Alarm raised", .72f, (t, random) => Chime(t, 0f, 960f) + Chime(t, .14f, 720f) + Chime(t, .28f, 960f) + Chime(t, .42f, 720f));
            clips[SoundCue.Turn] = Make("Turn", .28f, (t, random) => Chime(t, 0f, 440f) + Chime(t, .12f, 660f));
            clips[SoundCue.Mission] = Make("Mission complete", .72f, (t, random) => Chime(t, 0f, 440f) + Chime(t, .15f, 660f) + Chime(t, .30f, 880f) + Chime(t, .47f, 1100f));
            clips[SoundCue.Error] = Make("Unavailable", .15f, (t, random) => Mathf.Sin(t * 310f) * Mathf.Exp(-t * 13f) * .30f);
        }

        static float Burst(float t, float random, float start)
        {
            var local = t - start;
            return local < 0f || local > .12f ? 0f : (random * .58f + Mathf.Sin(local * 760f) * .16f) * Mathf.Exp(-local * 35f);
        }

        static float Chime(float t, float start, float frequency)
        {
            var local = t - start;
            return local < 0f ? 0f : Mathf.Sin(local * frequency * Mathf.PI * 2f) * Mathf.Exp(-local * 10f) * .22f;
        }

        static float Step(float t, float random, float start)
        {
            var local = t - start;
            return local < 0f || local > .075f ? 0f : (random * .24f + Mathf.Sin(local * 190f) * .18f) * Mathf.Exp(-local * 34f);
        }

        static float RadioSquelch(float t, float random)
        {
            if (t > .15f) return 0f;
            return (random * .20f + Mathf.Sin(t * 1850f) * .08f) * Mathf.Exp(-t * 15f);
        }

        static float Sweep(float t, float from, float to, float duration)
        {
            if (t < 0f || t > duration) return 0f;
            var frequency = Mathf.Lerp(from, to, t / duration);
            return Mathf.Sin(t * frequency * Mathf.PI * 2f) * Mathf.Sin(t / duration * Mathf.PI) * .17f;
        }

        static AudioClip Make(string name, float duration, Func<float, float, float> sample)
        {
            var count = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[count];
            var random = new System.Random(name.GetHashCode());
            for (var i = 0; i < count; i++) data[i] = Mathf.Clamp(sample((float)i / SampleRate, (float)(random.NextDouble() * 2.0 - 1.0)), -.9f, .9f);
            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
