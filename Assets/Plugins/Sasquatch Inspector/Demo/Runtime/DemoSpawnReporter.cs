using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// A listener for <see cref="DemoSpawner"/>'s events, so the UnityEvent widget in the demo has real wired
    /// entries rather than an empty list. An empty UnityEvent is the single most common way a demo scene
    /// reads as unfinished, and the widget only shows what it can do once something is actually in it.
    /// </summary>
    public class DemoSpawnReporter : MonoBehaviour
    {
        [Tooltip("Last message this reporter received — updated by the spawner's events.")]
        public string lastMessage = "Waiting for the first spawn…";

        public int timesNotified;

        public void ReportSpawn()
        {
            timesNotified++;
            lastMessage = $"Spawn requested ({timesNotified} so far).";
            Debug.Log($"[Demo] {lastMessage}", this);
        }

        public void ReportCount(int total)
        {
            lastMessage = $"Spawn count is now {total}.";
            Debug.Log($"[Demo] {lastMessage}", this);
        }
    }
}
