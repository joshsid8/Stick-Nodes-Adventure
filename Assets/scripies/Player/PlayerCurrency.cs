using UnityEngine;

namespace scripies.Player
{
    public class PlayerCurrency : MonoBehaviour
    {
        public float nodeBucks;
        public float bucksMult = 1;
        
        private void Update()
        {
            if (nodeBucks <= 0) nodeBucks = 0;
        }

        public bool CashFlow(float amount, bool outflow)
        {
            switch (outflow)
            {
                case true:
                    if (Mathf.Abs(amount) > nodeBucks)
                    {
                        print("Not enough buckaroonies pal");
                        return false;
                    }
                    nodeBucks -= amount;
                    return true;
                case false:
                    nodeBucks += amount;
                    return true;
            }
        } 
    }
}