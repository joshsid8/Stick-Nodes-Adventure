using System;
using System.Collections;
using scripies.Core;
using UnityEngine;
using Random = UnityEngine.Random;

public class AudioManager : MonoBehaviour, IAudioStatus
{
    public static AudioManager Instance {get; private set;}
    public string currentScene;
    public IAudioStatus.MusicType musicType { get; set; }
    
    public AudioSource musicSource;
    public AudioSource sfxSource;
    
    public AudioClip[] backgroundMusic;
    public float[] backgroundMusicVolume;
    public AudioClip[] sFXMusic;
    
    public GameObject[] gUI;
    //private bool toggle;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        DontDestroyOnLoad(gameObject);
        GameEvents.PlayMusic += PlayMusic;
        GameEvents.PlaySound += PlaySfx;
        GameEvents.OnDisableGUI += QuietOnGUI;
        GameEvents.OnEnableGUI += QuietOnGUI;
        
        if (backgroundMusic[1] == null && backgroundMusic[2] == null) PlayMusic(0, false);
    }

    private void PlayMusic(int index, bool fade)
    {
        StartCoroutine(BeginMusic(index, fade));
    }
    
    private IEnumerator BeginMusic(int index, bool fade)
    {
        //if (fade) StartCoroutine(FadeOutMusic()); else toggle = true;
        //yield return new WaitUntil(() => toggle);
        //toggle = false;
        if (musicSource.clip != backgroundMusic[index])
        {
            musicSource.clip = backgroundMusic[index];
            musicSource.volume = backgroundMusicVolume[index];
            musicType = (IAudioStatus.MusicType)index;
            musicSource.Play();
        }
        yield return null;
    }
    
    public IEnumerator FadeOutMusic(float fadeTime = 2)
    {
        while (musicSource.volume > 0)
        {
            musicSource.volume -= Time.deltaTime * fadeTime;
            yield return null;
        }
        //toggle = true;
    }

    private void PlaySfx(AudioClip clip = null, bool randomPitch = true)
    {
        if (randomPitch) sfxSource.pitch = Random.Range(0.9f, 1.1f);
        sfxSource.PlayOneShot(clip ?? sFXMusic[Random.Range(0, sFXMusic.Length)]);
    }
    
    private void QuietOnGUI()
    {
        foreach (var g in gUI)
        {
            if (g.activeInHierarchy)
            {
                musicSource.volume = backgroundMusicVolume[(int)musicType] / 2;
                sfxSource.volume = 0.2f;
            }
            else
            {
                musicSource.volume = backgroundMusicVolume[(int)musicType];
                sfxSource.volume = 1f;
            }
        }
    }

    public void Save(ref SceneName data) => data.sceneName = currentScene;
    public void Load(SceneName data) => currentScene = data.sceneName;
}

[System.Serializable]
public struct SceneName
{
    public string sceneName;
}
