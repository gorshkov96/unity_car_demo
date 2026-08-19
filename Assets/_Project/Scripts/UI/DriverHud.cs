using CarDemo.Diagnostics;
using CarDemo.Vehicle;
using UnityEngine;
using UnityEngine.UIElements;

namespace CarDemo.UI
{
    /// <summary>
    /// On-screen telemetry built with UI Toolkit: speedometer, wheel contact indicator,
    /// control hints and an optional frame-budget readout.
    ///
    /// Replaces an earlier IMGUI version that allocated several kilobytes every frame —
    /// UI Toolkit only rebuilds elements whose content actually changed.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DriverHud : MonoBehaviour
    {
        [SerializeField] private CarController _car;
        [SerializeField] private PerformanceProbe _probe;
        [Tooltip("How often the text fields refresh, seconds. The needle updates every frame.")]
        [SerializeField] private float _textRefreshInterval = 0.2f;

        private SpeedometerElement _speedometer;
        private Label _wheelsLabel;
        private Label _perfLabel;
        private float _nextTextRefresh;
        private int _lastGroundedWheels = -1;

        public void SetCar(CarController car) => _car = car;
        public void SetProbe(PerformanceProbe probe) => _probe = probe;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;

            var panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 20,
                    bottom = 20,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 10,
                    paddingBottom = 12,
                    backgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.55f),
                    borderTopLeftRadius = 14,
                    borderTopRightRadius = 14,
                    borderBottomLeftRadius = 14,
                    borderBottomRightRadius = 14,
                    alignItems = Align.Center,
                },
                pickingMode = PickingMode.Ignore,
            };

            _speedometer = new SpeedometerElement();
            if (_car != null && _car.Config != null)
            {
                _speedometer.MaxSpeedKmh = _car.Config.MaxSpeed * 3.6f;
            }

            panel.Add(_speedometer);

            _wheelsLabel = MakeLabel(new Color(0.72f, 0.76f, 0.82f), 13);
            panel.Add(_wheelsLabel);

            var hint = MakeLabel(new Color(0.55f, 0.59f, 0.66f), 12);
            hint.text = "WASD — руль и газ · Space — ручник · R — на старт";
            panel.Add(hint);

            root.Add(panel);

            _perfLabel = MakeLabel(new Color(0.75f, 0.8f, 0.86f), 13);
            _perfLabel.style.position = Position.Absolute;
            _perfLabel.style.right = 20;
            _perfLabel.style.top = 16;
            _perfLabel.style.paddingLeft = 10;
            _perfLabel.style.paddingRight = 10;
            _perfLabel.style.paddingTop = 4;
            _perfLabel.style.paddingBottom = 5;
            _perfLabel.style.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.5f);
            _perfLabel.style.borderTopLeftRadius = 8;
            _perfLabel.style.borderTopRightRadius = 8;
            _perfLabel.style.borderBottomLeftRadius = 8;
            _perfLabel.style.borderBottomRightRadius = 8;
            root.Add(_perfLabel);
        }

        private static Label MakeLabel(Color color, int fontSize)
        {
            return new Label
            {
                style =
                {
                    color = color,
                    fontSize = fontSize,
                    marginTop = 4,
                    unityTextAlign = TextAnchor.MiddleCenter,
                },
                pickingMode = PickingMode.Ignore,
            };
        }

        private void Update()
        {
            if (_car == null || _speedometer == null) return;

            // Cheap every frame: the setter skips the repaint unless the rounded value moved.
            _speedometer.SpeedKmh = _car.SpeedKmh;

            if (Time.unscaledTime < _nextTextRefresh) return;
            _nextTextRefresh = Time.unscaledTime + _textRefreshInterval;

            int grounded = CountGroundedWheels();
            if (grounded != _lastGroundedWheels)
            {
                _lastGroundedWheels = grounded;
                _wheelsLabel.text = $"колёс на земле: {grounded}/4";
            }

            if (_probe != null)
            {
                double median = _probe.FrameTimeStats.Percentile(0.5);
                _perfLabel.text = $"{1000.0 / Mathf.Max(0.01f, (float)median):0} fps · {median:0.0} мс · draw {_probe.DrawCalls}";
            }
        }

        private int CountGroundedWheels()
        {
            System.ReadOnlySpan<WheelState> wheels = _car.Wheels;
            if (wheels.IsEmpty) return 0;

            int grounded = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i].Grounded) grounded++;
            }

            return grounded;
        }
    }
}
