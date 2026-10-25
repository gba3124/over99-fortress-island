using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HtfFortress
{
    // The village keepers, the game's own people in its own outfits (Person.cs): the shopkeeper at the fish stall, the
    // grillmaster's apron and glasses at the smithy, the soldier at the vault, the lighthouse keeper (his cap, beard and
    // pipe) by the start circle. Look at one
    // and press E: the game's own outline and "Talk" prompt, then the station's action. Each client builds them locally;
    // they turn to whoever walks up, look at them and wave while they talk, in the voice of one of the game's NPCs.
    public static class Keepers
    {
        // `role` is also which person: Market, Smith, Vault, Lighthouse. (`clothes` was the game outfit a copied NPC wore.)
        public static Keeper Make(Transform parent, Vector3 pos, float yaw, string clothes, string role, Action talk)
        {
            var root = new GameObject("Keeper_" + role);
            root.SetActive(false);
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var person = Person.Make(root.transform, role);

            var k = root.AddComponent<Keeper>();
            k.Role = role;
            k.Talk = talk;
            k.Person = person;
            k.Voice = Voice(root);
            // The game's own interaction: a collider tagged and layered "Interactable", and our Interactable on it.
            var talkGo = new GameObject("Talk");
            talkGo.transform.SetParent(root.transform, false);
            talkGo.transform.localPosition = new Vector3(0f, 0.95f, 0f); // the game's line-of-sight check aims at this point
            talkGo.layer = LayerMask.NameToLayer("Interactable");
            talkGo.tag = "Interactable";
            var col = talkGo.AddComponent<CapsuleCollider>();
            col.center = Vector3.zero;
            col.height = 1.9f;
            col.radius = 0.45f;
            var text = new GameObject("TextTarget").transform;
            text.SetParent(root.transform, false);
            text.localPosition = new Vector3(0f, 2.15f, 0f);
            var it = talkGo.AddComponent<KeeperTalk>();
            it.Keeper = k;
            AccessTools.Field(typeof(Interactable), "_interactCol").SetValue(it, col);
            AccessTools.Field(typeof(Interactable), "_textTarget").SetValue(it, text);
            AccessTools.Field(typeof(Interactable), "_modelsToOutline").SetValue(it, person.Renderers);
            root.SetActive(true);
            return k;
        }

        // The babble of one of the island's NPCs, a little higher or lower for each keeper (none: they pop).
        private static AudioSource Voice(GameObject on)
        {
            var src = Resources.FindObjectsOfTypeAll<NPC>().Where(n => n && n.gameObject.scene.IsValid())
                               .Select(n => n.GetComponentInChildren<AudioSource>(true)).FirstOrDefault(a => a && a.clip);
            if (!src) return null;
            var a = on.AddComponent<AudioSource>();
            a.clip = src.clip;
            a.outputAudioMixerGroup = src.outputAudioMixerGroup;
            a.spatialBlend = src.spatialBlend;
            a.rolloffMode = src.rolloffMode;
            a.minDistance = src.minDistance;
            a.maxDistance = src.maxDistance;
            a.volume = src.volume;
            a.pitch = UnityEngine.Random.Range(0.85f, 1.2f);
            a.playOnAwake = false;
            return a;
        }
    }

    // Turns toward the local player when they come close (within its counter's reach), looks at them, breathes, and nods
    // and waves for a moment after being talked to.
    public class Keeper : MonoBehaviour
    {
        public string Role;
        public Action Talk;
        public Person Person;
        public AudioSource Voice;
        private float _yaw, _baseYaw, _talkUntil, _lookYaw, _lookPitch;
        private const float SeeRange = 7f, MaxTurn = 70f;
        // The lighthouse keeper walks a slow ring round the tower; when anyone comes close or talks to him he stops and faces them.
        private bool _wanders;
        private Vector3 _wanderCenter, _wanderTo;
        private float _wanderRadius = 5f, _wanderPause, _walkPhase, _nextWander, _wanderAngle;
        public void WanderAround(Vector3 center, float radius) { _wanders = true; _wanderCenter = center; _wanderRadius = radius; _wanderTo = transform.position; _wanderPause = 1f; Vector3 d = transform.position - center; _wanderAngle = Mathf.Atan2(d.z, d.x); }

        private void Start()
        {
            _baseYaw = _yaw = transform.eulerAngles.y;
            if (Voice) { Voice.Stop(); Voice.loop = true; }
        }

        public void Said()
        {
            _talkUntil = Time.time + 1.1f;
            if (Voice && Voice.clip) { Voice.time = UnityEngine.Random.Range(0f, Voice.clip.length); Voice.volume = 0.75f; Voice.Play(); } // the NPC's own babble
            else Fx.Sound("Pop", transform.position + Vector3.up * 1.6f, 0.5f, AudioDistance.Short);
            Talk?.Invoke();
        }

        private void LateUpdate()
        {
            if (!Person) return;
            var me = Player.LocalPlayer;
            Vector3? eye = me && me.Camera ? me.Camera.CamTransform.position : (Vector3?)null;
            bool near = eye is Vector3 e0 && (e0 - transform.position).sqrMagnitude < SeeRange * SeeRange;
            bool talking = Time.time < _talkUntil;
            bool moving = false;
            float speed = 0f;

            if (_wanders && !near && !talking)
            {
                if (_wanderPause > 0f) _wanderPause -= Time.deltaTime;
                else
                {
                    Vector3 flat = _wanderTo - transform.position; flat.y = 0f;
                    if (flat.sqrMagnitude < 0.22f)
                    {
                        // Stop and look around for a moment before the next slow lap.
                        _nextWander = UnityEngine.Random.Range(2.2f, 5.5f);
                        _wanderPause = _nextWander;
                        float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f), r = UnityEngine.Random.Range(_wanderRadius * 0.72f, _wanderRadius);
                        _wanderTo = _wanderCenter + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        _wanderTo.y = BossFx.Ground(_wanderTo);
                    }
                    else
                    {
                        Vector3 step = flat.normalized * 0.65f * Time.deltaTime;
                        Vector3 next = transform.position + Vector3.MoveTowards(Vector3.zero, flat, step.magnitude);
                        next.y = BossFx.Ground(next);
                        transform.position = next;
                        moving = true; speed = 0.65f;
                    }
                }
            }

            float want = _baseYaw, lookYaw = 0f, lookPitch = 0f;
            if (moving)
            {
                Vector3 d = _wanderTo - transform.position;
                want = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            else if (near && eye is Vector3 e)
            {
                Vector3 d = e - transform.position;
                float toward = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                want = _baseYaw + Mathf.Clamp(Mathf.DeltaAngle(_baseYaw, toward), -MaxTurn, MaxTurn);
                // The head finishes the turn and looks up (or down) at your eyes.
                Vector3 local = Person.transform.InverseTransformPoint(e) - Person.Eyes;
                lookYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -40f, 40f);
                lookPitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -25f, 25f);
            }
            _yaw = Mathf.MoveTowardsAngle(_yaw, want, (moving ? 180f : 140f) * Time.deltaTime);
            Person.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (moving) _walkPhase += Time.deltaTime * 8f;
            float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
            _lookYaw = Mathf.Lerp(_lookYaw, lookYaw, k);
            _lookPitch = Mathf.Lerp(_lookPitch, lookPitch, k);
            Person.Pose(_walkPhase, moving ? 1f : 0f, false, talking ? 1f : 0f, _lookYaw, _lookPitch + (talking ? Mathf.Sin(Time.time * 14f) * 6f : 0f));
            if (Voice && Voice.isPlaying && !talking) Voice.Stop();
        }
    }

    // E on a keeper: the game's "Talk [E]" under the station's name, and the outline while looked at.
    public class KeeperTalk : Interactable
    {
        public Keeper Keeper;
        private string _text;

        public override void Hover()
        {
            if (!_isHovering)
            {
                _text = $"{L10n.T("keeper" + Keeper.Role)}\n{L10n.T("keeperTalk")} {SpriteManager.GetPickUpInput()}";
                PlayerUI.SetLookAtText(_text, TextTarget);
            }
            base.Hover();
        }

        public override void UnHover()
        {
            PlayerUI.HideLookAtText(_text);
            base.UnHover();
        }

        public override void Interact(Player player)
        {
            base.Interact(player);
            if (player != Player.LocalPlayer) return;
            UnHover();
            Keeper.Said();
        }
    }
}
