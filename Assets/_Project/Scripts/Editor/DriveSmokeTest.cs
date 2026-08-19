using System.Text;
using CarDemo.Core;
using CarDemo.Game;
using CarDemo.Vehicle;
using CarDemo.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Headless check that the car drives and the generated world is sane.
    /// Steps the physics engine manually (SimulationMode.Script), so it runs from the
    /// command line in seconds without entering play mode.
    ///
    /// Part 1 drives on a clean flat plate — the handling result must not depend on
    /// where the world generator happened to drop an obstacle.
    /// Part 2 inspects the real demo scene.
    /// </summary>
    public static class DriveSmokeTest
    {
        private const string ScenePath = "Assets/_Project/Scenes/Demo.unity";
        private const string CarConfigPath = "Assets/_Project/Settings/CarConfig.asset";
        private const float StepTime = 0.02f;

        private static readonly StringBuilder Report = new StringBuilder();
        private static bool _passed = true;

        [MenuItem("CarDemo/Run Drive Smoke Test")]
        public static void Run()
        {
            Report.Clear();
            _passed = true;

            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                TestHandlingOnCleanGround();
                TestGeneratedScene();
            }
            finally
            {
                Physics.simulationMode = previousMode;
            }

            Report.AppendLine($"[SMOKE] RESULT: {(_passed ? "PASS" : "FAIL")}");
            Debug.Log(Report.ToString());

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(_passed ? 0 : 2);
            }
        }

        // ------------------------------------------------------------------
        // Part 1: handling on an empty plate
        // ------------------------------------------------------------------
        private static void TestHandlingOnCleanGround()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var config = AssetDatabase.LoadAssetAtPath<CarConfig>(CarConfigPath);
            if (config == null)
            {
                Fail("handling", "CarConfig asset not found — run the scene generator first.");
                return;
            }

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(1000f, 1f, 1000f);
            // The suspension only accepts drivable layers, so the test plate must be one.
            ground.layer = GameLayers.Ground;

            CarRigFactory.Rig rig = CarRigFactory.Build(config, new Vector3(0f, 1f, 0f), Quaternion.identity);
            CarController car = rig.Root.AddComponent<CarController>();
            car.Configure(config);
            car.Initialize();

            var input = new ScriptedCarInput();
            car.SetInputProvider(input);
            Transform carTransform = rig.Root.transform;

            // 1. Settle on the suspension.
            input.Set(0f, 0f);
            Simulate(car, 100);
            float restHeight = carTransform.position.y;
            Check("settle", car.IsGrounded && restHeight > 0.1f && restHeight < 2f,
                $"grounded={car.IsGrounded} height={restHeight:0.00}m");

            // 2. Accelerate.
            Vector3 launch = carTransform.position;
            input.Set(1f, 0f);
            Simulate(car, 250);
            float distance = Vector3.Distance(carTransform.position, launch);
            float speed = car.SpeedKmh;
            Check("accelerate", distance > 20f && speed > 40f, $"distance={distance:0.0}m speed={speed:0}km/h");

            // 3. No sideways drift without steering input.
            float drift = Mathf.Abs(carTransform.position.x - launch.x);
            Check("straight", drift < distance * 0.05f, $"lateral drift={drift:0.00}m over {distance:0.0}m");

            // 4. Steering turns the car. The yaw is accumulated step by step: a single
            // before/after comparison wraps around once the car turns more than 180 degrees
            // and would report a hard right as a left turn.
            input.Set(1f, 1f);
            float yawTurned = SimulateAndAccumulateYaw(car, 150);
            Check("steer", yawTurned > 15f, $"yaw turned={yawTurned:0.0} deg to the right");

            // 5. Stays on its wheels through the turn.
            float upright = Vector3.Dot(carTransform.up, Vector3.up);
            Check("upright", upright > 0.7f, $"up dot={upright:0.00}");

            // 6. Top speed limiter.
            input.Set(1f, 0f);
            Simulate(car, 700);
            float topSpeed = car.SpeedKmh;
            float limit = config.MaxSpeed * 3.6f * 1.15f;
            Check("top speed", topSpeed <= limit, $"{topSpeed:0}km/h (limit {limit:0}km/h)");

            // 7. Braking. Measured as signed forward speed: holding reverse after the
            // car stops legitimately drives it backwards, which is not a brake failure.
            float speedBeforeBraking = car.ForwardSpeed;
            input.Set(-1f, 0f);
            Simulate(car, 100);
            float speedAfterBraking = car.ForwardSpeed;
            float deceleration = (speedBeforeBraking - speedAfterBraking) / (100 * StepTime);
            // 5 m/s^2 is about half a g — below that the car feels like it has no brakes.
            Check("brake", deceleration > 5f,
                $"{speedBeforeBraking * 3.6f:0} -> {speedAfterBraking * 3.6f:0} km/h, {deceleration:0.0} m/s^2 ({deceleration / 9.81f:0.00}g)");

            // 8. Handbrake drift: rear grip must actually drop.
            input.Set(1f, 0f);
            Simulate(car, 200);
            input.Set(1f, 1f, handbrake: true);
            Simulate(car, 100);
            float slipAngle = Vector3.Angle(
                Vector3.ProjectOnPlane(rig.Body.linearVelocity, Vector3.up),
                Vector3.ProjectOnPlane(carTransform.forward, Vector3.up));
            Check("handbrake", slipAngle > 3f, $"slip angle={slipAngle:0.0} deg");
        }

        // ------------------------------------------------------------------
        // Part 2: the real generated scene
        // ------------------------------------------------------------------
        private static void TestGeneratedScene()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var carObject = GameObject.Find("Car");
            var worldObject = GameObject.Find("World");
            if (carObject == null || worldObject == null)
            {
                Fail("scene", "Demo scene has no 'Car' or no 'World'.");
                return;
            }

            int colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length;
            int renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length;
            int bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
            int movers = Object.FindObjectsByType<MovingPlatform>(FindObjectsSortMode.None).Length
                         + Object.FindObjectsByType<SpinningProp>(FindObjectsSortMode.None).Length;
            Check("world content", colliders > 50 && renderers > 100 && movers > 5,
                $"colliders={colliders} renderers={renderers} rigidbodies={bodies} movers={movers}");

            // The spawn point must be clear: a car starting inside a prop cannot drive.
            Collider[] overlaps = Physics.OverlapBox(
                carObject.transform.position + Vector3.up * 0.5f,
                new Vector3(1.5f, 1f, 3f),
                carObject.transform.rotation);

            int foreign = 0;
            foreach (Collider hit in overlaps)
            {
                if (hit.transform.root != carObject.transform.root && hit.gameObject.name != "Ground")
                {
                    foreign++;
                }
            }

            Check("spawn clear", foreign == 0, $"{foreign} foreign colliders overlap the car at spawn");

            // The car must settle on its suspension in the real scene too.
            CarController car = carObject.GetComponent<CarController>();
            car.Initialize();
            var input = new ScriptedCarInput();
            car.SetInputProvider(input);
            input.Set(0f, 0f);
            Simulate(car, 120);
            Check("scene settle", car.IsGrounded, $"grounded={car.IsGrounded} height={carObject.transform.position.y:0.00}m");
        }

        /// <summary>Simulates and returns the total yaw travelled, signed, without wrapping.</summary>
        private static float SimulateAndAccumulateYaw(CarController car, int steps)
        {
            Transform transform = car.transform;
            float previousYaw = transform.eulerAngles.y;
            float total = 0f;

            for (int i = 0; i < steps; i++)
            {
                car.Tick(StepTime);
                Physics.Simulate(StepTime);

                float yaw = transform.eulerAngles.y;
                total += Mathf.DeltaAngle(previousYaw, yaw);
                previousYaw = yaw;
            }

            return total;
        }

        private static void Simulate(CarController car, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                car.Tick(StepTime);
                Physics.Simulate(StepTime);
            }
        }

        private static void Check(string label, bool condition, string detail)
        {
            _passed &= condition;
            Report.AppendLine($"[SMOKE] {label}: {detail} -> {(condition ? "OK" : "FAILED")}");
        }

        private static void Fail(string label, string detail)
        {
            _passed = false;
            Report.AppendLine($"[SMOKE] {label}: {detail} -> FAILED");
        }
    }
}
