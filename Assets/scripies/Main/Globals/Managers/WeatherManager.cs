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

    public Dictionary<string, float> disasters = new(5);
    private float hurtTimer;
    
    private readonly float[] preValues = new float[3];
    private PlayerHealth player;
    
    private void Awake()
    {
        mm = GetComponent<MapManager>();
        player = PlayerHealth.Instance;
        preValues[0] = player.regenerationMultiplier;
        preValues[1] = player.pc.movementSpeed;
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
                player.regenerationMultiplier = 0;
                player.pc.movementSpeed = preValues[1] * 0.8f;
                mm.UpdateAreaInfo(weatherProperties[0].weatherMapName, weatherProperties[0].weatherMapDesc, weatherProperties[0].weatherTextColor,
                    weatherProperties[0].weatherBgColor, weatherProperties[0].weatherOst, weatherProperties[0].weatherOstVolume);
                break;
            case WeatherType.Cold:
                player.regenerationMultiplier = 0;
                player.pc.movementSpeed = preValues[1] * 0.8f;
                mm.UpdateAreaInfo(weatherProperties[1].weatherMapName, weatherProperties[1].weatherMapDesc, weatherProperties[1].weatherTextColor,
                    weatherProperties[1].weatherBgColor, weatherProperties[1].weatherOst, weatherProperties[1].weatherOstVolume);
                break;
            default:
                mm.UpdateNormalAreaInfo();
                break;
        }
    }
    
    private void Update()
    {
        DisasterEffects();
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
    
    public void SetDisaster(string dName = "", float dFactor = 0)
    {
        if (disasters.ContainsKey(dName)) disasters[dName] = dFactor;
    }

    public void DisasterEffects()
    {
        EarthQuake();
        Temperature();
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
                PollingHurtPlayer(0.002f * disasters["Temperature"]);
                break;
        }
    }
    
    private float thunderTimer;
    public void Thunderstorm()
    {
        if (disasters.ContainsKey("Thunderstorm") && disasters["Thunderstorm"] > 0 && thunderTimer <= 0)
        {
            var rng = Random.Range(0, 101);
            if (rng <= 50)
            {
                // TODO: A lightning bolt will strike near the player >:D
                thunderTimer = 1 / disasters["Thunderstorm"];
            }
        }
    }

    public void Wind(bool isRight)
    {
        if (disasters.ContainsKey("Wind") && disasters["Wind"] > 0)
        {
            player.pc.rb.AddForce(new Vector2(isRight ? 1 : -1, 0) * 100 * disasters["Wind"]);
        }
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