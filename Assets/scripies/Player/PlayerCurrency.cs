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
        
        
        /// <summary>
        /// Call this to manage cash flow, whether it be trade of assets or reserving funds
        /// </summary>
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
        
        /// <summary>
        /// Call this to check your financial eligibility for a purchase
        /// </summary>
        public bool Liquidity(float targetedPurchase)
        {
            return targetedPurchase <= nodeBucks;
        }
    }
}