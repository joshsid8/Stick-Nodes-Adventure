using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(CollectableTrigger))]
public class Collectable : MonoBehaviour
{
    [SerializeField] private CollectableSOBase collectable;
    private void Reset()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
    }

    public void Collect(GameObject objectCollected)
    {
        collectable.Collect(objectCollected);
    }
}
