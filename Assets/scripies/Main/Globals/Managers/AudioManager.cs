using System.Collections;
using scripies.Core;
using UnityEngine;

public class AudioManager : MonoBehaviour, IAudioStatus
{
    public static AudioManager Instance {get; private set;}

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
        
        GameEvents.PlayMusic += PlayMusic;
        GameEvents.PlaySound += PlaySfx;
        GameEvents.OnDisableGUI += QuietOnGUI;
        GameEvents.OnEnableGUI += QuietOnGUI;
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
        musicSource.clip = backgroundMusic[index];
        musicSource.volume = backgroundMusicVolume[index];
        musicType = (IAudioStatus.MusicType) index;
        musicSource.Play();
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

    private void PlaySfx(AudioClip clip = null)
    {
        sfxSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
        sfxSource.PlayOneShot(clip ?? sFXMusic[0]);
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

}
