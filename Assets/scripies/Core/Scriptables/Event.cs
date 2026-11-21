using UnityEngine;

[CreateAssetMenu(fileName = "NewEvent", menuName = "Event")]
public class Event : ScriptableObject
{
    public string eventName = "";
    public string eventDesc = "";
    public float eventDuration = 60;
    public Color eventTextColor = Color.white;
    public Color eventBgColor = Color.white;
    public AudioClip[] eventOst = new AudioClip[3];
    public float[] eventOstVolume = {0.7f,0.7f,0.7f};
}
