using System;
using System.Collections.Generic;
using scripies.Player;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class WeatherManager : MonoBehaviour
{
    public enum WeatherType {Clear, Hot, Cold, Rainy}
    public WeatherType weatherType;
    [FormerlySerializedAs("wp")]
    public WeatherProperties[] weatherProperties = new WeatherProperties[3];
    private MapManager mm;

    public Dictionary<string, float> disasters = new(4);
    private float hurtTimer;
    
    [SerializeField] private GameObject thunderbolt;
    [SerializeField] private float thunderTimer = 5;
    [SerializeField] private int thunderChance = 50;
    
    private readonly float[] preValues = new float[3];
    private PlayerHealth player;
    
    private void Awake()
    {
        mm = GetComponent<MapManager>();
        player = PlayerHealth.Instance;
        preValues[0] = player.regenerationMultiplier;
        preValues[1] = player.pc.movementSpeed;

        disasters.Add("Temperature", 0);
        disasters.Add("Thunderstorm", 0);
        disasters.Add("Wind", 0);
        disasters.Add("Earthquake", 0);
    }
    public void SetWeather()
    {
        switch (weatherType)
        {
            case WeatherType.Clear:
                mm.UpdateNormalAreaInfo();
                player.regenerationMultiplier = preValues[0];
                player.pc.movementSpeed = preValues[1];
                break;
            case WeatherType.Hot:
                mm.UpdateAreaInfo(weatherProperties[0].weatherMapName, weatherProperties[0].weatherMapDesc, weatherProperties[0].weatherTextColor,
                    weatherProperties[0].weatherBgColor, weatherProperties[0].weatherOst, weatherProperties[0].weatherOstVolume);
                break;
            case WeatherType.Cold:
                mm.UpdateAreaInfo(weatherProperties[1].weatherMapName, weatherProperties[1].weatherMapDesc, weatherProperties[1].weatherTextColor,
                    weatherProperties[1].weatherBgColor, weatherProperties[1].weatherOst, weatherProperties[1].weatherOstVolume);
                break;
            case WeatherType.Rainy:
                mm.UpdateAreaInfo(weatherProperties[2].weatherMapName, weatherProperties[2].weatherMapDesc, weatherProperties[2].weatherTextColor,
                    weatherProperties[2].weatherBgColor, weatherProperties[2].weatherOst, weatherProperties[2].weatherOstVolume);
                break;
            default:
                mm.UpdateNormalAreaInfo();
                break;
        }

        for (var i = 0; i < mm.mapDisasterNames.Length; i++)
        {
            if (!disasters.ContainsKey(mm.mapDisasterNames[i])) disasters.TryAdd(mm.mapDisasterNames[i], mm.mapDisasterFactors[i]);
            else disasters[mm.mapDisasterNames[i]] = mm.mapDisasterFactors[i];
        }
        InvokeRepeating(nameof(Thunderstorm), 0, thunderTimer);
        if (weatherType == WeatherType.Hot || weatherType == WeatherType.Cold) 
            player.pc.movementSpeed = preValues[1] * 0.8f;
    }
    
    private void Update()
    {
        EarthQuake();
        Temperature();
        Wind(false);
        hurtTimer -= Time.deltaTime;
    }
    
    public void SlowlyHurtPlayer(float damage = 0.5f, float time = 0)
    {
        if (hurtTimer <= 0)
        {
            player.playerHealth -= damage;
            hurtTimer = time;
        }
    }

    private void PollingHurtPlayer(float damage = 0.002f) => player.playerHealth -= damage;

    public void ClearDisasters()  
    {
        disasters.Clear();
        CancelInvoke(nameof(Thunderstorm));
    }

    private void EarthQuake()
    {
        if (disasters.ContainsKey("Earthquake") && disasters["Earthquake"] > 0 && player.pc.isGrounded) 
            SlowlyHurtPlayer(1 * disasters["Earthquake"], 0.2f);
    }

    private void Temperature()
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

    public void Wind(bool isRight)
    {
        if (disasters.ContainsKey("Wind") && disasters["Wind"] > 0) 
            player.pc.rb.AddForce(new Vector2(isRight ? 1 : -1, 0) * 50 * disasters["Wind"]);
    }
}


[Serializable]
public class WeatherProperties
{
    public string weatherMapName = "";
    public string weatherMapDesc = "";
    public Color weatherTextColor = Color.white;
    public Color weatherBgColor = Color.white;
    public AudioClip[] weatherOst = new AudioClip[3];
    public float[] weatherOstVolume = {0.7f,0.7f,0.7f};
}