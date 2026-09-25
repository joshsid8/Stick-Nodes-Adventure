using System;
using System.Collections;
using System.Collections.Generic;
using scripies.Core;
using scripies.Player;
using UnityEngine;
using Random = UnityEngine.Random;

public class WeatherManager : MonoBehaviour
{
    public enum WeatherType {Clear, Hot, Cold, Rainy}
    [EnumButtons] public WeatherType weatherType;
    public WeatherProperties[] weatherProperties = new WeatherProperties[3];
    
    private MapManager mm;
    private SectionManager sm;
    public bool freezeWeather;
    public float weatherDurationTimer = 60;
    [SerializeField] private bool randomWeather;
    [SerializeField] private float weatherChance = 10;
    public Dictionary<string, float> disasters = new(4);
    
    [SerializeField] private float earthquakeTimer = 0.2f;
    
    [SerializeField] private GameObject thunderbolt;
    [SerializeField] private float thunderTimer = 5;
    [SerializeField] private int thunderChance = 50;
    
    private readonly float[] preValues = new float[3];
    private PlayerHealth player;
    
    private void Awake()
    {
        mm = GetComponent<MapManager>();
        if (mm == null) sm = GetComponent<SectionManager>();
        
        // Initialise disasters
        disasters.Add("Temperature", 0);
        disasters.Add("Thunderstorm", 0);
        disasters.Add("Wind", 0);
        disasters.Add("Earthquake", 0);
    }

    private void Update()
    {
        if (freezeWeather) return;
        weatherDurationTimer -= Time.deltaTime;
        if (weatherDurationTimer <= 0) WeatherClockChime();
    }

    [ContextMenu("Set Weather to enum chosen")] public void WeatherClockChime()
    {
        ClearDisasters();
        
        weatherType = GetTargetWeather();
        
        TryGetComponent(out MapManager mapManager);
        if (mapManager != null && mapManager.activeMap) ChangeWeather();
        
        if (weatherType == WeatherType.Clear) 
            weatherDurationTimer = 60;
        else
        {
            var index = (int)weatherType - 1;
            weatherDurationTimer = index >= 0 && index < weatherProperties.Length ? weatherProperties[index].weatherDuration : 60;
        }
    }

    private void Start()
    {
        player = PlayerHealth.Instance;
        preValues[0] = player.regenerationMultiplier;
        preValues[1] = player.pc.movementSpeed;
    }

    public WeatherType GetTargetWeather()
    {
        var rng = Random.Range(0, 101);
        var targetWeather = weatherType;
        
        switch (randomWeather)
        {
            case true when rng <= weatherChance:
                targetWeather = mm.undiscoveredMap ? WeatherType.Clear : (WeatherType)Random.Range(1, 4);
                break;
            case true when weatherType != WeatherType.Clear:
            {
                var rng2 = Random.Range(0, 101);
                if (rng2 <= 50) targetWeather = WeatherType.Clear;
                break;
            }
        }
        return targetWeather;
    }

    public void ChangeWeather(WeatherType? forcedWeather = null)
    {
        if (forcedWeather.HasValue) weatherType = forcedWeather.Value;
        else weatherType = GetTargetWeather();
        
        var announce = (mm != null && mm.activeMap) || (sm != null && sm.announceDetails);
        
        switch (weatherType)
        {
            case WeatherType.Clear:
                if (mm != null) mm.UpdateNormalAreaInfo();
                else sm.UpdateNormalAreaInfo();
                break;
            case WeatherType.Hot:
                if (mm != null) mm.UpdateAreaInfo(weatherProperties[0].weatherMapName, weatherProperties[0].weatherMapDesc, weatherProperties[0].weatherTextColor,
                    weatherProperties[0].weatherBgColor, weatherProperties[0].weatherOst, weatherProperties[0].weatherOstVolume, announce);
                else sm.UpdateAreaInfo(weatherProperties[0].weatherMapName, weatherProperties[0].weatherTextColor,
                    weatherProperties[0].weatherBgColor, weatherProperties[0].weatherOst, weatherProperties[0].weatherOstVolume);
                break;
            case WeatherType.Cold:
                if (mm != null) mm.UpdateAreaInfo(weatherProperties[1].weatherMapName, weatherProperties[1].weatherMapDesc, weatherProperties[1].weatherTextColor,
                    weatherProperties[1].weatherBgColor, weatherProperties[1].weatherOst, weatherProperties[1].weatherOstVolume, announce);
                else sm.UpdateAreaInfo(weatherProperties[1].weatherMapName, weatherProperties[1].weatherTextColor,
                    weatherProperties[1].weatherBgColor, weatherProperties[1].weatherOst, weatherProperties[1].weatherOstVolume);
                break;
            case WeatherType.Rainy:
                if (mm != null) mm.UpdateAreaInfo(weatherProperties[2].weatherMapName, weatherProperties[2].weatherMapDesc, weatherProperties[2].weatherTextColor,
                    weatherProperties[2].weatherBgColor, weatherProperties[2].weatherOst, weatherProperties[2].weatherOstVolume, announce);
                else sm.UpdateAreaInfo(weatherProperties[2].weatherMapName, weatherProperties[2].weatherTextColor,
                    weatherProperties[2].weatherBgColor, weatherProperties[2].weatherOst, weatherProperties[2].weatherOstVolume);
                InvokeRepeating(nameof(Thunderstorm), 0, thunderTimer);
                break;
            default:
                if (mm != null) mm.UpdateNormalAreaInfo();
                else sm.UpdateNormalAreaInfo();
                player.regenerationMultiplier = preValues[0];
                player.pc.movementSpeed = preValues[1];
                break;
        }
        if (mm != null)
        {
            for (var i = 0; i < mm.mapDisasterNames.Length; i++)
            {
                if (!disasters.ContainsKey(mm.mapDisasterNames[i]))
                    disasters.TryAdd(mm.mapDisasterNames[i], mm.mapDisasterFactors[i]);
                else disasters[mm.mapDisasterNames[i]] = mm.mapDisasterFactors[i];
            }
        }
        else if (sm != null)
        {
            for (var i = 0; i < sm.mapDisasterNames.Length; i++)
            {
                if (!disasters.ContainsKey(sm.mapDisasterNames[i]))
                    disasters.TryAdd(sm.mapDisasterNames[i], sm.mapDisasterFactors[i]);
                else disasters[sm.mapDisasterNames[i]] = sm.mapDisasterFactors[i];
            }
        }
        SetPassiveDisasters();
    }

