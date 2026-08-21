using System;
using UnityEngine;

// TODO: Make use of this component in some way, if applicable. Watch a video on an efficient way of making sfx, cuz the current way is so beginner-coded
public class SoundFX : MonoBehaviour
{
    public Clip[] clips;
    
}

[Serializable]
public class Clip
{
    public AudioClip clip;
    public float volume;
}
