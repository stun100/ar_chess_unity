using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.ARFoundation.Samples
{
    /// <summary>
    /// Populates a drop down UI element with all the supported
    /// camera configurations and changes the active camera
    /// configuration when the user changes the selection in the dropdown.
    ///
    /// The camera configuration affects the resolution (and possibly framerate)
    /// of the hardware camera during an AR session.
    /// </summary>
    [RequireComponent(typeof(Dropdown))]
    public class CameraConfigController : MonoBehaviour
    {
        List<string> m_ConfigurationNames;

        Dropdown m_Dropdown;

        [SerializeField]
        [Tooltip("The ARCameraManager which will produce frame events.")]
        ARCameraManager m_CameraManager;

        /// <summary>
        /// Get or set the <c>ARCameraManager</c>.
        /// </summary>
        public ARCameraManager cameraManager
        {
            get => m_CameraManager;
            set => m_CameraManager = value;
        }

        /// <summary>
        /// Callback invoked when <see cref="m_Dropdown"/> changes. This
        /// lets us change the camera configuration when the user changes
        /// the selection in the UI.
        /// </summary>
        /// <param name="dropdown">The <c>Dropdown</c> which changed.</param>
        void OnDropdownValueChanged(Dropdown dropdown)
        {
            if ((cameraManager == null) || (cameraManager.subsystem == null) || !cameraManager.subsystem.running)
            {
                return;
            }

            var configurationIndex = dropdown.value;

            // Check that the value makes sense
            using (var configurations = cameraManager.GetConfigurations(Allocator.Temp))
            {
                if (configurationIndex >= configurations.Length)
                {
                    return;
                }

                // Get that configuration by index
                var configuration = configurations[configurationIndex];

                // Make it the active one
                cameraManager.currentConfiguration = configuration;
            }
        }

        void Awake()
        {
            m_Dropdown = GetComponent<Dropdown>();
            m_Dropdown.ClearOptions();
            m_Dropdown.onValueChanged.AddListener(delegate { OnDropdownValueChanged(m_Dropdown); });
            m_ConfigurationNames = new List<string>();
        }

        void PopulateDropdown()
        {
            if ((cameraManager == null) || (cameraManager.subsystem == null) || !cameraManager.subsystem.running)
                return;

            m_ConfigurationNames.Clear();
            using (var configurations = cameraManager.GetConfigurations(Allocator.Temp))
            {
                foreach (var config in configurations)
                {
                    if (config.width == 1920 && config.height == 1080 && config.framerate.HasValue && config.framerate.Value == 30)
                    {
                        m_ConfigurationNames.Add($"{config.width}x{config.height} at {config.framerate.Value} Hz");
                        cameraManager.currentConfiguration = config;
                    }
                }
                m_Dropdown.AddOptions(m_ConfigurationNames);
            }
        }

        void Update()
        {
            if (m_ConfigurationNames.Count == 0)
                PopulateDropdown();
        }
    }
}
