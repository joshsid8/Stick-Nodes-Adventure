using System;
using System.Collections;
using scripies.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class AudioManager : MonoBehaviour, IAudioStatus
{
    public static AudioManager Instance {get; private set;}
    public string currentScene;
    public Scene previousScene;
    public IAudioStatus.MusicType musicType { get; set; }
    
    public AudioSource musicSource;
    public AudioSource sfxSource;
    
    public AudioClip[] backgroundMusic;
    public float[] backgroundMusicVolume;
    public AudioClip[] sFXMusic;
    
    public GameObject[] gUI;
    private Coroutine fadeCoroutine;

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
        
        if (backgroundMusic.Length > 2 && backgroundMusic[1] == null && backgroundMusic[2] == null || backgroundMusic.Length > 0 && musicSource.clip == null) 
            PlayMusic(0, false);
    }

    private void PlayMusic(int index, bool fade)
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(BeginMusic(index, fade));
    }
    
    private IEnumerator BeginMusic(int index, bool fade)
    {
        if (index < 0 || index >= backgroundMusic.Length) yield break;
        
        var targetVolume = backgroundMusicVolume[index];

        if (fade && musicSource.isPlaying) yield return StartCoroutine(FadeOutMusic(2f));
        
        if (musicSource.clip != backgroundMusic[index])
        {
            musicSource.clip = backgroundMusic[index];
            musicType = (IAudioStatus.MusicType)index;
            musicSource.Play();
        }

        if (fade) yield return StartCoroutine(FadeInMusic(2f));
        else musicSource.volume = targetVolume;
        
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
        fadeCoroutine = StartCoroutine(FadeInCoroutine(fadeTime));
    }

    public void TriggerFadeOut(float fadeTime = 1)
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
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

    private void PlaySfx(AudioClip clip = null, bool randomPitch = true, GameObject source = null)
    {
        if (randomPitch) sfxSource.pitch = Random.Range(0.9f, 1.1f);
        
        if (source == null) sfxSource.PlayOneShot(clip ?? sFXMusic[Random.Range(0, sFXMusic.Length)]);
        else AudioSource.PlayClipAtPoint(clip ?? sFXMusic[Random.Range(0, sFXMusic.Length)], source.transform.position);
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
