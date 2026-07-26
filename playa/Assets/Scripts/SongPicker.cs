using UnityEngine;

namespace Playa
{
    // Proximity-gated song cycler. Walk within `activationRadius` of the DJ
    // booth, use ← / → (or scroll) to swap songs. Since all songs share the
    // same BPM (SongLibrary.DemoBPM), swapping is a cosmetic re-tint of the
    // booth glow + a title change on the HUD.
    public sealed class SongPicker : MonoBehaviour
    {
        public MusicBeat music;
        public Transform player;
        public Renderer boothGlowRenderer;
        public float activationRadius = 5f;

        Material boothGlowInstance;
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        float scrollAccum;

        void Awake()
        {
            if (music == null) music = FindAnyObjectByType<MusicBeat>();
            var rig = FindAnyObjectByType<PlayerRig>();
            if (player == null && rig != null) player = rig.transform;

            // Own material so the booth colour tracks selected song without
            // affecting other emissive materials in the scene.
            if (boothGlowRenderer != null)
            {
                boothGlowInstance = new Material(boothGlowRenderer.sharedMaterial)
                { name = "BoothGlow(Instance)" };
                boothGlowRenderer.sharedMaterial = boothGlowInstance;
            }
            ApplySongTint();
            if (music != null) music.SongChanged += _ => ApplySongTint();
        }

        void Update()
        {
            if (music == null || player == null) return;
            if (!PlayerNearby) return;

            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
                music.CycleSong(-1);
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                music.CycleSong(+1);

            scrollAccum += Input.mouseScrollDelta.y;
            while (scrollAccum >= 1f) { music.CycleSong(+1); scrollAccum -= 1f; }
            while (scrollAccum <= -1f) { music.CycleSong(-1); scrollAccum += 1f; }
        }

        public bool PlayerNearby
        {
            get
            {
                if (player == null) return false;
                Vector3 d = player.position - transform.position;
                d.y = 0f;
                return d.sqrMagnitude <= activationRadius * activationRadius;
            }
        }

        void ApplySongTint()
        {
            if (boothGlowInstance == null) return;
            var c = music.CurrentSong.accent;
            if (boothGlowInstance.HasProperty(EmissionColorId))
                boothGlowInstance.SetColor(EmissionColorId, c * 4.5f);
            if (boothGlowInstance.HasProperty(BaseColorId))
                boothGlowInstance.SetColor(BaseColorId, c * 0.5f);
            if (boothGlowInstance.HasProperty(ColorId))
                boothGlowInstance.SetColor(ColorId, c * 0.5f);
        }
    }
}
