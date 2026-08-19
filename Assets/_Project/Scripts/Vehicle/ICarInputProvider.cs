namespace CarDemo.Vehicle
{
    /// <summary>
    /// Source of driving input. Implemented by the keyboard/gamepad reader today;
    /// an AI driver or a replay system can implement it later without touching physics.
    /// </summary>
    public interface ICarInputProvider
    {
        CarInputState Current { get; }
    }
}
