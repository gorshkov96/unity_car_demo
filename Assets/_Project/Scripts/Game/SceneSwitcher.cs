using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CarDemo.Game
{
    /// <summary>
    /// Cycles between the demo maps at runtime, so both can be shown without rebuilding.
    /// Scene names come from the build settings order rather than hard-coded strings.
    /// </summary>
    public sealed class SceneSwitcher : MonoBehaviour
    {
        private InputAction _switchAction;

        private void OnEnable()
        {
            _switchAction = InputSystem.actions?.FindAction("SwitchMap", throwIfNotFound: false);
            _switchAction?.Enable();
        }

        private void OnDisable()
        {
            _switchAction?.Disable();
        }

        private void Update()
        {
            if (_switchAction == null || !_switchAction.WasPressedThisFrame()) return;

            int count = SceneManager.sceneCountInBuildSettings;
            if (count <= 1) return;

            int next = (SceneManager.GetActiveScene().buildIndex + 1) % count;
            SceneManager.LoadScene(next);
        }
    }
}
