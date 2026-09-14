using System.Collections;
using UnityEngine;

public abstract class SpawnerBase : MonoBehaviour
{
    public bool isOneTime;
    protected abstract IEnumerator Spawn();
    public abstract IEnumerator Despawn();
}
