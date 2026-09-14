using System;
using UnityEngine;
using UnityEngine.Audio;

public class GameSettingsManager : MonoBehaviour
{
    public static GameSettingsManager Instance {get; private set;}
    
    private GameMenuManager menuManager;
    
    public bool isActive;
    [SerializeField] private bool debugMode;
    [SerializeField] private float fps;
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
        menuManager = GameMenuManager.Instance ?? FindAnyObjectByType<GameMenuManager>();
    }
    
    // We don't know what Log10 is, but apparently it just makes volume more "linear" when being set
    public void SetMasterVolume(float volume) => audioMixerSettings.audioMixer.SetFloat("MasterVolume", Mathf.Log10(volume) * 20);
    public void SetMusicVolume(float volume) => audioMixerSettings.audioMixer.SetFloat("MusicVolume", Mathf.Log10(volume) * 20);
    public void SetSfxVolume(float volume) => audioMixerSettings.audioMixer.SetFloat("SfxVolume", Mathf.Log10(volume) * 20);
    
    public void ToggleSettings(bool toggle)
    {
        isActive = toggle;
        gameObject.SetActive(toggle);
        if (menuManager.pausedGame) menuManager.gameMenu.SetActive(!toggle);
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
