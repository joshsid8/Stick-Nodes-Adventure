using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(CollectableTrigger))]
public class Collectable : MonoBehaviour
{
    public CollectableSOBase collectable;
    public void Collect(GameObject objectCollected)
    {
        collectable.Collect(objectCollected);
    }
}
