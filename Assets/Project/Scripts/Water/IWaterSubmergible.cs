using UnityEngine;

namespace EscapeTheFlood.Water
{
    /// <summary>
    /// Contract for any entity (Player, furniture, props) that reacts to being submerged in water.
    /// Implemented by Member 1's Player controller for swim physics and breath depletion.
    /// </summary>
    public interface IWaterSubmergible
    {
        /// <summary>
        /// Called when the entity enters the water trigger volume.
        /// </summary>
        /// <param name="waterBody">Reference to the active WaterBody.</param>
        void OnEnterWater(WaterBody waterBody);

        /// <summary>
        /// Called every physics frame the entity remains inside the water volume.
        /// </summary>
        /// <param name="waterBody">Reference to the active WaterBody.</param>
        /// <param name="waterSurfaceY">Current Y level of the water surface.</param>
        void OnStayWater(WaterBody waterBody, float waterSurfaceY);

        /// <summary>
        /// Called when the entity completely exits the water trigger volume.
        /// </summary>
        /// <param name="waterBody">Reference to the active WaterBody.</param>
        void OnExitWater(WaterBody waterBody);
    }
}
