using System;
using UnityEngine;

public class AbilityObjSO : ScriptableObject
{
    public string abilityName;
    public AbilityProperty[] abilityProperties = new AbilityProperty[1];
    public float abilityCooldown = 1;
}

[Serializable]
public class AbilityProperty
{
    public GameObject abilityPrefab;
    public int amount = 1;
    public float lifetimeMult = 1;
    public float damageMult = 1;
    public float sizeMult = 1;
}