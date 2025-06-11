using System;
using UnityEngine;

public class PlayerFireStationaryElement : MonoBehaviour
{
    public Transform projectilePoint;
    public GameObject projectile;
    private PlayerController pc;
    private PlayerAbility pa;
    public float abilityLifeTime;
    public float abilityDamage;

    private float xVector;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        pa = GetComponent<PlayerAbility>();
        pc = GetComponent<PlayerController>(); // Gets the PlayerController from the Player GameObject
    }

    private void Update()
    {
        if (pc.isFacingRight) xVector = 2f;
        else xVector = -2f;
    }

    public void Execute()
    {
        GameObject newElement = Instantiate(projectile, projectilePoint.position + new Vector3(xVector, -1f, 0f), Quaternion.identity);
        PlayerFirePillar stationaryElement = newElement.GetComponent<PlayerFirePillar>();
        
        if (stationaryElement != null) stationaryElement.SetValues(abilityLifeTime, abilityDamage * pa.damageMultiplier);
        else Debug.LogError("where is bro's ability");
    }
}