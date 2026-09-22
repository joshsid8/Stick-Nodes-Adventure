using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Dispenser : MonoBehaviour
{
    public bool dispenseOnStart;
    public bool loop = true;
    public float dispenserInterval = 1f;
    public List<GameObjectToDispense> objectsToDispense;
    public List<AbilityObjSO> abilitiesToDispense;
    public Transform spawnPoint;
    public bool facingRight;
    private void Start()
    {
        if (dispenseOnStart) Dispense();
    }

    private void Update()
    {
        transform.rotation = Quaternion.Euler(0, facingRight ? 0 : 180, 0);
    }

    [ContextMenu("Dispense")]
    public void Dispense() => StartCoroutine(DispenseC());
    private IEnumerator DispenseC()
    {
        if (objectsToDispense.Count == 0 && abilitiesToDispense.Count == 0)
        {
            Debug.LogError($"EmptyListException: {name} has no objects or abilities to dispense!");
            yield break;
        }
        foreach (var newObj in objectsToDispense)
        {
            var obj = Instantiate(newObj.gameObjectToDispense, spawnPoint.position, Quaternion.identity);
            obj.GetComponent<Rigidbody2D>().linearVelocity = newObj.launchDirection * newObj.launchSpeed * transform.right;
        }

        var performAbility = GetComponent<PerformAbility>();
        foreach (var newAbility in abilitiesToDispense)
        {
            performAbility.Perform(newAbility);
        }

        if (!loop) yield break;
        if (dispenserInterval <= 0)
        {
            print($"{name} has a negative or zero dispenser interval! ({dispenserInterval}) This could explode the world! setting to a lenient number");
            dispenserInterval = 0.5f;
        }
        yield return new WaitForSeconds(dispenserInterval);
        Dispense();
    }
    
    [Serializable]
    public struct GameObjectToDispense
    {
        public GameObject gameObjectToDispense;
        public Vector2 launchDirection;
        public float launchSpeed;
    }
}
