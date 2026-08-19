using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CarDemo.UI
{
    /// <summary>
    /// Map selection overlay: opens on Escape, lists every scene in the build and loads the
    /// chosen one. Time is paused while it is open.
    ///
    /// Map names come from the build settings rather than a hard-coded list, so adding a scene
    /// to the build is enough to make it selectable.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapMenu : MonoBehaviour
    {
        private static readonly (string Scene, string Title, string Subtitle)[] Descriptions =
        {
            ("Demo", "Кольцевая трасса", "Дорога-кольцо, здания, вращающиеся барьеры и ящики"),
            ("Descent", "Спуск", "7 км под уклон: трамплины, подвижные барьеры, разгон от гравитации"),
        };

        private InputAction _menuAction;
        private VisualElement _overlay;
        private bool _isOpen;

        private void OnEnable()
        {
            _menuAction = InputSystem.actions?.FindAction("Menu", throwIfNotFound: false);
            _menuAction?.Enable();
        }

        private void OnDisable()
        {
            _menuAction?.Disable();
            if (_isOpen) Time.timeScale = 1f;
        }

        // Built in Start, not OnEnable: the HUD clears the shared root in its own OnEnable,
        // and anything added before that would be wiped.
        private void Start()
        {
            BuildOverlay();
            SetOpen(false);
        }

        private void Update()
        {
            if (_menuAction != null && _menuAction.WasPressedThisFrame())
            {
                SetOpen(!_isOpen);
            }
        }

        private void BuildOverlay()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            _overlay = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0, right = 0, top = 0, bottom = 0,
                    backgroundColor = new Color(0.03f, 0.04f, 0.06f, 0.82f),
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                },
            };

            var panel = new VisualElement
            {
                style =
                {
                    minWidth = 460,
                    paddingLeft = 34, paddingRight = 34, paddingTop = 28, paddingBottom = 30,
                    backgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.96f),
                    borderTopLeftRadius = 18, borderTopRightRadius = 18,
                    borderBottomLeftRadius = 18, borderBottomRightRadius = 18,
                },
            };

            panel.Add(new Label("Выбор карты")
            {
                style =
                {
                    fontSize = 26,
                    color = new Color(0.95f, 0.96f, 0.98f),
                    marginBottom = 18,
                    unityFontStyleAndWeight = FontStyle.Bold,
                },
            });

            int activeIndex = SceneManager.GetActiveScene().buildIndex;
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                panel.Add(BuildMapButton(i, i == activeIndex));
            }

            panel.Add(new Label("Esc — закрыть · Tab — следующая карта")
            {
                style =
                {
                    fontSize = 13,
                    color = new Color(0.55f, 0.6f, 0.68f),
                    marginTop = 16,
                    unityTextAlign = TextAnchor.MiddleCenter,
                },
            });

            _overlay.Add(panel);
            root.Add(_overlay);
        }

        private VisualElement BuildMapButton(int buildIndex, bool isActive)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);

            string title = sceneName;
            string subtitle = string.Empty;
            foreach ((string scene, string mapTitle, string mapSubtitle) in Descriptions)
            {
                if (scene != sceneName) continue;
                title = mapTitle;
                subtitle = mapSubtitle;
                break;
            }

            var button = new Button(() => Load(buildIndex))
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.FlexStart,
                    paddingLeft = 18, paddingRight = 18, paddingTop = 12, paddingBottom = 14,
                    marginTop = 6, marginBottom = 6, marginLeft = 0, marginRight = 0,
                    backgroundColor = isActive
                        ? new Color(0.18f, 0.34f, 0.5f, 0.95f)
                        : new Color(0.14f, 0.16f, 0.2f, 0.95f),
                    borderTopLeftRadius = 12, borderTopRightRadius = 12,
                    borderBottomLeftRadius = 12, borderBottomRightRadius = 12,
                    borderLeftWidth = 0, borderRightWidth = 0, borderTopWidth = 0, borderBottomWidth = 0,
                },
            };

            button.Add(new Label(isActive ? $"{title}  •  сейчас здесь" : title)
            {
                style =
                {
                    fontSize = 18,
                    color = new Color(0.94f, 0.95f, 0.97f),
                    unityFontStyleAndWeight = FontStyle.Bold,
                },
            });

            if (!string.IsNullOrEmpty(subtitle))
            {
                button.Add(new Label(subtitle)
                {
                    style =
                    {
                        fontSize = 13,
                        color = new Color(0.62f, 0.68f, 0.76f),
                        marginTop = 3,
                        whiteSpace = WhiteSpace.Normal,
                    },
                });
            }

            return button;
        }

        private void Load(int buildIndex)
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(buildIndex);
        }

        private void SetOpen(bool open)
        {
            _isOpen = open;
            if (_overlay != null)
            {
                _overlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Pausing is what a player expects from a menu, and it stops the car from driving
            // into a wall while they read.
            Time.timeScale = open ? 0f : 1f;
        }
    }
}
