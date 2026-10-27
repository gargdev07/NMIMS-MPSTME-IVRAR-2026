using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace S014.CampusVR
{
    /// <summary>
    /// The campus guide's voice. Plays short pre-recorded lines (Resources/Voice) one after
    /// another through a non-positional audio source so they are always audible in the headset,
    /// wherever the visitor stands. Also makes the lift sounds (hum + arrival chime).
    /// Created on demand, so no scene wiring is needed.
    /// </summary>
    public class S014AgentVoice : MonoBehaviour
    {
        private static S014AgentVoice instance;
        private AudioSource src;
        private AudioSource fx;
        private Coroutine routine;
        private AudioClip hum, chime;
        private readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        public static S014AgentVoice Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("CampusVR_Voice");
                    instance = go.AddComponent<S014AgentVoice>();
                }
                return instance;
            }
        }

        public bool IsSpeaking { get { return src != null && src.isPlaying; } }

        private void Awake()
        {
            instance = this;
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 1f; src.priority = 0;
            fx = gameObject.AddComponent<AudioSource>();
            fx.playOnAwake = false; fx.spatialBlend = 0f; fx.volume = 0.5f; fx.priority = 32;
            hum = MakeHum();
            chime = MakeChime();
        }

        public static string NameKey(string destinationId)
        {
            var sb = new System.Text.StringBuilder("n_");
            foreach (char c in destinationId) sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
            return sb.ToString();
        }

        public static string FloorKey(string floorLabel) { return "f_" + floorLabel; }

        private AudioClip Get(string key)
        {
            AudioClip c;
            if (cache.TryGetValue(key, out c)) return c;
            c = Resources.Load<AudioClip>("Voice/" + key);
            cache[key] = c;
            return c;
        }

        /// <summary>Speak the given clips in order (e.g. "p_follow", NameKey(id)). Replaces whatever is playing.</summary>
        public void Say(params string[] keys)
        {
            if (routine != null) StopCoroutine(routine);
            if (src != null) src.Stop();
            routine = StartCoroutine(Run(keys));
        }

        public void Say(AudioClip clip)
        {
            if (clip == null) return;
            if (routine != null) StopCoroutine(routine);
            src.Stop();
            routine = StartCoroutine(RunClips(new List<AudioClip> { clip }));
        }

        public void Silence()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (src != null) src.Stop();
        }

        private IEnumerator Run(string[] keys)
        {
            var clips = new List<AudioClip>();
            foreach (var k in keys) { var c = Get(k); if (c != null) clips.Add(c); }
            yield return RunClips(clips);
        }

        private IEnumerator RunClips(List<AudioClip> clips)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                src.clip = clips[i];
                src.Play();
                while (src.isPlaying) yield return null;
                yield return new WaitForSeconds(0.08f);
            }
            routine = null;
        }

        public void LiftHum(bool on)
        {
            if (fx == null) return;
            if (on) { fx.clip = hum; fx.loop = true; fx.volume = 0.35f; fx.Play(); }
            else { fx.loop = false; fx.Stop(); }
        }

        public void LiftChime()
        {
            if (fx == null) return;
            fx.loop = false; fx.volume = 0.6f; fx.clip = chime; fx.Play();
        }

        private static AudioClip MakeHum()
        {
            const int rate = 22050; int n = rate * 2;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                d[i] = 0.35f * Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.18f * Mathf.Sin(2f * Mathf.PI * 110f * t) + 0.06f * Mathf.Sin(2f * Mathf.PI * 165f * t);
            }
            var c = AudioClip.Create("liftHum", n, 1, rate, false); c.SetData(d, 0); return c;
        }

        private static AudioClip MakeChime()
        {
            const int rate = 22050; int n = (int)(rate * 1.4f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-3.2f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * 880f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 1318f * t);
                if (t > 0.35f) tone += Mathf.Sin(2f * Mathf.PI * 1046f * (t - 0.35f)) * 0.8f;
                d[i] = 0.4f * env * tone * (t < 0.01f ? t / 0.01f : 1f);
            }
            var c = AudioClip.Create("liftChime", n, 1, rate, false); c.SetData(d, 0); return c;
        }
    }
}
