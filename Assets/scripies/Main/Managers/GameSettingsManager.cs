using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class GameSettingsManager : MonoBehaviour
{
    public static GameSettingsManager Instance {get; private set;}
    
    private GameMenuManager menuManager;
    [SerializeField] private GameObject settingsMenu;
    public bool isActive;
    [SerializeField] private bool debugMode;
    [SerializeField] private float fps = 60;
    [SerializeField] private bool vSync;
    public AudioMixerSettings audioMixerSettings = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        SaveSystem.LoadSettings();
        ApplyAllSettings();
        SyncSliders();
    }

    private void OnEnable()
    {
        menuManager = GameMenuManager.Instance ?? FindAnyObjectByType<GameMenuManager>();
    }

    private static float LinearToDecibels(float volume)
    {
        return Mathf.Log10(Mathf.Clamp(volume, 0.0001f, 1f)) * 20f;
    }

    // We don't know what Log10 is, but apparently it just makes volume more "linear" when being set
    public void SetMasterVolume(float volume)
    {
        audioMixerSettings.masterVolume = Mathf.Clamp01(volume);
        if (audioMixerSettings.audioMixer != null)
            audioMixerSettings.audioMixer.SetFloat("MasterVolume", LinearToDecibels(volume));
    }

    public void SetMusicVolume(float volume)
    {
        audioMixerSettings.musicVolume = Mathf.Clamp01(volume);
        if (audioMixerSettings.audioMixer != null)
            audioMixerSettings.audioMixer.SetFloat("MusicVolume", LinearToDecibels(volume));
    }

    public void SetSfxVolume(float volume)
    {
        audioMixerSettings.sfxVolume = Mathf.Clamp01(volume);
        if (audioMixerSettings.audioMixer != null)
            audioMixerSettings.audioMixer.SetFloat("SfxVolume", LinearToDecibels(volume));
    }
    
    public void ToggleSettings(bool toggle)
    {
        var wasActive = isActive;
        isActive = toggle;
        if (settingsMenu != null) settingsMenu.SetActive(toggle);
        if (toggle) SyncSliders();
        else if (wasActive) SaveSystem.SaveSettings();

        menuManager = GameMenuManager.Instance ?? FindAnyObjectByType<GameMenuManager>();
        if (menuManager != null && menuManager.pausedGame) menuManager.gameMenu.SetActive(!toggle);
    }

    private void ApplyAllSettings()
    {
        SetMasterVolume(audioMixerSettings.masterVolume);
        SetMusicVolume(audioMixerSettings.musicVolume);
        SetSfxVolume(audioMixerSettings.sfxVolume);
        QualitySettings.vSyncCount = vSync ? 1 : 0;
        if (fps > 0) Application.targetFrameRate = Mathf.RoundToInt(fps);
    }

    private void SyncSliders()
    {
        if (settingsMenu == null) return;
        foreach (var slider in settingsMenu.GetComponentsInChildren<Slider>(true))
        {
            var count = slider.onValueChanged.GetPersistentEventCount();
            for (var i = 0; i < count; i++)
            {
                var method = slider.onValueChanged.GetPersistentMethodName(i);
                switch (method)
                {
                    case nameof(SetMasterVolume):
                        slider.SetValueWithoutNotify(audioMixerSettings.masterVolume);
                        break;
                    case nameof(SetMusicVolume):
                        slider.SetValueWithoutNotify(audioMixerSettings.musicVolume);
                        break;
                    case nameof(SetSfxVolume):
                        slider.SetValueWithoutNotify(audioMixerSettings.sfxVolume);
                        break;
                }
            }
        }
    }

    public void Save(ref MainMenuData data)
    {
        data.fps = fps > 0 ? Mathf.RoundToInt(fps) : 60;
        data.vsync = vSync;
        data.masterVolume = audioMixerSettings.masterVolume;
        data.musicVolume = audioMixerSettings.musicVolume;
        data.sfxVolume = audioMixerSettings.sfxVolume;
        data.initialized = true;
    }

    public void Load(MainMenuData data)
    {
        if (!data.initialized) return;
        fps = data.fps > 0 ? data.fps : 60;
        vSync = data.vsync;
        audioMixerSettings.masterVolume = data.masterVolume;
        audioMixerSettings.musicVolume = data.musicVolume;
        audioMixerSettings.sfxVolume = data.sfxVolume;
        ApplyAllSettings();
        SyncSliders();
    }
    
    [Serializable]
    public class AudioMixerSettings
    {
        public AudioMixer audioMixer;
        public float masterVolume = 1;
        public float musicVolume = 1;
        public float sfxVolume = 1;
    }
}
