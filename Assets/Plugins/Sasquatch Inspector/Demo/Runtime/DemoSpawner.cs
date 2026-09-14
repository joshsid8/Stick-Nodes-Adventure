using UnityEngine;
using UnityEngine.Events;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 5 — an inspector that does something. Buttons call these methods straight from the Inspector,
    /// with arguments supplied in the designed layout, and the UnityEvent below gets a proper editor instead
    /// of the stock list-with-a-plus-button.
    /// </summary>
    public class DemoSpawner : MonoBehaviour
    {
        public enum SpawnPattern { Single, Ring, Grid, Scatter }

        public GameObject prefabToSpawn;
        public SpawnPattern pattern = SpawnPattern.Ring;

        [Range(1, 64)] public int count = 12;
        [Range(0f, 20f)] public float radius = 6f;
        [Range(0f, 5f)] public float spacing = 1.5f;
        public Vector3 originOffset = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Read-only in the designed layout — a live count the buttons below change.")]
        public int spawnedThisSession;

        // A concrete subclass rather than UnityEvent<int> directly — the explicit form serializes on every
        // editor version the package supports, which the bare generic does not.
        [System.Serializable] public class SpawnCountEvent : UnityEvent<int> { }

        public UnityEvent onSpawnRequested;
        public SpawnCountEvent onCountChanged;

        // ---- Button targets ------------------------------------------------------------------------------
        // Plain public methods. The designed layout binds Button nodes to them and supplies the arguments,
        // so none of this needs an attribute or a custom editor.

        public void Spawn()
        {
            spawnedThisSession += count;
            onSpawnRequested?.Invoke();
            onCountChanged?.Invoke(spawnedThisSession);
            Debug.Log($"[Demo] Spawned {count} in a {pattern} pattern (radius {radius}).", this);
        }

        public void SpawnBatch(int howMany)
        {
            spawnedThisSession += howMany;
            onCountChanged?.Invoke(spawnedThisSession);
            Debug.Log($"[Demo] Spawned a batch of {howMany}.", this);
        }

        public void ClearSpawned()
        {
            spawnedThisSession = 0;
            onCountChanged?.Invoke(0);
            Debug.Log("[Demo] Cleared the spawn counter.", this);
        }

        public void SetPattern(SpawnPattern next)
        {
            pattern = next;
            Debug.Log($"[Demo] Pattern set to {next}.", this);
        }
    }
}
