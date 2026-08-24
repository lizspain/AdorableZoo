using UnityEngine;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Music ducking + single-voice SFX (design doc, Audio Architecture section 9). All SFX
    /// triggers come from AnimalController polling its own Animator's current state, never
    /// directly from raw input, so sound stays locked to what's actually animating on screen --
    /// see AnimalController.PollAnimatorAudio. The vendor Animator Controllers can't be edited
    /// here (no Animation Events/StateMachineBehaviours to hook), so state-transition polling is
    /// the code-only equivalent: the same guarantee (audio can't drift from the animation), just
    /// detected by watching ControllerPetZoo.GetCurrentState() change rather than an authored event.
    ///
    /// A new SFX request always preempts and starts immediately, even over one still playing --
    /// no fade-out, no gap. An earlier version faded the old voice out and waited before starting
    /// the new one, which read as input lag (tap, then silence, then the sound). Only one voice
    /// plays at a time, it just switches instantly instead of crossfading.
    ///
    /// Implemented as a MonoBehaviour rather than the roadmap's "plain C# class" -- ducking needs
    /// a continuous per-frame volume lerp, which requires Unity's Update() lifecycle that a plain
    /// class doesn't have access to.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        [Header("Music Ducking")]
        [SerializeField] private float fullMusicVolume = 0.8f;
        [SerializeField] private float duckedMusicVolume = 0.2f;
        [SerializeField] private float musicFadeSpeed = 2f;
        [Tooltip("Placeholder/silent until real music exists -- this phase wires the architecture, not the content.")]
        [SerializeField] private AudioClip musicClip;

        [Header("Tableau")]
        [Tooltip("Played once when the Offer Tableau appears (Care Meter completion) -- the zoo-wide beat, replacing each animal's own CelebrationSfx so N placed animals don't layer that clip on top of each other.")]
        [SerializeField] private AudioClip tableauAppearSfx;

        private AudioSource musicSource;
        private AudioSource tableauSfxSource;
        private AudioSource currentSfxVoice;

        private void Awake()
        {
            Instance = this;

            var musicPlayer = new GameObject("MusicPlayer");
            musicPlayer.transform.SetParent(transform, false);
            musicSource = musicPlayer.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.volume = fullMusicVolume;
            musicSource.clip = musicClip;

            var tableauSfxPlayer = new GameObject("TableauSfxPlayer");
            tableauSfxPlayer.transform.SetParent(transform, false);
            tableauSfxSource = tableauSfxPlayer.AddComponent<AudioSource>();
            tableauSfxSource.playOnAwake = false;
        }

        private void Start()
        {
            if (musicSource.clip != null)
            {
                musicSource.Play();
            }
        }

        private void Update()
        {
            bool sfxPlaying = currentSfxVoice != null && currentSfxVoice.isPlaying;
            float target = sfxPlaying ? duckedMusicVolume : fullMusicVolume;
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, target, musicFadeSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Requests source play clip as the single dominant SFX voice, regardless of which
        /// animal's own AudioSource each request comes from. Starts immediately -- if a different
        /// source is currently playing, it's cut off outright rather than faded/delayed, so the
        /// new sound never lags behind the input that triggered it.
        /// </summary>
        public void PlaySfx(AudioSource source, AudioClip clip)
        {
            if (source == null || clip == null) return;

            if (currentSfxVoice != null && currentSfxVoice.isPlaying && currentSfxVoice != source)
            {
                currentSfxVoice.Stop();
            }

            currentSfxVoice = source;
            source.volume = 1f;
            source.clip = clip;
            source.Play();
        }

        /// <summary>Called by OfferTableauController when the tableau actually appears -- the single zoo-wide
        /// beat for Care Meter completion, in place of every placed animal playing its own CelebrationSfx at once.</summary>
        public void PlayTableauFanfare()
        {
            PlaySfx(tableauSfxSource, tableauAppearSfx);
        }
    }
}
