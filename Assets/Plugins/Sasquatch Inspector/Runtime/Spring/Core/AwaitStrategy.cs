namespace SasquatchSpring
{
    /// <summary>Controls when an awaited spring's Task completes — once physically settled, or once visually close enough.</summary>
    public enum AwaitStrategy
    {
        /// <summary>
        /// Waits until the spring has completely stopped moving (Velocity ~0).
        /// Use this for precise positioning or looping platforms.
        /// </summary>
        PhysicalSettling,

        /// <summary>
        /// Waits until the value is visually "close enough" to the target.
        /// The spring continues to wobble in the background.
        /// Use this for snappy UI or impact events.
        /// </summary>
        VisualArrival
    }
}
