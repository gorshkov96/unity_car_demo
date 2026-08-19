namespace CarDemo.Vehicle
{
    /// <summary>
    /// Input provider driven from code. Used by automated tests
    /// and usable later as a base for AI drivers or replays.
    /// </summary>
    public sealed class ScriptedCarInput : ICarInputProvider
    {
        public CarInputState Current { get; private set; }

        public void Set(float throttle, float steer, bool handbrake = false)
        {
            Current = new CarInputState(throttle, steer, handbrake);
        }
    }
}
