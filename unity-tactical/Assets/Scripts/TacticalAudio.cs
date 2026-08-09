using System;
using System.Collections.Generic;
using UnityEngine;

namespace DownRange.Tactical
{
    public enum SoundCue { Click, Move, Fire, Suppress, Hit, Medical, Objective, Turn, Error }

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

        void BuildClips()
        {
            clips[SoundCue.Click] = Make("UI click", .055f, (t, random) => Mathf.Sin(t * 2900f) * Mathf.Exp(-t * 45f) * .42f);
            clips[SoundCue.Move] = Make("Movement", .13f, (t, random) => (random * .18f + Mathf.Sin(t * 520f) * .12f) * Mathf.Exp(-t * 18f));
            clips[SoundCue.Fire] = Make("Rifle shot", .17f, (t, random) => (random * .75f + Mathf.Sin(t * 720f) * .22f) * Mathf.Exp(-t * 30f));
            clips[SoundCue.Suppress] = Make("Suppressive burst", .48f, (t, random) => Burst(t, random, 0f) + Burst(t, random, .14f) + Burst(t, random, .29f));
            clips[SoundCue.Hit] = Make("Impact", .20f, (t, random) => (Mathf.Sin(t * 260f) * .4f + random * .18f) * Mathf.Exp(-t * 15f));
            clips[SoundCue.Medical] = Make("Medical", .34f, (t, random) => Chime(t, 0f, 760f) + Chime(t, .16f, 980f));
            clips[SoundCue.Objective] = Make("Objective", .46f, (t, random) => Chime(t, 0f, 620f) + Chime(t, .13f, 780f) + Chime(t, .27f, 1040f));
            clips[SoundCue.Turn] = Make("Turn", .28f, (t, random) => Chime(t, 0f, 440f) + Chime(t, .12f, 660f));
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
