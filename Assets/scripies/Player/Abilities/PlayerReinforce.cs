using UnityEngine;

public class PlayerReinforce : MonoBehaviour
{
    public float reinforceDamageResistance;

    public float maxReinforceTime;
    public float currentReinforceTime;

    public float reinforceCooldown;
    public float currentCooldown;
    
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (currentReinforceTime > 0) currentReinforceTime -= Time.deltaTime;
        if (currentCooldown > 0) currentCooldown -= Time.deltaTime;
    }

    void ActivateReinforce(int type)
    {
        switch (type)
        {
            case 0: //fire
                
                break;
        }
    }
}
