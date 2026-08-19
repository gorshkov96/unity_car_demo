namespace CarDemo.Vehicle
{
    /// <summary>
    /// A single frame of driving input, already normalized.
    /// Produced by an <see cref="ICarInputProvider"/> and consumed by <see cref="CarController"/>.
    /// Keeping this a plain struct decouples "who drives" (player, AI, test script)
    /// from "what drives" (the physics controller).
    /// </summary>
    public readonly struct CarInputState
    {
        /// <summary>Forward/backward request in [-1, 1]. Positive is forward.</summary>
        public readonly float Throttle;

        /// <summary>Steering request in [-1, 1]. Positive is right.</summary>
        public readonly float Steer;

        /// <summary>Handbrake: kills rear grip for drifting and acts as a brake.</summary>
        public readonly bool Handbrake;

        public CarInputState(float throttle, float steer, bool handbrake)
        {
            Throttle = throttle;
            Steer = steer;
            Handbrake = handbrake;
        }
    }
}
