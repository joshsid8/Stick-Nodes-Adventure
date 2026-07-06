using System;
using UnityEngine;
using UnityEngine.Serialization;

public class WeatherManager : MonoBehaviour
{
    public enum WeatherFactor {Clear, Hot, Cold, Rainy}
    public WeatherFactor weatherFactor;
    [FormerlySerializedAs("wp")] public WeatherProperties[] weatherProperties = new WeatherProperties[3];
    private MapManager mm;
    private readonly float[] preValues = new float[3];
    private void Awake()
    {
        mm = GetComponent<MapManager>();
        preValues[0] = PlayerHealth.Instance.regenerationMultiplier;
        preValues[1] = PlayerHealth.Instance.pc.movementSpeed;
    }
    public void SetWeather()
    {
        switch (weatherFactor)
        {
            case WeatherFactor.Clear:
                mm.UpdateNormalAreaInfo();
                PlayerHealth.Instance.regenerationMultiplier = preValues[0];
                PlayerHealth.Instance.pc.movementSpeed = preValues[1];
                break;
            case WeatherFactor.Hot:
                preValues[0] = PlayerHealth.Instance.regenerationMultiplier;
                preValues[1] = PlayerHealth.Instance.pc.movementSpeed;
                PlayerHealth.Instance.regenerationMultiplier = 0;
                PlayerHealth.Instance.pc.movementSpeed = preValues[1] * 0.8f;
                mm.UpdateAreaInfo(weatherProperties[0].weatherMapName, weatherProperties[0].weatherMapDesc, weatherProperties[0].weatherTextColor,
                    weatherProperties[0].weatherBgColor, weatherProperties[0].weatherOst, weatherProperties[0].weatherOstVolume);
                break;
            case WeatherFactor.Cold:
                preValues[0] = PlayerHealth.Instance.regenerationMultiplier;
                preValues[1] = PlayerHealth.Instance.pc.movementSpeed;
                PlayerHealth.Instance.regenerationMultiplier = 0;
                PlayerHealth.Instance.pc.movementSpeed = preValues[1] * 0.6f;
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
        switch (weatherFactor)
        {
            case WeatherFactor.Hot:
            case WeatherFactor.Cold:
                SlowlyHurtPlayer(0.002f, PlayerHealth.Instance);
                break;
        }
    }
    
    public void SlowlyHurtPlayer(float damage, PlayerHealth ph)
    {
        ph.playerHealth -= damage;
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