    private void SetPassiveDisasters()
    {
        InvokeRepeating(nameof(EarthQuake), 0, earthquakeTimer);
        InvokeRepeating(nameof(Temperature), 0, 0.001f);
        StartCoroutine(Wind());
        Invoke(nameof(SetProperties), 1f);
    }

    public void ClearDisasters()
    {
        CancelInvoke(nameof(Thunderstorm));
        CancelInvoke(nameof(EarthQuake));
        CancelInvoke(nameof(Temperature));
        StopCoroutine(Wind());
        disasters.Clear();
        player.regenerationMultiplier = preValues[0];
        player.pc.movementSpeed = preValues[1];
    }
    
    private void SetProperties()
    {
        switch (weatherType)
        {
            case WeatherType.Hot:
            case WeatherType.Cold:
                player.regenerationMultiplier = 0.5f;
                player.pc.movementSpeed = preValues[1] * 0.9f;
                break;
            case WeatherType.Clear:
            case WeatherType.Rainy:
            default:
                player.regenerationMultiplier = preValues[0];
                player.pc.movementSpeed = preValues[1];
                break;
        }
    }

    private void PollingHurtPlayer(float damage = 0.001f) => player.health -= damage;


    private void EarthQuake()
    {
        if (disasters.ContainsKey("Earthquake") && disasters["Earthquake"] > 0 && player.pc.isGrounded) 
            player.GetComponent<IDamageable>().Damage(0.5f * disasters["Earthquake"], false, 3, IDamageable.ElementType.Earth, 1.2f);
    }

    private void Temperature()
    {
        if (disasters.ContainsKey("Temperature") && disasters["Temperature"] > 0)
        {
            switch (weatherType)
            {
                case WeatherType.Hot:
                case WeatherType.Cold:
                    PollingHurtPlayer(0.002f * Math.Abs(disasters["Temperature"]));
                    player.regenerationMultiplier = 0;
                    break;
            }
        }
    }
    
    public void Thunderstorm()
    {
        if (disasters.ContainsKey("Thunderstorm") && disasters["Thunderstorm"] > 0)
        {
            var rng = Random.Range(0, 101);
            if (rng <= thunderChance * disasters["Thunderstorm"])
            {
                var tPos = player.pc.transform.position;
                tPos = new Vector2(tPos.x + Random.Range(-30, 30f), tPos.y + Random.Range(-5, 10f));
                Instantiate(thunderbolt, tPos, Quaternion.identity);
            }
        }
    }
    
    public IEnumerator Wind()
    {
        var rng = Random.Range(0, 2);
        var windDirection = rng == 0;
        while (disasters.ContainsKey("Wind") && disasters["Wind"] > 0)
        {
            player.pc.rb.AddForce(new Vector2(windDirection ? 1 : -1, 0) * 50 * disasters["Wind"]);
            yield return new WaitForFixedUpdate();
        }
        yield return null;
    }
}


[Serializable]
public class WeatherProperties
{
    public string weatherMapName = "";
    public string weatherMapDesc = "";
    public float weatherDuration = 60;
    public Color weatherTextColor = Color.white;
    public Color weatherBgColor = Color.white;
    public AudioClip[] weatherOst = new AudioClip[3];
    public float[] weatherOstVolume = {0.7f,0.7f,0.7f};
}