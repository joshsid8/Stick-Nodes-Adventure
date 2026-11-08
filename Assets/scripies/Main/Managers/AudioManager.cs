using System;
using System.Collections;
using scripies.Core;
using scripies.Player;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class AudioManager : MonoBehaviour, IAudioStatus
{
    public static AudioManager Instance { get; private set; }
    public Scene previousScene;
    public IAudioStatus.MusicType musicType { get; set; }

    public enum SyncUnit
    {
        Bar,
        Beat
    }

    public enum LoopSyncMode
    {
        StartFromBeginning,
        ContinueBarProgression
    }

    [Serializable]
    public struct TrackBpmData
    {
        public AudioClip clip;
        [Tooltip("Manual Beats Per Minute (BPM) for this track.")]
        public float bpm;
        [Tooltip("Number of beats in 1 bar (default 4 for 4/4 time).")]
        public int beatsPerBar;
        [Tooltip("If true, switching TO this track ignores beat/bar sync and starts immediately.")]
        public bool playImmediately;
    }

    public AudioSource musicSource;
    [Tooltip("Optional secondary source for sample-accurate bar transitions. Auto-created if left empty.")]
    public AudioSource secondaryMusicSource;
    public AudioSource globalSfxSource;

    public AudioClip[] backgroundMusic;
    public float[] backgroundMusicVolume;
    [Tooltip("Manual BPM for each slot in backgroundMusic (0 = Area, 1 = Combat, 2 = Boss).")]
    public float[] backgroundMusicBpm = { 130f, 130f, 130f };
    [Tooltip("If true for a slot (0 = Area, 1 = Combat, 2 = Boss), switching to that slot skips beat sync and plays immediately.")]
    public bool[] playSlotImmediately = { false, false, true };

    [Header("Musical Bar / Beat Sync")]
    [Tooltip("Assign BPM per AudioClip here so any track's BPM is known automatically whenever it plays.")]
    public TrackBpmData[] trackBpmData;
    [Tooltip("Fallback BPM if a track has no entry in trackBpmData or backgroundMusicBpm.")]
    public float defaultBpm = 130f;
    public int beatsPerBar = 4;
    public float musicInterval;
    [Tooltip("Seconds per musical bar, calculated from BPM: (60 / BPM) * beatsPerBar")]
    public float currentBarTimer = 1.8461538f;
    [Tooltip("Seconds per beat, calculated from BPM: 60 / BPM")]
    public float currentBeatTimer = 0.4615385f;
    public SyncUnit syncUnit = SyncUnit.Bar;
    public LoopSyncMode loopSyncMode = LoopSyncMode.StartFromBeginning;

    [Header("Live Sync Readout")]
    public float activeTrackBpm = 130f;
    public int currentBarIndex;
    public int currentBeatIndex;
    public float timeUntilNextBar;

    [FormerlySerializedAs("sfxMusic")]
    public AudioClip[] backupSfx;

    public GameObject[] gUI;
    private Coroutine fadeCoroutine;
    private bool isSwitchScheduled;
    private AudioClip lastCalculatedClip;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);
        EnsureSecondaryMusicSource();

        if (backgroundMusic.Length > 2 && backgroundMusic[1] == null && backgroundMusic[2] == null ||
            backgroundMusic.Length > 0 && musicSource.clip == null)
            PlayMusic(0, false);
    }

    private void EnsureSecondaryMusicSource()
    {
        if (secondaryMusicSource != null || musicSource == null) return;
        var go = new GameObject("SecondaryMusicSource");
        go.transform.SetParent(transform);
        secondaryMusicSource = go.AddComponent<AudioSource>();
        secondaryMusicSource.playOnAwake = false;
        secondaryMusicSource.loop = musicSource.loop;
        secondaryMusicSource.outputAudioMixerGroup = musicSource.outputAudioMixerGroup;
        secondaryMusicSource.spatialBlend = musicSource.spatialBlend;
        secondaryMusicSource.priority = musicSource.priority;
    }

    private void OnEnable()
    {
        GameEvents.PlayMusic += PlayMusic;
        GameEvents.PlaySound += PlaySfx;
        GameEvents.OnDisableGUI += QuietOnGUI;
        GameEvents.OnEnableGUI += QuietOnGUI;
        GameEvents.MuteMusic += ToggleMuteMusic;
    }

    private void OnDisable()
    {
        GameEvents.PlayMusic -= PlayMusic;
        GameEvents.PlaySound -= PlaySfx;
        GameEvents.OnDisableGUI -= QuietOnGUI;
        GameEvents.OnEnableGUI -= QuietOnGUI;
        GameEvents.MuteMusic -= ToggleMuteMusic;
        StopAudio();
    }

    private void OnDestroy()
    {
        StopAudio();
    }

    public void StopAudio()
    {
        CancelPendingScheduledSwitch();
        if (musicSource != null) musicSource.Stop();
        if (secondaryMusicSource != null) secondaryMusicSource.Stop();
        if (globalSfxSource != null) globalSfxSource.Stop();
    }

    private void Update()
    {
        UpdateMusicalClock();
    }
    
    #region Music
    
    // Hella vibecoded by gemini
    private void UpdateMusicalClock()
    {
        if (musicSource != null && musicSource.isPlaying && musicSource.clip != null && musicSource.clip.frequency > 0)
        {
            if (musicSource.clip != lastCalculatedClip)
                UpdateBarTimerForClip(musicSource.clip, (int)musicType);

            // Sample-accurate playback position inside the current loop
            musicInterval = (float)musicSource.timeSamples / musicSource.clip.frequency;
        }
        else
        {
            musicInterval += Time.deltaTime;
        }

        var activeBarLength = GetActiveBarLength();
        if (activeBarLength > 0.01f)
        {
            currentBarIndex = Mathf.FloorToInt(musicInterval / activeBarLength);
            var elapsedInBar = musicInterval % activeBarLength;
            timeUntilNextBar = activeBarLength - elapsedInBar;

            var beatLength = activeBarLength / Mathf.Max(1, beatsPerBar);
            currentBeatIndex = Mathf.FloorToInt(elapsedInBar / beatLength) + 1;
        }
    }

    /// <summary>
    /// Resolves the manual BPM and beats-per-bar for a track and calculates its exact bar length in seconds.
    /// Priority:
    /// 1. trackBpmData list (matched by AudioClip reference or name)
    /// 2. backgroundMusicBpm[trackIndex]
    /// 3. defaultBpm
    /// </summary>
    public float CalculateBarLengthForClip(AudioClip clip, int trackIndex, out float resolvedBpm, out int resolvedBeatsPerBar)
    {
        resolvedBpm = defaultBpm > 0f ? defaultBpm : 130f;
        resolvedBeatsPerBar = Mathf.Max(1, beatsPerBar);

        var foundInDatabase = false;

        // 1. Check the TrackBpmData list for this specific AudioClip
        if (clip != null && trackBpmData != null)
        {
            for (var i = 0; i < trackBpmData.Length; i++)
            {
                var entry = trackBpmData[i];
                if (entry.clip == null) continue;
                if (entry.clip == clip || entry.clip.name == clip.name)
                {
                    if (entry.bpm > 0f)
                    {
                        resolvedBpm = entry.bpm;
                        foundInDatabase = true;
                    }
                    if (entry.beatsPerBar > 0)
                        resolvedBeatsPerBar = entry.beatsPerBar;
                    break;
                }
            }
        }

        // 2. Otherwise check the per-slot BPM array (0 = Area, 1 = Combat, 2 = Boss)
        if (!foundInDatabase && backgroundMusicBpm != null && trackIndex >= 0 && trackIndex < backgroundMusicBpm.Length && backgroundMusicBpm[trackIndex] > 0f)
        {
            resolvedBpm = backgroundMusicBpm[trackIndex];
        }

        var beatLength = 60f / resolvedBpm;
        return beatLength * resolvedBeatsPerBar;
    }

    public void UpdateBarTimerForClip(AudioClip clip, int trackIndex = -1)
    {
        lastCalculatedClip = clip;
        currentBarTimer = CalculateBarLengthForClip(clip, trackIndex, out activeTrackBpm, out var trackBeatsPerBar);
        beatsPerBar = trackBeatsPerBar;
        currentBeatTimer = 60f / Mathf.Max(1f, activeTrackBpm);
    }

    public float GetActiveBarLength()
    {
        return Mathf.Max(0.05f, currentBarTimer);
    }

    public void SetBarLengthFromBPM(float bpm, int beatsInBar = 4)
    {
        if (bpm <= 0f) return;
        beatsPerBar = Mathf.Max(1, beatsInBar);
        activeTrackBpm = bpm;
        currentBeatTimer = 60f / bpm;
        currentBarTimer = currentBeatTimer * beatsPerBar;
    }

    

    public bool ShouldPlayImmediately(AudioClip clip, int trackIndex)
    {
        if (clip != null && trackBpmData != null)
        {
            for (var i = 0; i < trackBpmData.Length; i++)
            {
                var entry = trackBpmData[i];
                if (entry.clip == null) continue;
                if (entry.clip == clip || entry.clip.name == clip.name)
                {
                    if (entry.playImmediately) return true;
                    break;
                }
            }
        }

        if (playSlotImmediately != null && trackIndex >= 0 && trackIndex < playSlotImmediately.Length)
            return playSlotImmediately[trackIndex];

        return false;
    }

    [ContextMenu("Play music in roster")] public void PlayRandomMusic() => PlayMusic(Random.Range(0, 3), false);

    private void CancelPendingScheduledSwitch()
    {
        if (!isSwitchScheduled) return;
        isSwitchScheduled = false;

        if (secondaryMusicSource != null)
            secondaryMusicSource.Stop();

        // Reset scheduled end time on the currently playing musicSource so it continues looping
        if (musicSource != null && musicSource.isPlaying)
            musicSource.SetScheduledEndTime(AudioSettings.dspTime + 3600000.0);
    }

    public void PlayMusic(int index, bool fade, bool immediate = false)
    {
        var keepMutedInSection = SectionManager.ActiveSection != null && SectionManager.ActiveSection.MuteMusic && index != (int)IAudioStatus.MusicType.Boss;
        ToggleMuteMusic(keepMutedInSection);
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        CancelPendingScheduledSwitch();
        fadeCoroutine = StartCoroutine(BeginMusic(index, fade, immediate));
    }

    private IEnumerator BeginMusic(int index, bool fade, bool immediate = false)
    {
        if (index < 0 || index >= backgroundMusic.Length) yield break;
        var nextClip = backgroundMusic[index];
        if (nextClip == null) yield break;

        var targetVolume = index < backgroundMusicVolume.Length ? backgroundMusicVolume[index] : 0.5f;

        // If the requested clip is already playing on musicSource, just ensure state/volume matches
        if (musicSource.isPlaying && musicSource.clip == nextClip)
        {
            musicType = (IAudioStatus.MusicType)index;
            UpdateBarTimerForClip(nextClip, index);
            if (fade && musicSource.volume < targetVolume)
                yield return StartCoroutine(FadeInMusic(2f));
            else
                musicSource.volume = targetVolume;

            fadeCoroutine = null;
            yield break;
        }

        // Case 1: Fade transition requested while music is playing
        if (fade && musicSource.isPlaying)
        {
            yield return StartCoroutine(FadeOutMusic());
            musicSource.clip = nextClip;
            musicType = (IAudioStatus.MusicType)index;
            UpdateBarTimerForClip(nextClip, index);
            musicSource.time = 0f;
            musicSource.Play();
            musicInterval = 0f;
            yield return StartCoroutine(FadeInMusic(2f));
            fadeCoroutine = null;
            yield break;
        }

        // Case 2: Nothing is currently playing OR immediate flag is set -> start immediately without waiting for a bar/beat
        var skipBeatSync = immediate || ShouldPlayImmediately(nextClip, index);
        if (skipBeatSync || !musicSource.isPlaying || musicSource.clip == null)
        {
            musicSource.Stop();
            musicSource.clip = nextClip;
            musicType = (IAudioStatus.MusicType)index;
            UpdateBarTimerForClip(nextClip, index);
            musicSource.volume = fade ? 0f : targetVolume;
            musicSource.time = 0f;
            musicSource.Play();
            musicInterval = 0f;

            if (fade) yield return StartCoroutine(FadeInMusic(2f));
            fadeCoroutine = null;
            yield break;
        }

        // Case 3: Bar/Beat-synced seamless transition using the CURRENT track's bar length,
        // then updating currentBarTimer to the NEW track's bar length as soon as the handoff occurs!
        EnsureSecondaryMusicSource();

        var outgoingBarLength = GetActiveBarLength();
        var syncLength = syncUnit == SyncUnit.Beat
            ? outgoingBarLength / Mathf.Max(1, beatsPerBar)
            : outgoingBarLength;

        var sampleRate = musicSource.clip.frequency > 0 ? (double)musicSource.clip.frequency : 44100.0;
        var currentClipTime = (double)musicSource.timeSamples / sampleRate;
        var elapsedInInterval = currentClipTime % syncLength;
        var timeToWait = syncLength - elapsedInInterval;

        // If we are already within 15ms of the bar boundary, switch immediately
        if (timeToWait < 0.015) timeToWait = 0.0;

        var incomingBarLength = CalculateBarLengthForClip(nextClip, index, out _, out _);

        if (secondaryMusicSource != null && timeToWait > 0.0)
        {
            var switchDspTime = AudioSettings.dspTime + timeToWait;

            secondaryMusicSource.Stop();
            secondaryMusicSource.clip = nextClip;
            secondaryMusicSource.volume = targetVolume;
            secondaryMusicSource.loop = musicSource.loop;
            secondaryMusicSource.mute = musicSource.mute;

            if (loopSyncMode == LoopSyncMode.ContinueBarProgression && outgoingBarLength > 0.01f)
            {
                var nextBarNumber = Mathf.FloorToInt((float)((currentClipTime + timeToWait) / outgoingBarLength));
                secondaryMusicSource.time = (nextBarNumber * incomingBarLength) % nextClip.length;
            }
            else
            {
                secondaryMusicSource.time = 0f;
            }

            // Schedule sample-accurate handoff on the audio thread
            musicSource.SetScheduledEndTime(switchDspTime);
            secondaryMusicSource.PlayScheduled(switchDspTime);
            isSwitchScheduled = true;

            while (AudioSettings.dspTime < switchDspTime)
            {
                yield return null;
            }

            isSwitchScheduled = false;
            musicSource.Stop();

            // Swap active musicSource so all external references point to the playing source
            (musicSource, secondaryMusicSource) = (secondaryMusicSource, musicSource);
            musicType = (IAudioStatus.MusicType)index;
            UpdateBarTimerForClip(nextClip, index);
            musicInterval = musicSource.time;
        }
        else
        {
            if (timeToWait > 0.0)
                yield return new WaitForSecondsRealtime((float)timeToWait);

            musicSource.clip = nextClip;
            musicType = (IAudioStatus.MusicType)index;
            UpdateBarTimerForClip(nextClip, index);
            musicSource.volume = targetVolume;
            musicSource.time = 0f;
            musicSource.Play();
            musicInterval = 0f;
        }

        fadeCoroutine = null;
    }

    public IEnumerator FadeInMusic(float fadeTime = 1)
    {
        var targetVolume = backgroundMusicVolume[(int)musicType];
        while (musicSource.volume < targetVolume)
        {
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, targetVolume, Time.deltaTime * fadeTime);
            yield return null;
        }

        musicSource.volume = targetVolume;
    }

    public IEnumerator FadeOutMusic(float fadeTime = 2)
    {
        while (musicSource.volume > 0)
        {
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, 0, Time.deltaTime * fadeTime);
            yield return null;
        }

        musicSource.volume = 0;
    }

    // Junie worked here, making fading work properly. learn how all this works passively once you learn more about coroutines
    public void TriggerFadeIn(float fadeTime = 1)
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        CancelPendingScheduledSwitch();
        fadeCoroutine = StartCoroutine(FadeInCoroutine(fadeTime));
    }

    public void TriggerFadeOut(float fadeTime = 1)
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        CancelPendingScheduledSwitch();
        fadeCoroutine = StartCoroutine(FadeOutCoroutine(fadeTime));
    }

    private IEnumerator FadeInCoroutine(float fadeTime)
    {
        yield return FadeInMusic(fadeTime);
        fadeCoroutine = null;
    }

    private IEnumerator FadeOutCoroutine(float fadeTime)
    {
        yield return FadeOutMusic(fadeTime);
        fadeCoroutine = null;
    }

    public void ToggleMuteMusic(bool toggle = true)
    {
        if (musicSource != null) musicSource.mute = toggle;
        if (secondaryMusicSource != null) secondaryMusicSource.mute = toggle;
    }

    private void QuietOnGUI()
    {
        foreach (var g in gUI)
        {
            if (g.activeInHierarchy)
            {
                musicSource.volume = backgroundMusicVolume[(int)musicType] / 2;
                globalSfxSource.volume = 0.2f;
            }
            else
            {
                musicSource.volume = backgroundMusicVolume[(int)musicType];
                globalSfxSource.volume = 1f;
            }
        }
    }

    #endregion

    #region SFX

    private void PlaySfx(AudioClip clip, GameObject source = null, float volume = 1f, bool randomPitch = false)
    {
        var pos = source != null ? source.transform.position : globalSfxSource.transform.position;
        var sfxSource = Instantiate(globalSfxSource, pos, Quaternion.identity);
        if (source != null)
        {
            SceneManager.MoveGameObjectToScene(sfxSource.gameObject, source.scene);
            StartCoroutine(UpdateSfxVolume(source, sfxSource, volume));
        }
        else sfxSource.volume = volume;

        sfxSource.clip = clip != null ? clip : backupSfx[Random.Range(0, backupSfx.Length)];
        sfxSource.pitch = randomPitch ? Random.Range(0.85f, 1.15f) : 1;

        sfxSource.Play();
        Destroy(sfxSource.gameObject, sfxSource.clip.length);
    }

    private static IEnumerator UpdateSfxVolume(GameObject source, AudioSource sfxSource, float volume = 1f)
    {
        const float maxDistance = 65;

        while (!source.IsDestroyed() && !sfxSource.IsDestroyed() && PlayerHealth.Instance != null)
        {
            var distToPlayer =
                Mathf.Abs(PlayerHealth.Instance.gameObject.transform.position.x - source.transform.position.x);
            var distancePercentage = Mathf.Clamp01(distToPlayer / maxDistance);
            //print($"{source.name} is {distancePercentage * 100}% away from player");
            var vol = volume * (1 - distancePercentage);
            sfxSource.volume = vol;

            yield return null;
        }
    }
    #endregion
}
