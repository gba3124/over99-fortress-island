using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace HtfFortress
{
    // The bosses' sounds on a channel of their own (Owen 2026-10-27: 「打王 特效的時候怪物沒有自己的叫聲 還有怪物本身招式或是他
    // 移動都沒聲音」). Through the game's AudioManager they took turns on its 10 varied world sources with every turret shot
    // (each new sound moving the source an earlier one was still playing on), our own budget (Fx.MayPlay, 12 a quarter
    // second) went to the turrets first in a big fight, and Medium faded out by 50 m, often short of the boss. Here:
    // sources of our own, copies of the game's (its mixer, so its volume settings), first in line for a real voice, heard
    // much further, each at its own pitch, held on a moving boss, and faded out where a call should end before its clip
    // does. Clients only, like every effect.
    public class BossAudio : MonoBehaviour
    {
        private sealed class Voice { public AudioSource Src; public Transform Follow; public float Started, Until, Volume, Fade; public bool Call; public int Owner; }
        private const int Most = 20;
        private const float SameGap = 0.06f, Fade = 0.35f;
        private static BossAudio _host;
        private static AudioSource _template;
        private static FieldInfo _amInstance, _amVolume;
        private readonly List<Voice> _voices = new List<Voice>();
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();

        // Fx.Sound / Fx.SoundRandom on this channel (a random one: prefix + min..max, both ends included, the game's way).
        public static void Sound(string clip, Vector3 at, float volume = 1f, AudioDistance dist = AudioDistance.Medium) => Play(clip, clip, at, volume, 1f, dist);
        public static void SoundRandom(string prefix, int min, int max, Vector3 at, float volume = 1f, AudioDistance dist = AudioDistance.Medium) =>
            Play(prefix + Random.Range(min, max + 1), prefix, at, volume, 1f, dist);
        // One of `prefix`1..n (n 0: `prefix` itself), at a pitch (which stretches it too), faded out by `max` seconds (0: whole).
        public static void Pitched(string prefix, int n, Vector3 at, float volume, float pitch, float max = 0f) =>
            Play(n > 0 ? prefix + Random.Range(1, n + 1) : prefix, prefix, at, volume, pitch, AudioDistance.Long, null, max);

        // `follow`: it moves with that (a boss calling as it goes); `call`: a boss's voice, heard furthest, never cut for an
        // effect; `owner`: whose call it is (Hush cuts it short).
        public static void Play(string clip, string family, Vector3 at, float volume, float pitch, AudioDistance dist, Transform follow = null, float max = 0f, bool call = false, int owner = 0)
        {
            if (!Ready()) { Fx.Sound(clip, at, volume, dist); return; } // no game audio to copy yet: the old way
            float now = Time.time;
            string fam = family ?? clip; // (a call isn't held back: BossVoice paces its own)
            if (!call && LastPlayed.TryGetValue(fam, out float last) && now - last < SameGap && now >= last) return;
            var c = AudioManager.GetClip(clip);
            if (!c) return;
            if (!call) LastPlayed[fam] = now;
            var v = _host.Take(call);
            if (v == null) return;
            var s = v.Src;
            s.Stop();
            s.clip = c;
            s.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            var reach = Reach(dist, call);
            s.minDistance = reach.x; s.maxDistance = reach.y;
            s.priority = call ? 0 : 4; // (the game's Long sounds are 8, its turrets' 128: lower comes first for a real voice)
            v.Volume = Mathf.Clamp01(volume * GameVolume() * _template.volume);
            s.volume = v.Volume;
            v.Follow = follow;
            s.transform.position = follow ? follow.position : at;
            v.Call = call;
            v.Owner = owner;
            v.Started = now;
            v.Until = max > 0f ? now + max : 0f;
            v.Fade = Fade;
            s.Play();
        }

        // Its calls still sounding faded out at once (a new call of its own takes over).
        public static void Hush(int owner)
        {
            if (!_host || owner == 0) return;
            float now = Time.time;
            foreach (var v in _host._voices)
                if (v.Owner == owner && v.Src && v.Src.isPlaying && (v.Until <= 0f || v.Until - now > v.Fade)) { v.Until = now + 0.12f; v.Fade = 0.12f; }
        }

        // Later, on the channel's own object (a boss's body may be gone by then).
        public static void After(float seconds, Action a) { if (Ready()) _host.StartCoroutine(Wait(seconds, a)); }
        private static IEnumerator Wait(float seconds, Action a) { yield return new WaitForSeconds(seconds); a(); }

        // How far it carries (linear: still clear at half of it): a boss's voice furthest, then its moves.
        private static Vector2 Reach(AudioDistance d, bool call) =>
            call ? new Vector2(35f, 300f) : d == AudioDistance.Long ? new Vector2(25f, 220f) : d == AudioDistance.Medium ? new Vector2(15f, 130f) : new Vector2(6f, 60f);

        private static bool Ready()
        {
            if (_host && _template) return true;
            if (!_template)
            {
                var pool = AccessTools.Field(typeof(AudioManager), "_worldSources")?.GetValue(null) as List<AudioSource>;
                if (pool != null) foreach (var s in pool) if (s) { _template = s; break; } // (a second AudioManager's are destroyed, still listed)
                if (!_template) return false;
            }
            if (!_host)
            {
                var go = new GameObject("FortressBossAudio");
                DontDestroyOnLoad(go);
                _host = go.AddComponent<BossAudio>();
            }
            return true;
        }

        // The game's own loudness for its one-shots (AudioManager._volume), on top of its mixer.
        private static float GameVolume()
        {
            try
            {
                if (_amInstance == null) { _amInstance = AccessTools.Field(typeof(AudioManager), "_instance"); _amVolume = AccessTools.Field(typeof(AudioManager), "_volume"); }
                var am = _amInstance?.GetValue(null);
                return am != null && _amVolume != null ? (float)_amVolume.GetValue(am) : 0.5f;
            }
            catch { return 0.5f; }
        }

        // A free source, or a new one, or (all busy) the oldest effect; a call takes the oldest of anything.
        private Voice Take(bool call)
        {
            foreach (var v in _voices) if (v.Src && !v.Src.isPlaying) return v;
            _voices.RemoveAll(v => !v.Src);
            if (_voices.Count < Most)
            {
                var src = Instantiate(_template, transform);
                src.name = "BossVoice";
                src.playOnAwake = false; src.loop = false; src.spatialBlend = 1f; src.dopplerLevel = 0f; src.spread = 0f;
                src.rolloffMode = AudioRolloffMode.Linear;
                var made = new Voice { Src = src };
                _voices.Add(made);
                return made;
            }
            Voice oldest = null;
            foreach (var v in _voices) if ((call || !v.Call) && (oldest == null || v.Started < oldest.Started)) oldest = v;
            return oldest;
        }

        private void LateUpdate()
        {
            float now = Time.time;
            foreach (var v in _voices)
            {
                if (!v.Src || !v.Src.isPlaying) continue;
                if (v.Follow) v.Src.transform.position = v.Follow.position;
                if (v.Until <= 0f) continue;
                if (now >= v.Until) { v.Src.Stop(); continue; }
                v.Src.volume = v.Volume * Mathf.Clamp01((v.Until - now) / v.Fade);
            }
        }
    }

    // A boss's own voice: the fish they're made of have none in the game, so each gets calls made of the game's creature
    // sounds, pitched to it (a little deeper in its second form): a roar as it winds up a big move or changes form, a call
    // as it attacks, a cry when it's hurt, a growl now and then, and its death; and how it sounds on the move.
    public static class BossVoice
    {
        public enum Call { Roar, Attack, Hurt, Idle, Death }

        // One sound of a call: Clip (N > 0: one of Clip1..ClipN), at Pitch (± Spread), Vol, faded out by Max seconds (0: the
        // whole clip), after Delay; Repeat times Gap apart, the pitch moving by Step each time (a glitch's stutter).
        public struct Line { public string Clip; public int N, Repeat; public float Pitch, Spread, Vol, Max, Delay, Gap, Step; }
        private static Line L(string clip, int n, float pitch, float vol = 1f, float max = 0f, float delay = 0f, float spread = 0.04f, int repeat = 1, float gap = 0f, float step = 0f) =>
            new Line { Clip = clip, N = n, Pitch = pitch, Vol = vol, Max = max, Delay = delay, Spread = spread, Repeat = repeat, Gap = gap, Step = step };
        private static Line[] Of(params Line[] l) => l;
        private static readonly Line[] None = new Line[0];

        // Minions (the squadron's fish, the voxel's copies): a few small sounds, shared between all of them.
        public static bool Minion(EnemyKind k) => k == EnemyKind.Squadron || k == EnemyKind.Glitch;

        public static Line[] Lines(EnemyKind k, Call c)
        {
            switch (k)
            {
                case EnemyKind.Warden: // the jailer lobster: a throaty growl, its claws clacking
                    switch (c)
                    {
                        case Call.Roar: return Of(L("MantaRayAttack_0", 4, 0.7f, 1f, 2.2f), L("SpiderCrabSnip_V", 3, 0.85f, 0.9f, 0f, 0.15f), L("SpiderCrabSnip_V", 3, 0.8f, 0.9f, 0f, 0.4f));
                        case Call.Attack: return Of(L("SpiderCrabSnip_V", 3, 0.8f, 0.9f), L("MantaRayAttack_0", 4, 0.8f, 0.7f, 1.1f, 0.05f));
                        case Call.Hurt: return Of(L("MantaRayHurt_0", 2, 0.75f, 1f, 1.4f));
                        case Call.Idle: return Of(L("SpiderCrabSnip_V", 3, 0.7f, 0.6f, 0f, 0f, 0.06f, 2, 0.22f));
                        default: return Of(L("LavaWhaleDeathCry_0", 3, 1.1f, 1f, 3.5f), L("MetallicItemImpact_V", 3, 0.6f, 0.8f));
                    }
                case EnemyKind.Urchin: // the sea urchin: puffing up
                    switch (c)
                    {
                        case Call.Roar: return Of(L("Pufferfish", 0, 0.75f, 1f, 2.4f));
                        case Call.Attack: return Of(L("Pufferfish", 0, 1.05f, 0.8f, 1f));
                        case Call.Hurt: return Of(L("MantaRayHurt_0", 2, 1.5f, 0.9f, 1f));
                        case Call.Idle: return Of(L("PufferfishRoll", 0, 0.7f, 0.5f, 1.6f));
                        default: return Of(L("Pufferfish", 0, 0.55f, 1f, 3f), L("Poof", 0, 0.6f, 1f, 0f, 0.8f));
                    }
                case EnemyKind.Angler: // the anglerfish: a deep-sea moan
                    switch (c)
                    {
                        case Call.Roar: return Of(L("WhaleNoise_0", 6, 0.8f, 1f, 3f));
                        case Call.Attack: return Of(L("MantaRayAttack_0", 4, 0.65f, 0.8f, 1.4f));
                        case Call.Hurt: return Of(L("MantaRayHurt_0", 2, 0.65f, 1f, 1.6f));
                        case Call.Idle: return Of(L("WhaleNoise_0", 6, 0.7f, 0.6f, 2.6f));
                        default: return Of(L("LavaWhaleDeathCry_0", 3, 0.85f, 1f, 4f));
                    }
                case EnemyKind.Steed: // the seahorse knight: a high, ringing whinny
                    switch (c)
                    {
                        case Call.Roar: return Of(L("MantaRayAttack_0", 4, 1.2f, 1f, 2f));
                        case Call.Attack: return Of(L("MantaRayAttack_0", 4, 1.35f, 0.8f, 1.2f));
                        case Call.Hurt: return Of(L("MantaRayHurt_0", 2, 1.25f, 1f, 1.3f));
                        case Call.Idle: return Of(L("MantaRayIdle_0", 4, 1.2f, 0.55f, 2.2f));
                        default: return Of(L("MantaRayHurt_0", 2, 0.85f, 1f, 1.8f), L("LavaWhaleDeathCry_0", 3, 1.35f, 0.8f, 3f, 0.4f));
                    }
                case EnemyKind.Stonefish: // the stonefish: a grinding rumble, stone in it
                    switch (c)
                    {
                        case Call.Roar: return Of(L("LavaWhaleNoise_0", 4, 0.65f, 1f, 3f), L("ItemImpactHeavy_V", 3, 0.6f, 0.7f, 0f, 0.1f));
                        case Call.Attack: return Of(L("LavaWhaleNoise_0", 4, 0.8f, 0.8f, 1.4f));
                        case Call.Hurt: return Of(L("MantaRayHurt_0", 2, 0.55f, 1f, 1.8f));
                        case Call.Idle: return Of(L("LavaWhaleNoise_0", 4, 0.6f, 0.5f, 2.4f));
                        default: return Of(L("LavaWhaleDeathCry_0", 3, 0.75f, 1f, 4.2f), L("ItemImpactHeavy_V", 3, 0.5f, 0.8f, 0f, 0.3f));
                    }
                case EnemyKind.Skyfish: // the flying fish: a seabird's scream
                    switch (c)
                    {
                        case Call.Roar: return Of(L("AlbatrossScreamDive_0", 2, 1.1f, 1f, 2.4f));
                        case Call.Attack: return Of(L("AlbatrossScreamDive_0", 2, 1.3f, 0.8f, 1.3f));
                        case Call.Hurt: return Of(L("AlbatrossScreamTakeDamage", 0, 1.15f, 1f, 1.6f));
                        case Call.Idle: return Of(L("AlbatrossScreamIdle_0", 2, 1.2f, 0.55f, 1.8f));
                        default: return Of(L("AlbatrossScreamTakeDamage", 0, 0.8f, 1f, 2.6f));
                    }
                case EnemyKind.Squadron:
                    switch (c)
                    {
                        case Call.Attack: case Call.Roar: return Of(L("Seagull_V", 11, 1.3f, 0.35f));
                        case Call.Hurt: return Of(L("Seagull_V", 11, 1.5f, 0.3f));
                        case Call.Death: return Of(L("AlbatrossScreamTakeDamage", 0, 1.6f, 0.4f, 0.8f));
                        default: return None;
                    }
                case EnemyKind.Kraken: // the kraken: a titan's groan out of the sea
                    switch (c)
                    {
                        case Call.Roar: return Of(L("LavaWhaleNoise_0", 4, 0.85f, 1f, 3.4f));
                        case Call.Attack: return Of(L("WhaleNoise_0", 6, 0.6f, 0.8f, 1.6f));
                        case Call.Hurt: return Of(L("MantaRayHurt_0", 2, 0.5f, 1f, 2f));
                        case Call.Idle: return Of(L("WhaleNoise_0", 6, 0.55f, 0.55f, 3f));
                        default: return Of(L("LavaWhaleDeathCry_0", 3, 0.65f, 1f, 5f));
                    }
                case EnemyKind.Voxel: // the pixel fish: static and error beeps
                    switch (c)
                    {
                        case Call.Roar: return Of(L("RadioNoise_loop", 0, 0.8f, 0.6f, 1.1f), L("Error", 0, 0.55f, 1f, 0f, 0.05f, 0.3f, 5, 0.1f));
                        case Call.Attack: return Of(L("Error", 0, 1f, 0.8f, 0f, 0f, 0.35f, 3, 0.08f));
                        case Call.Hurt: return Of(L("Error", 0, 0.6f, 1f, 0f, 0f, 0.1f, 2, 0.12f));
                        case Call.Idle: return Of(L("TypingClick", 3, 0.9f, 0.5f, 0f, 0f, 0.3f, 4, 0.09f));
                        default: return Of(L("RadioNoise_loop", 0, 0.5f, 0.8f, 2.2f), L("Error", 0, 0.7f, 1f, 0f, 0.1f, 0.03f, 6, 0.2f, -0.05f));
                    }
                case EnemyKind.Glitch:
                    switch (c)
                    {
                        case Call.Attack: case Call.Roar: return Of(L("Error", 0, 1.3f, 0.35f, 0f, 0f, 0.3f, 2, 0.08f));
                        case Call.Hurt: return Of(L("Error", 0, 1.4f, 0.3f));
                        case Call.Death: return Of(L("Error", 0, 0.9f, 0.4f, 0f, 0f, 0.03f, 3, 0.08f, -0.1f));
                        default: return None;
                    }
                case EnemyKind.Koi: // the sky koi: a high whale song
                    switch (c)
                    {
                        case Call.Roar: return Of(L("WhaleNoise_0", 6, 1.25f, 1f, 2.6f));
                        case Call.Attack: return Of(L("MantaRayAttack_0", 4, 1.05f, 0.8f, 1.3f));
                        case Call.Hurt: return Of(L("MantaRayHurt_0", 2, 1.05f, 1f, 1.4f));
                        case Call.Idle: return Of(L("WhaleNoise_0", 6, 1.3f, 0.5f, 2.2f));
                        default: return Of(L("WhaleNoise_0", 6, 0.9f, 1f, 3.5f));
                    }
                case EnemyKind.Eye: // the eye: something vast and wrong, a hum of static under it
                    switch (c)
                    {
                        case Call.Roar: return Of(L("LavaWhaleNoise_0", 4, 0.55f, 1f, 3.6f), L("RadioNoise_loop", 0, 0.45f, 0.45f, 2.2f));
                        case Call.Attack: return Of(L("WhaleNoise_0", 6, 0.5f, 0.75f, 1.6f));
                        case Call.Hurt: return Of(L("MantaRayHurt_0", 2, 0.45f, 1f, 2f));
                        case Call.Idle: return Of(L("WhaleNoise_0", 6, 0.42f, 0.5f, 3f));
                        default: return Of(L("LavaWhaleDeathCry_0", 3, 0.55f, 1f, 5.5f));
                    }
                default: return None;
            }
        }

        // On the move: Clip (one of 1..N; N 0: itself) every Stride metres. Feet: on the ground only, a splash in the surf.
        public struct Gait { public string Clip; public int N; public float Stride, Vol, Pitch; public bool Feet; }
        private static Gait G(string clip, int n, float stride, float vol, float pitch, bool feet = false) => new Gait { Clip = clip, N = n, Stride = stride, Vol = vol, Pitch = pitch, Feet = feet };
        public const string Splash = "Footstep_Water_V"; public const int Splashes = 5;
        public static Gait? GaitOf(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Warden: return G("SpidercrabWalk_V", 8, 1.8f, 0.9f, 0.75f, true);  // legs clicking over the sand
                case EnemyKind.Urchin: return G("CrabStep", 3, 1.2f, 0.8f, 0.75f, true);          // its spines tapping
                case EnemyKind.Steed: return G("Footstep_Sand_V", 5, 2.6f, 0.9f, 0.85f, true);
                case EnemyKind.Stonefish: return G("Footstep_Sand_V", 5, 1.6f, 1f, 0.6f, true);    // heavy
                case EnemyKind.Angler: return G("ItemHitWaterLight_V", 3, 3f, 0.6f, 0.75f);       // swimming through the surf
                case EnemyKind.Kraken: return G("ItemHitWaterMedium_V", 3, 4.5f, 0.7f, 0.6f);
                case EnemyKind.Koi: return G("PickupWhoosh", 0, 3.5f, 0.55f, 0.6f);               // through the air
                case EnemyKind.Skyfish: return G("PunchSwoosh", 0, 2.6f, 0.7f, 0.7f);              // wingbeats
                case EnemyKind.Squadron: return G("PunchSwoosh", 0, 4f, 0.25f, 1.1f);
                case EnemyKind.Voxel: return G("TypingClick", 3, 1.6f, 0.55f, 0.8f);               // glitching along
                case EnemyKind.Glitch: return G("TypingClick", 3, 2.6f, 0.25f, 1.1f);
                case EnemyKind.Eye: return G("PickupWhoosh", 0, 4f, 0.4f, 0.4f);                   // drifting
                default: return null;
            }
        }

        // Per boss (all of a kind's minions share one): when its call ends, when it may next attack or cry out, when it last
        // roared, its latest call (what's left of an earlier one is dropped), and what it is (for its death: its body may
        // be gone by then).
        private static readonly Dictionary<int, float> BusyUntil = new Dictionary<int, float>(), NextAttack = new Dictionary<int, float>(),
                                                       NextHurt = new Dictionary<int, float>(), RoaredAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> Latest = new Dictionary<int, int>();
        private static readonly Dictionary<int, (EnemyKind kind, bool second)> Known = new Dictionary<int, (EnemyKind, bool)>();
        private static float Get(Dictionary<int, float> d, int key) => d.TryGetValue(key, out float t) ? t : float.NegativeInfinity;

        // From a boss effect (BossFx), by its network id; its body gone (it died), from `where` if that's given.
        public static void Say(int id, Call c, Vector3? where = null)
        {
            var body = BossFx.BodyOf(id);
            var bb = body ? body.GetComponent<BossBody>() : null;
            if (bb) Known[id] = (bb.Kind, bb.Second);
            if (!body && where == null) return;
            var (kind, second) = Known.TryGetValue(id, out var k) ? k : (BossFx.KindOf(id), false);
            Say(body, where ?? body.position, kind, id, c, second);
        }

        // Idle and attack calls come only when it's quiet; a cry, a roar or its death cuts short whatever it was saying.
        public static void Say(Transform body, Vector3 at, EnemyKind kind, int id, Call c, bool second)
        {
            if (body) Known[id] = (kind, second);
            var lines = Lines(kind, c);
            if (lines.Length == 0) return;
            int key = Minion(kind) ? -(int)kind - 1 : id; // a kind's minions take turns
            float now = Time.time, busy = Get(BusyUntil, key);
            switch (c)
            {
                case Call.Idle: if (now < busy + 1.5f) return; break;
                case Call.Attack:
                    if (now < busy || now < Get(NextAttack, key)) return;
                    NextAttack[key] = now + (Minion(kind) ? 0.8f : 2.5f);
                    break;
                case Call.Hurt:
                    if (now < Get(NextHurt, key)) return;
                    NextHurt[key] = now + 1.2f;
                    break;
                case Call.Roar: // (the second form roars as it changes and again as it starts: once is enough)
                    if (now - Get(RoaredAt, key) < 1.5f) return;
                    RoaredAt[key] = now;
                    break;
            }
            BossAudio.Hush(key);
            int gen = Latest[key] = (Latest.TryGetValue(key, out int g) ? g : 0) + 1;
            float longest = 0f;
            foreach (var l in lines) longest = Mathf.Max(longest, l.Delay + (l.Repeat - 1) * l.Gap + (l.Max > 0f ? l.Max : 1.5f));
            BusyUntil[key] = now + longest;
            float deeper = second ? 0.92f : 1f;
            foreach (var l in lines)
                for (int i = 0; i < Mathf.Max(1, l.Repeat); i++)
                {
                    var line = l; int n = i;
                    void Go()
                    {
                        if (Latest[key] != gen) return;
                        BossAudio.Play(line.N > 0 ? line.Clip + Random.Range(1, line.N + 1) : line.Clip, null, body ? body.position : at, line.Vol,
                                       (line.Pitch + line.Step * n + Random.Range(-line.Spread, line.Spread)) * deeper, AudioDistance.Long, body, line.Max, true, key);
                    }
                    float wait = l.Delay + i * l.Gap;
                    if (wait <= 0f) Go(); else BossAudio.After(wait, Go);
                }
        }
    }
}
