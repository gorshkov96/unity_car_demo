using UnityEngine;
using UnityEngine.UIElements;

namespace CarDemo.UI
{
    /// <summary>
    /// Speedometer drawn with <see cref="Painter2D"/>: an arc gauge, a needle and a numeric
    /// readout. Vector drawing rather than sprites keeps it resolution independent and adds
    /// no texture assets to the repository.
    ///
    /// Repaints only when the displayed value actually changes, so a parked car costs nothing.
    /// </summary>
    public sealed class SpeedometerElement : VisualElement
    {
        private const float StartAngle = 140f;
        private const float SweepAngle = 260f;

        private readonly Label _readout;
        private float _speedKmh;
        private float _maxSpeedKmh = 120f;
        private float _lastRepaintedSpeed = float.NaN;

        public SpeedometerElement()
        {
            style.width = 190;
            style.height = 190;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;

            _readout = new Label("0")
            {
                style =
                {
                    fontSize = 40,
                    color = new Color(0.94f, 0.95f, 0.97f),
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginTop = 26,
                },
            };
            Add(_readout);

            var units = new Label("км/ч")
            {
                style =
                {
                    fontSize = 13,
                    color = new Color(0.62f, 0.66f, 0.72f),
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginTop = -6,
                },
            };
            Add(units);

            generateVisualContent += Draw;
        }

        public float MaxSpeedKmh
        {
            get => _maxSpeedKmh;
            set
            {
                _maxSpeedKmh = Mathf.Max(1f, value);
                MarkDirtyRepaint();
            }
        }

        /// <summary>Current speed. Setting it repaints only when the rounded value changed.</summary>
        public float SpeedKmh
        {
            get => _speedKmh;
            set
            {
                _speedKmh = value;
                if (Mathf.Abs(_speedKmh - _lastRepaintedSpeed) < 0.5f) return;

                _lastRepaintedSpeed = _speedKmh;
                _readout.text = Mathf.RoundToInt(_speedKmh).ToString();
                MarkDirtyRepaint();
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width < 1f || rect.height < 1f) return;

            Painter2D painter = context.painter2D;
            var centre = new Vector2(rect.width * 0.5f, rect.height * 0.5f);
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 12f;

            // Track.
            painter.lineWidth = 10f;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = new Color(1f, 1f, 1f, 0.12f);
            painter.BeginPath();
            painter.Arc(centre, radius, StartAngle, StartAngle + SweepAngle);
            painter.Stroke();

            // Filled portion, shifting from cyan to amber as the car approaches top speed.
            float fill = Mathf.Clamp01(_speedKmh / _maxSpeedKmh);
            if (fill > 0.001f)
            {
                painter.strokeColor = Color.Lerp(
                    new Color(0.29f, 0.76f, 0.94f),
                    new Color(0.96f, 0.58f, 0.16f),
                    fill);
                painter.BeginPath();
                painter.Arc(centre, radius, StartAngle, StartAngle + SweepAngle * fill);
                painter.Stroke();
            }

            // Needle.
            float needleAngle = (StartAngle + SweepAngle * fill) * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(needleAngle), Mathf.Sin(needleAngle));
            painter.lineWidth = 3f;
            painter.strokeColor = new Color(0.96f, 0.97f, 0.98f, 0.9f);
            painter.BeginPath();
            painter.MoveTo(centre + direction * (radius * 0.35f));
            painter.LineTo(centre + direction * (radius - 4f));
            painter.Stroke();
        }
    }
}
