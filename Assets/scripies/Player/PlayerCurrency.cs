using UnityEngine;

namespace scripies.Player
{
    public class PlayerCurrency : MonoBehaviour
    {
        public float nodeBucks;
        // Update is called once per frame
        void Update()
        {
            if (nodeBucks <= 0) nodeBucks = 0;
        }
    }
}